using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ShortDrive.Components;
using ShortDrive.Data;
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
}

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

app.Run();
