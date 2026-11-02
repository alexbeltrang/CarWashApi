namespace Carwash.API.Services;

public sealed record EmailSendResult(bool Accepted, bool Transient, string? ErrorCode);

public interface IEmailClient
{
    Task<EmailSendResult> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken);
}
