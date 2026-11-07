using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Carwash.API.DTOs;
using Carwash.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers;

[ApiController]
[Route("api/Facturas")]
[Authorize(AuthenticationSchemes = SessionAuthenticationHandler.SchemeName)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class FacturasController(IFacturacionService service, IImpresionComprobantesService printing) : ControllerBase
{
    [HttpGet("configuracion")]
    [ProducesResponseType(typeof(ConfiguracionFacturacionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Configuration(CancellationToken token) => Execute(async () =>
    {
        var result = await service.ConfigurationAsync(token);
        return result is null ? NoContent() : Ok(result);
    });

    [HttpPut("configuracion")]
    [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(typeof(ConfiguracionFacturacionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> SaveConfiguration(ConfiguracionFacturacionDto request, CancellationToken token) =>
        Execute(async () => Ok(await service.SaveConfigurationAsync(request, token)));

    [HttpGet("preparacion")]
    [ProducesResponseType(typeof(PreparacionFacturaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Prepare([FromQuery] int? idTurno, [FromQuery] string? placa, CancellationToken token) =>
        Execute(async () => Ok(await service.PrepareAsync(idTurno, placa, token)));

    [HttpGet]
    [ProducesResponseType(typeof(List<FacturaDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] string? placa, CancellationToken token) => Execute(async () => Ok(await service.ListAsync(placa, token)));

    [HttpGet("turnos/{idTurno:int}")]
    [ProducesResponseType(typeof(FacturaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Get(int idTurno, CancellationToken token) => Execute(async () =>
    {
        var result = await service.GetAsync(idTurno, token);
        return result is null ? NoContent() : Ok(result);
    });

    [HttpPost("turnos/{idTurno:int}")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(typeof(FacturaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(FacturaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Issue(int idTurno, EmitirFacturaRequest request, CancellationToken token) => Execute(async () =>
    {
        var result = await service.IssueAsync(idTurno, request,
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture), token);
        return result.Created ? CreatedAtAction(nameof(Get), new { idTurno }, result.Invoice) : Ok(result.Invoice);
    });

    [HttpPost("{id:guid}/impresiones")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(typeof(ImpresionComprobanteDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Print(Guid id, SolicitarImpresionRequest request, CancellationToken token) => Execute(async () =>
    {
        var job = await printing.InvoiceAsync(await service.GetByIdAsync(id, token), request,
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture), token);
        return Accepted($"/api/RetiroVehiculos/impresiones/{job.SolicitudId}", job);
    });

    [HttpGet("{id:guid}/impresiones/ultima")]
    [ProducesResponseType(typeof(ImpresionComprobanteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> LatestPrint(Guid id, CancellationToken token) => Execute(async () =>
    {
        var job = await printing.LatestInvoiceAsync(await service.GetByIdAsync(id, token), token);
        return job is null ? NoContent() : Ok(job);
    });

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (FacturacionException error) { return Problem(statusCode: error.Status, title: error.Message); }
        catch (RetiroException error) { return Problem(statusCode: error.Status, title: error.Status == 503 ? "Impresion no disponible en el servidor." : "Solicitud de impresion invalida o utilizada por otro documento."); }
        catch (EmpresaException error) { return Problem(statusCode: error.Status, title: "Datos de empresa no disponibles."); }
        catch (Exception error) when (error is DbException or DbUpdateException or JsonException or IOException)
        { return Problem(statusCode: 503, title: "Facturacion no disponible. Consulte antes de reintentar."); }
    }
}