using Carwash.API.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TipoVehiculoController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TipoVehiculoController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.TipoVehiculo.ToListAsync();
            return Ok(data);
        }

    }
}
