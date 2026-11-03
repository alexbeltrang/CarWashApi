using System.Data.Common;
using Carwash.API.DTOs;
using Carwash.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Carwash.API.Controllers;

[ApiController]
[Route("api/Reportes")]
[Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
public sealed class ReportesController(ReporteVentasService reports) : ControllerBase
{
    [HttpGet("Ventas")]
    [ProducesResponseType(typeof(ReporteVentasDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Ventas([FromQuery] ReporteVentasQuery query, CancellationToken cancellationToken)
        => Execute(async () => await reports.GetAsync(query.Parse(), cancellationToken), cancellationToken);

    [HttpGet("Ventas/Filtros")]
    [ProducesResponseType(typeof(ReporteVentasFiltrosDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Filtros([FromQuery] ReporteVentasQuery query, CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var extras = Request.Query.Keys.Where(key => !key.Equals("fechaInicial", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("fechaFinal", StringComparison.OrdinalIgnoreCase)).ToDictionary(key => key, _ => new[] { "Solo se admiten fechas para consultar opciones." });
            if (extras.Count != 0) throw new ReporteVentasException(400, "INVALID_FILTERS", extras);
            return await reports.GetOptionsAsync(query.Parse().Criterios, cancellationToken);
        }, cancellationToken);

    private async Task<IActionResult> Execute(Func<Task<object>> action, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            Response.Headers["X-Server-Date"] = reports.GetBusinessDate().ToString("yyyy-MM-dd");
            return Ok(await action());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ReporteVentasException error) { return ReportProblem(error.Status, error.Code, error.Errors); }
        catch (Exception error) when (error is FormatException or OverflowException or InvalidCastException)
        {
            return ReportProblem(503, "INVALID_SOURCE_DATA");
        }
        catch (DbException) { return ReportProblem(503, "REPORT_UNAVAILABLE"); }
        catch (Exception) { return ReportProblem(500, "UNEXPECTED_ERROR"); }
    }

    private ObjectResult ReportProblem(int status, string code, IDictionary<string, string[]>? errors = null)
    {
        ProblemDetails problem = errors == null ? new ProblemDetails() : new ValidationProblemDetails(errors);
        problem.Status = status;
        problem.Type = "about:blank";
        problem.Instance = Request.Path;
        problem.Title = status == 400 ? "Los criterios de consulta no son validos." : "No se pudo consultar el reporte.";
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}