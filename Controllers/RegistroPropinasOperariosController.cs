using Carwash.API.Context;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegistroPropinasOperariosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public RegistroPropinasOperariosController(AppDbContext context)
        {
            _context = context;
        }
        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.RegistroPropinasOperarios.ToListAsync();
            return Ok(data);
        }



        [HttpGet("GetallActivos")]
        public async Task<IActionResult> GetallActivos()
        {
            var data = await _context.RegistroPropinasOperarios
                .Where(t => t.isDelete == false)
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/Turnos/GetById?id=5
        [HttpGet("GetById")]
        public async Task<IActionResult> GetById([FromQuery] int id)
        {
            try
            {
                var registro = await _context.RegistroPropinasOperarios.FindAsync(id);

                if (registro == null)
                    return NotFound();

                return Ok(registro);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // POST: api/Turnos
        [HttpPost("RegistrarRegistroPropinasOperarios")]
        public async Task<IActionResult> Create([FromBody] RegistroPropinasOperarios registro)
        {

            _context.RegistroPropinasOperarios.Add(registro);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = registro.idPropina }, registro);
        }

        // PUT: api/Turnos/5
        [HttpPut("ActualizarRegistroPropinasOperarios/{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] RegistroPropinasOperarios registro)
        {
            if (id != registro.idPropina)
                return BadRequest();

            _context.Entry(registro).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var registro = await _context.RegistroPropinasOperarios.FindAsync(id);

            if (registro == null)
                return NotFound();

            _context.RegistroPropinasOperarios.Remove(registro);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
