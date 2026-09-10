using ShortDrive.Models;

namespace ShortDrive.Services;

public interface IEmailSender
{
    Task SendCertificateEmailAsync(
        Quote quote,
        string toEmail,
        CancellationToken ct = default);
}
