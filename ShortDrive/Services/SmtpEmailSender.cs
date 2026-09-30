using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using ShortDrive.Data;
using ShortDrive.Models;

namespace ShortDrive.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IServiceProvider sp, IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _sp = sp;
        _config = config;
        _logger = logger;
    }

    private sealed record SmtpConfig(
        string? Host, int Port, string? User, string? Pass, string FromEmail, string FromName, bool UseSsl)
    {
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(User);

        public SmtpClient CreateClient() => new(Host, Port)
        {
            Credentials = new NetworkCredential(User, Pass),
            EnableSsl = UseSsl
        };
    }

    // Admin-panel settings take precedence over appsettings.json.
    private SmtpConfig ResolveSmtp(PricingSettings? settings)
    {
        var configPort = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        return new SmtpConfig(
            settings?.SmtpHost ?? _config["Smtp:Host"],
            settings?.SmtpPort > 0 ? settings.SmtpPort : configPort,
            settings?.SmtpUsername ?? _config["Smtp:Username"],
            settings?.SmtpPassword ?? _config["Smtp:Password"],
            settings?.SmtpFromEmail ?? _config["Smtp:FromEmail"] ?? "noreply@drive-flex.co.uk",
            settings?.SmtpFromName ?? _config["Smtp:FromName"] ?? "Drive-Flex Insurance",
            settings?.SmtpUseSsl ?? true);
    }

    public async Task SendCertificateEmailAsync(Quote quote, string toEmail, CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.PricingSettings.FirstOrDefaultAsync(ct);

        var smtp = ResolveSmtp(settings);
        var fromEmail = smtp.FromEmail;
        var fromName = smtp.FromName;

        if (!smtp.IsConfigured)
        {
            _logger.LogWarning("SMTP not configured — falling back to log-only for {Email}", toEmail);
            LogCertificate(quote, toEmail);
            return;
        }

        var driverName = $"{quote.Driver?.FirstName} {quote.Driver?.LastName}";
        var vehicle = $"{quote.Vehicle?.Make} {quote.Vehicle?.Model} ({quote.Vehicle?.Registration})";
        var coverStart = UkTime.FromUtc(quote.CoverStartUtc);
        var coverEnd = UkTime.FromUtc(quote.CoverStartUtc.AddHours(quote.DurationHours));
        var underwriter = settings?.UnderwriterName ?? "ERS Syndicate 218 at Lloyd's";
        var fca = settings?.FcaFirmReference ?? "612248";

        var body = $"""
            <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px;">
                <div style="text-align: center; padding-bottom: 16px; border-bottom: 2px solid #1D4ED8;">
                    <h1 style="color: #0F172A; margin: 0; font-size: 24px;">
                        <span>Drive</span><span style="color: #1D4ED8;">Flex</span>
                    </h1>
                    <p style="color: #64748B; font-size: 13px; margin-top: 4px;">Certificate of Motor Insurance</p>
                </div>

                <div style="padding: 24px 0;">
                    <h2 style="color: #059669; font-size: 18px; margin-bottom: 8px;">✓ Payment Confirmed</h2>
                    <p style="color: #475569; font-size: 14px; margin: 0;">
                        Your temporary motor insurance policy is now active and registered on the Motor Insurance Database (MID).
                    </p>
                </div>

                <table style="width: 100%; border-collapse: collapse; font-size: 14px; margin-bottom: 24px;">
                    <tr style="border-bottom: 1px solid #E2E8F0;">
                        <td style="padding: 10px 0; color: #64748B; width: 40%;">Policy Number</td>
                        <td style="padding: 10px 0; font-weight: 700;">{quote.PolicyNumber}</td>
                    </tr>
                    <tr style="border-bottom: 1px solid #E2E8F0;">
                        <td style="padding: 10px 0; color: #64748B;">Policyholder</td>
                        <td style="padding: 10px 0; font-weight: 600;">{driverName}</td>
                    </tr>
                    <tr style="border-bottom: 1px solid #E2E8F0;">
                        <td style="padding: 10px 0; color: #64748B;">Vehicle</td>
                        <td style="padding: 10px 0; font-weight: 600;">{vehicle}</td>
                    </tr>
                    <tr style="border-bottom: 1px solid #E2E8F0;">
                        <td style="padding: 10px 0; color: #64748B;">Cover Period</td>
                        <td style="padding: 10px 0;">{coverStart:dd MMM yyyy HH:mm} – {coverEnd:dd MMM yyyy HH:mm} (UK time)</td>
                    </tr>
                    <tr style="border-bottom: 1px solid #E2E8F0;">
                        <td style="padding: 10px 0; color: #64748B;">Premium Paid</td>
                        <td style="padding: 10px 0; font-weight: 700; color: #059669;">£{quote.TotalPremium:0.00}</td>
                    </tr>
                    <tr>
                        <td style="padding: 10px 0; color: #64748B;">MID Status</td>
                        <td style="padding: 10px 0; font-weight: 700; color: #059669;">ACTIVE</td>
                    </tr>
                </table>

                <div style="background: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 16px; font-size: 12px; color: #64748B; line-height: 1.6;">
                    <p style="margin: 0 0 8px 0;">
                        Underwritten by {underwriter}. Authorised and regulated by the Financial Conduct Authority (FCA Firm Reference: {fca}).
                    </p>
                    <p style="margin: 0;">
                        This document serves as your Certificate of Motor Insurance. Your cover details have been registered on the Motor Insurance Database (MID) and are accessible to police and ANPR systems.
                    </p>
                </div>

                <div style="text-align: center; padding-top: 24px; font-size: 11px; color: #94A3B8;">
                    Drive-Flex · Temporary Motor Insurance · United Kingdom
                </div>
            </div>
            """;

        try
        {
            using var client = smtp.CreateClient();

            var plainText =
                $"""
                Drive-Flex — Certificate of Motor Insurance

                Payment confirmed. Your temporary motor insurance policy is now active and
                registered on the Motor Insurance Database (MID).

                Policy Number: {quote.PolicyNumber}
                Policyholder:  {driverName}
                Vehicle:       {vehicle}
                Cover Period:  {coverStart:dd MMM yyyy HH:mm} - {coverEnd:dd MMM yyyy HH:mm} (UK time)
                Premium Paid:  £{quote.TotalPremium:0.00}
                MID Status:    ACTIVE

                Underwritten by {underwriter}. Authorised and regulated by the Financial Conduct
                Authority (FCA Firm Reference: {fca}).

                This document serves as your Certificate of Motor Insurance.

                Drive-Flex · Temporary Motor Insurance · United Kingdom
                """;

            var msg = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = $"Your Drive-Flex Policy {quote.PolicyNumber} – Certificate of Motor Insurance"
            };
            msg.To.Add(new MailAddress(toEmail, driverName));
            // Replies go back to the sending address rather than being undeliverable.
            msg.ReplyToList.Add(new MailAddress(fromEmail, fromName));
            // Send BOTH plain-text and HTML (multipart/alternative). HTML-only mail is far more
            // likely to be filtered as spam; a text part improves deliverability significantly.
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainText, null, "text/plain"));
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(body, null, "text/html"));

            await client.SendMailAsync(msg, ct);
            _logger.LogInformation("Certificate email sent to {Email} for policy {Policy}", toEmail, quote.PolicyNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send certificate email to {Email} for policy {Policy}", toEmail, quote.PolicyNumber);
            LogCertificate(quote, toEmail);
        }
    }

    public async Task SendOwnerSaleNotificationAsync(Quote quote, CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.PricingSettings.FirstOrDefaultAsync(ct);

        var ownerEmail = settings?.OwnerNotifyEmail;
        if (string.IsNullOrWhiteSpace(ownerEmail))
            return;

        var smtp = ResolveSmtp(settings);
        var customerName = $"{quote.Driver?.FirstName} {quote.Driver?.LastName}".Trim();
        var customerEmail = string.IsNullOrWhiteSpace(quote.CustomerEmail) ? "(not given)" : quote.CustomerEmail;
        var amount = $"£{quote.TotalPremium:0.00}";
        var policy = quote.PolicyNumber ?? $"Q{quote.Id}";

        if (!smtp.IsConfigured)
        {
            _logger.LogInformation(
                "[OWNER SALE ALERT - LOGGED ONLY] Policy {Policy}: {Amount} from {Name} <{Email}>",
                policy, amount, customerName, customerEmail);
            return;
        }

        var vehicle = $"{quote.Vehicle?.Make} {quote.Vehicle?.Model} ({quote.Vehicle?.Registration})";
        var coverStart = UkTime.FromUtc(quote.CoverStartUtc);
        var coverEnd = UkTime.FromUtc(quote.CoverStartUtc.AddHours(quote.DurationHours));
        var cover = $"{quote.VehicleClass}, {quote.Usage} — {coverStart:dd MMM yyyy HH:mm} to {coverEnd:dd MMM yyyy HH:mm}";
        var paidAt = UkTime.FromUtc(quote.CertificateIssuedAt ?? DateTime.UtcNow);

        // Flag Stripe test-mode payments so a test purchase isn't mistaken for real money.
        var secretKey = !string.IsNullOrWhiteSpace(settings?.StripeSecretKey)
            ? settings.StripeSecretKey
            : _config["Stripe:SecretKey"];
        var testTag = secretKey?.StartsWith("sk_test_") == true ? "[TEST] " : "";

        // Customer-typed values are HTML-encoded before going into the email body.
        static string Row(string label, string value, string style = "") =>
            "<tr style=\"border-bottom: 1px solid #E2E8F0;\">" +
            $"<td style=\"padding: 8px 0; color: #64748B; width: 40%;\">{label}</td>" +
            $"<td style=\"padding: 8px 0; {style}\">{WebUtility.HtmlEncode(value)}</td></tr>";

        var html = $"""
            <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px;">
                <h2 style="color: #059669; font-size: 20px; margin: 0 0 4px 0;">{testTag}New policy sold — {amount}</h2>
                <p style="color: #64748B; font-size: 13px; margin: 0 0 16px 0;">A customer has just paid on Drive-Flex.</p>
                <table style="width: 100%; border-collapse: collapse; font-size: 14px;">
                    {Row("Amount paid", amount, "font-weight: 700; color: #059669;")}
                    {Row("Customer name", customerName, "font-weight: 600;")}
                    {Row("Customer email", customerEmail, "font-weight: 600;")}
                    {Row("Policy number", policy)}
                    {Row("Vehicle", vehicle)}
                    {Row("Cover", cover)}
                    {Row("Paid at", $"{paidAt:dd MMM yyyy HH:mm} (UK time)")}
                </table>
            </div>
            """;

        var plainText =
            $"""
            {testTag}New policy sold — {amount}

            Amount paid:    {amount}
            Customer name:  {customerName}
            Customer email: {customerEmail}
            Policy number:  {policy}
            Vehicle:        {vehicle}
            Cover:          {cover}
            Paid at:        {paidAt:dd MMM yyyy HH:mm} (UK time)
            """;

        try
        {
            using var client = smtp.CreateClient();
            var msg = new MailMessage
            {
                From = new MailAddress(smtp.FromEmail, smtp.FromName),
                Subject = $"{testTag}New sale: {amount} — {customerName} ({policy})"
            };
            msg.To.Add(ownerEmail);
            // Hitting Reply goes straight to the customer.
            if (MailAddress.TryCreate(quote.CustomerEmail, customerName, out var replyTo))
                msg.ReplyToList.Add(replyTo);
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainText, null, "text/plain"));
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, null, "text/html"));

            await client.SendMailAsync(msg, ct);
            _logger.LogInformation("Owner sale alert sent to {Owner} for policy {Policy}", ownerEmail, policy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send owner sale alert for policy {Policy}", policy);
        }
    }

    public async Task<(bool ok, string message)> SendTestEmailAsync(
        string? host, int port, string? username, string? password,
        string? fromEmail, string? fromName, bool useSsl, string toEmail,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return (false, "SMTP host is required.");
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
            return (false, "Enter a valid recipient email address to test.");

        var from = string.IsNullOrWhiteSpace(fromEmail) ? "noreply@drive-flex.co.uk" : fromEmail;
        var fromDisplay = string.IsNullOrWhiteSpace(fromName) ? "Drive-Flex Insurance" : fromName;

        try
        {
            using var client = new SmtpClient(host, port > 0 ? port : 587)
            {
                Credentials = string.IsNullOrWhiteSpace(username) ? null : new NetworkCredential(username, password),
                EnableSsl = useSsl
            };

            const string testPlain = "This is a test email from the Drive-Flex admin panel. " +
                "If you received this, your SMTP settings are working correctly.";
            const string testHtml = "<p>This is a test email from the Drive-Flex admin panel. " +
                "If you received this, your SMTP settings are working correctly.</p>";

            var msg = new MailMessage
            {
                From = new MailAddress(from, fromDisplay),
                Subject = "Drive-Flex SMTP test"
            };
            msg.To.Add(toEmail);
            msg.ReplyToList.Add(new MailAddress(from, fromDisplay));
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(testPlain, null, "text/plain"));
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(testHtml, null, "text/html"));

            await client.SendMailAsync(msg, ct);
            _logger.LogInformation("SMTP test email sent to {Email} via {Host}", toEmail, host);
            return (true, $"Success — test email sent to {toEmail} via {host}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP test failed for host {Host}", host);
            return (false, $"Failed: {ex.Message}");
        }
    }

    private void LogCertificate(Quote quote, string toEmail)
    {
        var driverName = $"{quote.Driver?.FirstName} {quote.Driver?.LastName}";
        var vehicle = $"{quote.Vehicle?.Make} {quote.Vehicle?.Model} ({quote.Vehicle?.Registration})";
        var coverStart = UkTime.FromUtc(quote.CoverStartUtc);
        var coverEnd = UkTime.FromUtc(quote.CoverStartUtc.AddHours(quote.DurationHours));
        _logger.LogInformation(
            "[EMAIL FALLBACK - LOGGED ONLY]\n To: {Email} ({Driver})\n Policy: {Policy}\n Vehicle: {Vehicle}\n Period: {Start:dd/MM/yyyy HH:mm} – {End:dd/MM/yyyy HH:mm} (UK time)\n Premium: £{Total:0.00}",
            toEmail, driverName, quote.PolicyNumber, vehicle, coverStart, coverEnd, quote.TotalPremium);
    }
}
