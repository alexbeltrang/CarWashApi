using Carwash.API.DTOs;
using Carwash.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Carwash.API.Controllers;

[ApiController]
[Route("api/Turnos/{idTurno:int}/notificacion-email")]
public sealed class EmailNotificationController(EmailNotificationService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<EmailNotificationResult>> Notify(int idTurno, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.NotifyAsync(idTurno, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (EmailNotificationConflictException)
        {
            return Conflict();
        }
    }

    [HttpPost("reintentar")]
    public async Task<ActionResult<EmailNotificationResult>> Retry(int idTurno, CancellationToken cancellationToken)
    {
        var result = await service.RetryAsync(idTurno, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<EmailNotificationResult>> Get(int idTurno, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(idTurno, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
