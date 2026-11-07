namespace Carwash.API.Services;

public sealed record EmailSendResult(bool Accepted, bool Transient, string? ErrorCode);
public sealed record EmailBody(string Text, string? Html = null, string? LogoBase64 = null, string? SenderName = null);

public interface IEmailClient
{
    Task<EmailSendResult> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken);

    Task<EmailSendResult> SendAsync(
        string recipient,
        string subject,
        EmailBody body,
        CancellationToken cancellationToken) => SendAsync(recipient, subject, body.Text, cancellationToken);
}
