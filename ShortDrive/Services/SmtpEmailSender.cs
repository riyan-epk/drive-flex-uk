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

    public async Task SendCertificateEmailAsync(Quote quote, string toEmail, CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.PricingSettings.FirstOrDefaultAsync(ct);

        var host = settings?.SmtpHost ?? _config["Smtp:Host"];
        var configPort = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        var port = settings?.SmtpPort > 0 ? settings.SmtpPort : configPort;
        var user = settings?.SmtpUsername ?? _config["Smtp:Username"];
        var pass = settings?.SmtpPassword ?? _config["Smtp:Password"];
        var fromEmail = settings?.SmtpFromEmail ?? _config["Smtp:FromEmail"] ?? "noreply@drive-flex.co.uk";
        var fromName = settings?.SmtpFromName ?? _config["Smtp:FromName"] ?? "Drive-Flex Insurance";
        var useSsl = settings?.SmtpUseSsl ?? true;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
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
            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(user, pass),
                EnableSsl = useSsl
            };

            var msg = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = $"Your Drive-Flex Policy {quote.PolicyNumber} – Certificate of Motor Insurance",
                Body = body,
                IsBodyHtml = true
            };
            msg.To.Add(new MailAddress(toEmail, driverName));

            await client.SendMailAsync(msg, ct);
            _logger.LogInformation("Certificate email sent to {Email} for policy {Policy}", toEmail, quote.PolicyNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send certificate email to {Email} for policy {Policy}", toEmail, quote.PolicyNumber);
            LogCertificate(quote, toEmail);
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
