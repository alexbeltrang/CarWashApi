using Carwash.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Carwash.API.Services;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CajaLegacyWriteAttribute : AuthorizeAttribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) =>
        context.Result = CajaHttp.Error(context.HttpContext, 409, "CLIENT_UPDATE_REQUIRED");

    public void OnActionExecuted(ActionExecutedContext context) { }
}

public static class CajaHttp
{
    public static ObjectResult Error(HttpContext context, int status, string code, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        return new ObjectResult(new CajaErrorResponse(code,
            code == "CLIENT_UPDATE_REQUIRED" ? "Actualice el cliente financiero para esta operacion." : "No se pudo confirmar la operacion de caja.",
            context.TraceIdentifier, errors)) { StatusCode = status };
    }
}