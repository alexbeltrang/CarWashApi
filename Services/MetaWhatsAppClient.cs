using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Carwash.API.Configuration;
using Microsoft.Extensions.Options;

namespace Carwash.API.Services;

public sealed class MetaWhatsAppClient(
    HttpClient httpClient,
    IOptions<WhatsAppOptions> options,
    ILogger<MetaWhatsAppClient> logger) : IWhatsAppClient
{
    public async Task<WhatsAppSendResult> SendTemplateAsync(
        string recipient,
        string templateName,
        string language,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.PhoneNumberId) ||
            string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return new(false, false, null, null, "CONFIGURATION_MISSING");
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = recipient,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = language },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = parameters.Select(text => new { type = "text", text }).ToArray(),
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.GraphVersion.Trim('/')}/{settings.PhoneNumberId}/messages")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var traceId = root.TryGetProperty("fbtrace_id", out var trace) ? trace.GetString() : null;

            if (response.IsSuccessStatusCode &&
                root.TryGetProperty("messages", out var messages) &&
                messages.GetArrayLength() > 0 &&
                messages[0].TryGetProperty("id", out var messageId))
            {
                return new(true, false, messageId.GetString(), traceId, null);
            }

            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement
                : default;
            var code = error.ValueKind == JsonValueKind.Object &&
                       error.TryGetProperty("code", out var errorCode)
                ? errorCode.ToString()
                : $"HTTP_{(int)response.StatusCode}";
            var transient = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
            return new(false, transient, null, traceId, code);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, true, null, null, "TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return new(false, true, null, null, "NETWORK");
        }
        catch (JsonException)
        {
            logger.LogWarning("Meta WhatsApp devolvió una respuesta no interpretable.");
            return new(false, false, null, null, "INVALID_RESPONSE");
        }
    }
}
