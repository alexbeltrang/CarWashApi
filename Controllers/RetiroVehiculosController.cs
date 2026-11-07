using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using Carwash.API.DTOs;
using Carwash.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Carwash.API.Controllers;

[ApiController]
[Route("api/RetiroVehiculos")]
[Authorize(AuthenticationSchemes = SessionAuthenticationHandler.SchemeName)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class RetiroVehiculosController(RetiroVehiculosService service, IImpresionComprobantesService printing) : ControllerBase
{
    [HttpPost("comprobante")]
    [ProducesResponseType(typeof(ComprobanteRetiroDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ComprobanteRetiroDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Issue(EmitirComprobanteRequest request, CancellationToken cancellationToken) => Execute(async () =>
    {
        var result = await service.IssueAsync(request, cancellationToken);
        return result.Created
            ? CreatedAtAction(nameof(Get), new { idTurno = result.Receipt.IdTurno }, result.Receipt)
            : Ok(result.Receipt);
    });

    [HttpGet("{idTurno:int}")]
    [ProducesResponseType(typeof(ComprobanteRetiroDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(int idTurno, CancellationToken cancellationToken) => Execute(async () =>
        Ok(await service.GetAsync(idTurno, cancellationToken)));

    [HttpPost("{idTurno:int}/entrega")]
    [ProducesResponseType(typeof(ComprobanteRetiroDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Confirm(int idTurno, ConfirmarRetiroRequest request, CancellationToken cancellationToken) => Execute(async () =>
        Ok(await service.ConfirmAsync(idTurno, request,
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture), cancellationToken)));

    [HttpPost("{idTurno:int}/impresiones")]
    [ProducesResponseType(typeof(ImpresionComprobanteDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Print(int idTurno, SolicitarImpresionRequest request, CancellationToken cancellationToken) => Execute(async () =>
        AcceptedAtAction(nameof(GetPrint), new { solicitudId = request.SolicitudId }, await printing.RequestAsync(idTurno, request,
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture), cancellationToken)));

    [HttpGet("impresiones/{solicitudId:guid}")]
    [ProducesResponseType(typeof(ImpresionComprobanteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetPrint(Guid solicitudId, CancellationToken cancellationToken) => Execute(async () =>
        Ok(await printing.GetAsync(solicitudId, cancellationToken)));

    [HttpPost("{idTurno:int}/impresiones/ingreso")]
    [ProducesResponseType(typeof(ImpresionComprobanteDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> PrintEntry(int idTurno, ImprimirIngresoRequest request, CancellationToken cancellationToken) => Execute(async () =>
    {
        var job = await printing.EntryAsync(idTurno, request,
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture), cancellationToken);
        return AcceptedAtAction(nameof(GetPrint), new { solicitudId = job.SolicitudId }, job);
    });

    [HttpGet("{idTurno:int}/impresiones/ultima")]
    [ProducesResponseType(typeof(ImpresionComprobanteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> LatestPrint(int idTurno, CancellationToken cancellationToken) => Execute(async () =>
    {
        var job = await printing.LatestAsync(idTurno, cancellationToken);
        return job is null ? NoContent() : Ok(job);
    });

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (RetiroException error)
        {
            var problem = new ProblemDetails { Status = error.Status, Title = "No se pudo completar la operacion de retiro." };
            problem.Extensions["code"] = error.Code;
            return new ObjectResult(problem) { StatusCode = error.Status, ContentTypes = { "application/problem+json" } };
        }
        catch (DbException)
        {
            return Problem(statusCode: 503, title: "Retiro no disponible. Consulte el estado antes de reintentar.");
        }
    }
}