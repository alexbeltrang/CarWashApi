namespace Carwash.API.Services;

public sealed record WhatsAppSendResult(
    bool Accepted,
    bool Transient,
    string? MessageId,
    string? FbTraceId,
    string? ErrorCode);

public interface IWhatsAppClient
{
    Task<WhatsAppSendResult> SendTemplateAsync(
        string recipient,
        string templateName,
        string language,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken);
}
