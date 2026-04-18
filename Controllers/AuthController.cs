namespace Carwash.API.Controllers
{
    using Carwash.API.Context;
    using Carwash.API.DTOs;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using System.Threading.Tasks;

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.UserName)
                || string.IsNullOrWhiteSpace(request.Password))
            {
                return Ok(new LoginResponseDto
                {
                    EsValido = false,
                    Respuesta = "Usuario y contraseña son requeridos"
                });
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u =>
                    u.UserName == request.UserName &&
                    u.Password == request.Password &&
                    !u.IsDelete);

            if (usuario == null)
            {
                return Ok(new LoginResponseDto
                {
                    EsValido = false,
                    Respuesta = "Usuario o contraseña incorrectos"
                });
            }

            return Ok(new LoginResponseDto
            {
                EsValido = true,
                Respuesta = "Ok",
                Usuario = new UsuarioLoginDto
                {
                    IdUser = usuario.IdUser,
                    DisplayName = usuario.DisplayName,
                    Email = usuario.Email,
                    PerfilId = usuario.PerfilId
                }
            });
        }
    }
}
