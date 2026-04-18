using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServiciosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ServiciosController(AppDbContext context)
        {
            _context = context;
        }


        // GET: api/CajaDiaria/5
        [HttpGet("GetListaCompletaServicios")]
        public async Task<IActionResult> GetListaCompletaServicios()
        {
            var result = await (
                from psr in _context.PrecioServicioVehiculo
                join tpv in _context.TipoVehiculo
                on psr.IdTipoVehiculo equals tpv.IdTipoVehiculo
                join ser in _context.Servicios
                on psr.idServicio equals ser.idServicio
                where ser.IsDelete == false
                select new ServicioComboDTO
                {
                    idServicio = ser.idServicio,
                    Nombre = ser.Nombre,
                    precio = psr.Precio,
                    precioBaseComision = psr.PrecioBaseComision,
                    idTipoVehiculo = tpv.IdTipoVehiculo
                }
                ).ToListAsync();
            return Ok(result);
        }

        [HttpGet("ServiciosPorTurno")]
        public async Task<IActionResult> ServiciosPorTurno([FromQuery] int idTurno)
        {
            try
            {
                var serviciosAdquiridos = await (
                    from ser in _context.Servicios
                    join tus in _context.TurnoServicios
                        on ser.idServicio equals tus.idServicios
                    where tus.IsDeleted == false && tus.IdTurno == idTurno
                    select new ServicioListaDTO
                    {
                        idServicio = ser.idServicio,
                        Nombre = ser.Nombre
                    }
                ).ToListAsync();

                if (serviciosAdquiridos == null || serviciosAdquiridos.Count == 0)
                    return NotFound();

                return Ok(serviciosAdquiridos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener servicios: {ex.Message}");
            }
        }
    }
}
