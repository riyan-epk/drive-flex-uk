using ShortDrive.Models;

namespace ShortDrive.Services;

public interface IEmailSender
{
    Task SendCertificateEmailAsync(
        Quote quote,
        string toEmail,
        CancellationToken ct = default);

    /// <summary>
    /// Tells the business owner a policy was just paid for (amount, customer name and email).
    /// Does nothing if no owner notification address is set in the admin panel.
    /// </summary>
    Task SendOwnerSaleNotificationAsync(Quote quote, CancellationToken ct = default);

    /// <summary>
    /// Sends a test email using the supplied SMTP settings, for the admin "test connection"
    /// feature. Returns a human-readable success/error result.
    /// </summary>
    Task<(bool ok, string message)> SendTestEmailAsync(
        string? host, int port, string? username, string? password,
        string? fromEmail, string? fromName, bool useSsl, string toEmail,
        CancellationToken ct = default);
}
