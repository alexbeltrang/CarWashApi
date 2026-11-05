using Carwash.API.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Carwash.API.Controllers;

[ApiController]
[Route("api/webhooks/whatsapp")]
public sealed class WhatsAppWebhookController(
    AppDbContext context,
    IConfiguration configuration,
    ILogger<WhatsAppWebhookController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = configuration["WhatsApp:WebhookVerifyToken"];
        return mode == "subscribe" &&
               !string.IsNullOrWhiteSpace(expected) &&
               verifyToken == expected &&
               !string.IsNullOrWhiteSpace(challenge)
            ? Ok(challenge)
            : Forbid();
    }

    [HttpPost]
    [RequestSizeLimit(65536)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Receive(
        CancellationToken cancellationToken)
    {
        var secret = configuration["WhatsApp:AppSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Webhook no configurado.");
        var signature = Request.Headers["X-Hub-Signature-256"].ToString();
        if (!signature.StartsWith("sha256=", StringComparison.Ordinal) || signature.Length != 71)
            return Unauthorized();
        byte[] provided;
        try { provided = Convert.FromHexString(signature[7..]); }
        catch (FormatException) { return Unauthorized(); }
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
            return Unauthorized();
        WhatsAppWebhookPayload? payload;
        try { payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return BadRequest(); }
        if (payload?.Entry is null) return BadRequest();
        if (payload.Entry.Any(entry => entry is null || entry.Changes is null ||
            entry.Changes.Any(change => change?.Value?.Statuses is null || change.Value.Statuses.Any(status => status is null))))
            return BadRequest();
        foreach (var status in payload.Entry.SelectMany(entry => entry.Changes)
                     .SelectMany(change => change.Value.Statuses))
        {
            var notification = await context.WhatsAppNotifications
                .SingleOrDefaultAsync(item => item.MessageIdExterno == status.Id, cancellationToken);
            if (notification is null)
                continue;

            notification.Estado = status.Status switch
            {
                "sent" => notification.Estado == "DELIVERED" ? "DELIVERED" : "ACCEPTED",
                "delivered" or "read" => "DELIVERED",
                _ => notification.Estado,
            };
            notification.ActualizadoEn = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Webhook WhatsApp procesado.");
        return Ok();
    }
}

public sealed class WhatsAppWebhookPayload
{
    public List<WhatsAppWebhookEntry> Entry { get; set; } = [];
}

public sealed class WhatsAppWebhookEntry
{
    public List<WhatsAppWebhookChange> Changes { get; set; } = [];
}

public sealed class WhatsAppWebhookChange
{
    public WhatsAppWebhookValue Value { get; set; } = new();
}

public sealed class WhatsAppWebhookValue
{
    public List<WhatsAppWebhookStatus> Statuses { get; set; } = [];
}

public sealed class WhatsAppWebhookStatus
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
