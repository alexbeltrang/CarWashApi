namespace Carwash.API.Controllers
{
    using Carwash.API.Context;
    using Carwash.API.DTOs;
    using Carwash.API.Services;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.EntityFrameworkCore;
    using System.Threading.Tasks;

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ApiSessionStore _sessions;
        private readonly TimeProvider _clock;
        private readonly TimeZoneInfo _businessZone;

        public AuthController(AppDbContext context, ApiSessionStore sessions, TimeProvider clock, IConfiguration configuration)
        {
            _context = context;
            _sessions = sessions;
            _clock = clock;
            _businessZone = TimeZoneInfo.FindSystemTimeZoneById(configuration["BusinessTimeZoneId"] ?? TimeZoneInfo.Local.Id);
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (request == null
                || string.IsNullOrWhiteSpace(request.UserName)
                || string.IsNullOrWhiteSpace(request.Password))
            {
                return Ok(new LoginResponseDto
                {
                    EsValido = false,
                    Respuesta = "Usuario y contrase�a son requeridos"
                });
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u =>
                    u.UserName == request.UserName &&
                    u.Password == request.Password &&
                    !u.IsDelete, cancellationToken);

            if (usuario == null)
            {
                return Ok(new LoginResponseDto
                {
                    EsValido = false,
                    Respuesta = "Usuario o contrase�a incorrectos"
                });
            }

            var isAdmin = await PerfilAdministrador.EsAdministradorAsync(_context, usuario.PerfilId);
            var session = _sessions.Issue(usuario.IdUser);
            if (session == null)
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "No se puede iniciar sesion en este momento.",
                    extensions: new Dictionary<string, object?> { ["code"] = "SESSION_UNAVAILABLE" });
            Response.Headers["X-Server-Date"] = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _businessZone)
                .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            return Ok(new LoginResponseDto
            {
                EsValido = true,
                Respuesta = "Ok",
                AccessToken = session.AccessToken,
                TokenType = "Bearer",
                ExpiresAt = session.ExpiresAt,
                IdleTimeoutSeconds = session.IdleTimeoutSeconds,
                Usuario = new UsuarioLoginDto
                {
                    IdUser = usuario.IdUser,
                    DisplayName = usuario.DisplayName,
                    Email = usuario.Email,
                    PerfilId = usuario.PerfilId,
                    EsAdministrador = isAdmin
                }
            });
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public IActionResult Logout()
        {
            Response.Headers.CacheControl = "no-store";
            if (!SessionAuthenticationHandler.TryReadToken(Request, out var token))
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Se requiere una credencial valida.",
                    extensions: new Dictionary<string, object?> { ["code"] = "SESSION_REQUIRED" });
            _sessions.Revoke(token);
            return NoContent();
        }
    }
}
