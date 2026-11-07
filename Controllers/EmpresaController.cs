using System.Data.Common;
using System.Text.Json;
using Carwash.API.DTOs;
using Carwash.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Carwash.API.Controllers;

[ApiController]
[Route("api/Empresa")]
[Authorize(AuthenticationSchemes = SessionAuthenticationHandler.SchemeName)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class EmpresaController(EmpresaService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(EmpresaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Get(CancellationToken cancellationToken) => Execute(async () =>
    {
        var result = await service.GetAsync(cancellationToken);
        return result is null ? NoContent() : Ok(result);
    });

    [HttpPut]
    [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
    [RequestSizeLimit(2_097_152)]
    [ProducesResponseType(typeof(EmpresaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Put(EmpresaDto request, CancellationToken cancellationToken) =>
        Execute(async () => Ok(await service.SaveAsync(request, cancellationToken)));

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (EmpresaException error) { return Problem(statusCode: error.Status, title: error.Message); }
        catch (Exception error) when (error is DbException or JsonException)
        { return Problem(statusCode: 503, title: "Datos de empresa no disponibles."); }
    }
}