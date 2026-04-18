using Carwash.API.Context;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OperarioComisionesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public OperarioComisionesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/OperarioComisiones
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.OperarioComisiones.ToListAsync();
            return Ok(data);
        }

        // GET: api/OperarioComisiones/5
        [HttpGet("BuscarPorDiaSemana")]
        public async Task<IActionResult> GetById(int numeroDia, int idOperario)
        {
            try
            {
                var operarioComision = await _context.OperarioComisiones
                    .Where(c => c.DiaSemana == numeroDia && c.idOperario == idOperario)
                    .FirstOrDefaultAsync();

                if (operarioComision == null)
                    return NotFound();

                return Ok(operarioComision);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al buscar la comisión del operario: {ex.Message}");
            }
        }

        // GET: api/OperarioComisiones/5
        [HttpGet("ComisionbyOperario")]
        public async Task<IActionResult> ComisionbyOperario(int idOperario)
        {
            try
            {
                var operarioComision = await _context.OperarioComisiones
                    .Where(c => c.idOperario == idOperario)
                    .ToListAsync();

                if (operarioComision == null)
                    return NotFound();

                return Ok(operarioComision);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al buscar la comisión del operario: {ex.Message}");
            }
        }

        // POST: api/OperarioComisiones
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] OperarioComisiones operarioComision)
        {
            _context.OperarioComisiones.Add(operarioComision);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = operarioComision.IdOperarioComision }, operarioComision);
        }

        // PUT: api/OperarioComisiones/5
        [HttpPut("ActualizarOperarioComision/{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] OperarioComisiones operarioComision)
        {
            if (id != operarioComision.IdOperarioComision)
                return BadRequest();

            _context.Entry(operarioComision).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }


        // DELETE: api/OperarioComisiones/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var operarioComision = await _context.OperarioComisiones.FindAsync(id);

            if (operarioComision == null)
                return NotFound();

            _context.OperarioComisiones.Remove(operarioComision);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
