using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShortDrive.Data;

namespace ShortDrive.Services;

public class PaymentService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(AppDbContext db, IConfiguration config, HttpClient http, ILogger<PaymentService> logger)
    {
        _db = db;
        _config = config;
        _http = http;
        _logger = logger;
    }

    public async Task<string> CreateCheckoutSessionAsync(
        int quoteId,
        decimal totalPremium,
        string durationLabel,
        string vehicleReg,
        string? customerEmail = null,
        CancellationToken ct = default)
    {
        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5000";

        var settings = await _db.PricingSettings.FirstOrDefaultAsync(ct);
        var secretKey = !string.IsNullOrWhiteSpace(settings?.StripeSecretKey)
            ? settings.StripeSecretKey
            : _config["Stripe:SecretKey"];

        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Stripe secret key is not configured. Please set it in the Admin Panel or environment.");

        var unitAmountPence = (long)(totalPremium * 100);

        // Omit payment_method_types so Stripe uses "automatic payment methods" —
        // all card brands (Visa, Mastercard, Amex, JCB, UnionPay, Diners, Discover),
        // Apple Pay, Google Pay, and any other methods enabled in the Stripe Dashboard
        // are shown automatically, including to international customers.
        var form = new List<KeyValuePair<string, string>>
        {
            new("line_items[0][price_data][currency]", "gbp"),
            new("line_items[0][price_data][unit_amount]", unitAmountPence.ToString()),
            new("line_items[0][price_data][product_data][name]", $"Drive-Flex · {durationLabel} Cover ({vehicleReg})"),
            new("line_items[0][price_data][product_data][description]", "Temporary comprehensive motor insurance"),
            new("line_items[0][quantity]", "1"),
            new("mode", "payment"),
            new("locale", "auto"),
            new("success_url", $"{baseUrl}/quote/success?session_id={{CHECKOUT_SESSION_ID}}&quote_id={quoteId}"),
            new("cancel_url", $"{baseUrl}/quote"),
            new("metadata[quote_id]", quoteId.ToString())
        };

        // Pre-fill the customer's email on the Stripe Checkout page so they
        // don't have to re-type it (smoother experience for all customers).
        if (!string.IsNullOrWhiteSpace(customerEmail))
            form.Add(new("customer_email", customerEmail.Trim()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Stripe API error: {StatusCode} {ResponseBody}", response.StatusCode, responseBody);
            throw new InvalidOperationException("Payment service is temporarily unavailable. Please try again.");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var sessionId = doc.RootElement.GetProperty("id").GetString() ?? "";
        var checkoutUrl = doc.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("No checkout URL returned from payment provider.");

        var quote = await _db.Quotes.FindAsync(new object[] { quoteId }, ct);
        if (quote is not null)
        {
            quote.StripeSessionId = sessionId;
            quote.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Stripe session {SessionId} created for quote {QuoteId}", sessionId, quoteId);
        return checkoutUrl;
    }

    /// <summary>
    /// Validates a Stripe secret key by calling the Stripe API. Uses the supplied key, or the
    /// saved/config key when blank. Returns a human-readable result for the admin panel.
    /// </summary>
    public async Task<(bool ok, string message)> TestStripeKeyAsync(string? secretKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            var settings = await _db.PricingSettings.FirstOrDefaultAsync(ct);
            secretKey = !string.IsNullOrWhiteSpace(settings?.StripeSecretKey)
                ? settings.StripeSecretKey
                : _config["Stripe:SecretKey"];
        }

        if (string.IsNullOrWhiteSpace(secretKey))
            return (false, "No Stripe secret key provided.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.stripe.com/v1/balance");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var mode = secretKey.StartsWith("sk_live", StringComparison.Ordinal) ? "LIVE" : "TEST";
                return (true, $"Success — Stripe secret key is valid ({mode} mode).");
            }
            if ((int)response.StatusCode == 401)
                return (false, "Invalid Stripe secret key (401 Unauthorized).");
            return (false, $"Stripe returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach Stripe: {ex.Message}");
        }
    }

    public async Task<bool> VerifySessionPaidAsync(string sessionId, CancellationToken ct = default)
    {
        var settings = await _db.PricingSettings.FirstOrDefaultAsync(ct);
        var secretKey = !string.IsNullOrWhiteSpace(settings?.StripeSecretKey)
            ? settings.StripeSecretKey
            : _config["Stripe:SecretKey"];

        if (string.IsNullOrWhiteSpace(secretKey))
            return false;

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.stripe.com/v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe session verification failed for {SessionId}: {StatusCode}", sessionId, response.StatusCode);
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var paymentStatus = doc.RootElement.GetProperty("payment_status").GetString();
        return paymentStatus == "paid";
    }
}
