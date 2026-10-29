using Carwash.API.DTOs;
using Carwash.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Carwash.API.Controllers;

[ApiController]
[Route("api/Turnos/{idTurno:int}/notificacion-whatsapp")]
public sealed class WhatsAppNotificationController(WhatsAppNotificationService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WhatsAppNotificationResult>> Notify(
        int idTurno,
        CancellationToken cancellationToken)
    {
        var result = await service.NotifyAsync(idTurno, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("reintentar")]
    public async Task<ActionResult<WhatsAppNotificationResult>> Retry(
        int idTurno,
        CancellationToken cancellationToken)
    {
        var result = await service.RetryAsync(idTurno, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<WhatsAppNotificationResult>> Get(
        int idTurno,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(idTurno, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
