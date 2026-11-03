using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Carwash.API.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Carwash.API.Services;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ApiSessionStore sessions, AppDbContext context) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiSession";

    public static bool TryReadToken(HttpRequest request, out string token)
    {
        token = string.Empty;
        if (request.Headers.Authorization.Count != 1
            || !AuthenticationHeaderValue.TryParse(request.Headers.Authorization[0], out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || !ApiSessionStore.IsValidToken(header.Parameter)) return false;
        token = header.Parameter!;
        return true;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadToken(Request, out var token)) return AuthenticateResult.NoResult();
        var userId = sessions.Validate(token);
        if (userId == null) return AuthenticateResult.Fail("Sesion no vigente.");
        var user = await context.Usuarios.AsNoTracking()
            .Where(user => user.IdUser == userId.Value && !user.IsDelete)
            .Select(user => new { user.IdUser }).SingleOrDefaultAsync(Context.RequestAborted);
        if (user == null)
        {
            sessions.Revoke(token);
            return AuthenticateResult.Fail("Sesion no vigente.");
        }
        Response.Headers.CacheControl = "no-store";
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.IdUser.ToString(CultureInfo.InvariantCulture))], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return WriteProblemAsync(StatusCodes.Status401Unauthorized, "SESSION_REQUIRED", "Se requiere iniciar sesion.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(StatusCodes.Status403Forbidden, "ADMIN_REQUIRED", "Se requiere perfil Administrador.");

    private Task WriteProblemAsync(int status, string code, string title)
    {
        Response.StatusCode = status;
        Response.Headers.CacheControl = "no-store";
        var problem = new ProblemDetails { Status = status, Title = title, Instance = Request.Path };
        problem.Extensions["code"] = code;
        return Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", Context.RequestAborted);
    }
}