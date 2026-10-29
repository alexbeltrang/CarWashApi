using Carwash.API.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<IActionResult> Receive(
        [FromBody] WhatsAppWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        foreach (var status in payload.Entry.SelectMany(entry => entry.Changes)
                     .SelectMany(change => change.Value.Statuses))
        {
            var notification = await context.WhatsAppNotifications
                .SingleOrDefaultAsync(item => item.MessageIdExterno == status.Id, cancellationToken);
            if (notification is null)
                continue;

            notification.Estado = status.Status switch
            {
                "sent" or "read" => notification.Estado == "DELIVERED" ? "DELIVERED" : "ACCEPTED",
                "delivered" => "DELIVERED",
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
