using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ShortDrive.Components;
using ShortDrive.Data;
using ShortDrive.Models;
using ShortDrive.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Persistent SQLite database — quotes, policies, drivers and admin-managed settings
// (Stripe/DVSA/SMTP keys, admin password) survive application restarts.
// The DB file location comes from ConnectionStrings:DefaultConnection (default driveflex.db).
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=driveflex.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connStr));

builder.Services.AddMemoryCache();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("vehicle-lookup", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
});

builder.Services.Configure<DvsaOptions>(builder.Configuration.GetSection("Dvsa"));
builder.Services.AddHttpClient<DvsaClient>();

builder.Services.AddScoped<QuoteService>();
builder.Services.AddHttpClient<PaymentService>();
builder.Services.AddSingleton<VehicleLookupRateLimiter>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<AdminAuthService>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("OK"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    // EnsureCreated does not add new tables to an already-created SQLite database, so create the
    // SitePages table if it is missing (safe no-op on fresh databases), then seed default content.
    await db.Database.ExecuteSqlRawAsync(
        "CREATE TABLE IF NOT EXISTS \"SitePages\" (" +
        "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_SitePages\" PRIMARY KEY AUTOINCREMENT, " +
        "\"Slug\" TEXT NOT NULL, \"Title\" TEXT NOT NULL, \"ContentHtml\" TEXT NOT NULL, " +
        "\"UpdatedAt\" TEXT NOT NULL);");

    // Add StripeWebhookSecret column if missing (safe no-op on fresh databases).
    try { await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE \"PricingSettings\" ADD COLUMN \"StripeWebhookSecret\" TEXT;"); }
    catch { /* column already exists */ }

    await SiteContent.SeedAsync(db);
}

// Security response headers on every response (defence-in-depth against clickjacking, MIME
// sniffing, referrer leakage, and unwanted browser features). frame-ancestors blocks the site
// from being embedded in an <iframe> on another domain.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    headers["Content-Security-Policy"] = "frame-ancestors 'self'";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHealthChecks("/healthz");

// ── Stripe webhook endpoint ─────────────────────────────────────────────────
// Receives checkout.session.completed events so payment confirmation does not
// rely solely on the customer landing on the success page.
app.MapPost("/webhook/stripe", async (HttpContext context) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("StripeWebhook");

    // Read raw body (must happen before anything else consumes the stream).
    string json;
    using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8))
        json = await reader.ReadToEndAsync();

    // Resolve webhook secret from DB (admin-configurable).
    using var scope = context.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var settings = await db.PricingSettings.FirstOrDefaultAsync();
    var webhookSecret = settings?.StripeWebhookSecret;

    // Verify Stripe signature when a webhook secret is configured.
    if (!string.IsNullOrWhiteSpace(webhookSecret))
    {
        var sigHeader = context.Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sigHeader) || !VerifyStripeSignature(json, sigHeader, webhookSecret))
        {
            logger.LogWarning("Stripe webhook signature verification failed");
            return Results.BadRequest("Invalid signature");
        }
    }

    // Parse the event.
    using var doc = JsonDocument.Parse(json);
    var eventType = doc.RootElement.GetProperty("type").GetString();

    if (eventType == "checkout.session.completed")
    {
        var session = doc.RootElement.GetProperty("data").GetProperty("object");
        var sessionId = session.GetProperty("id").GetString();
        var paymentStatus = session.GetProperty("payment_status").GetString();

        if (paymentStatus == "paid" && sessionId is not null)
        {
            var quote = await db.Quotes
                .Include(q => q.Vehicle)
                .Include(q => q.Driver)
                .FirstOrDefaultAsync(q => q.StripeSessionId == sessionId);

            if (quote is not null && quote.Status != QuoteStatus.Purchased)
            {
                quote.Status = QuoteStatus.Purchased;
                quote.CertificateIssuedAt = DateTime.UtcNow;
                quote.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                logger.LogInformation("Webhook: Quote {QuoteId} marked Purchased via checkout.session.completed", quote.Id);

                // Send the certificate email so the customer gets it even if they
                // closed the browser before the success page loaded.
                var email = quote.CustomerEmail;
                if (!string.IsNullOrWhiteSpace(email))
                {
                    try
                    {
                        var emailSvc = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                        await emailSvc.SendCertificateEmailAsync(quote, email);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Webhook: failed to send certificate email for quote {QuoteId}", quote.Id);
                    }
                }
            }
        }
    }

    return Results.Ok();
});

// Stripe webhook signature verification (HMAC-SHA256).
static bool VerifyStripeSignature(string payload, string header, string secret)
{
    string? timestamp = null, signature = null;
    foreach (var part in header.Split(','))
    {
        if (part.StartsWith("t=")) timestamp = part[2..];
        else if (part.StartsWith("v1=")) signature = part[3..];
    }
    if (timestamp is null || signature is null) return false;

    var signedPayload = $"{timestamp}.{payload}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
    var computed = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    return string.Equals(computed, signature, StringComparison.OrdinalIgnoreCase);
}

app.Run();
