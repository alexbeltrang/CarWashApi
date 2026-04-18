using Carwash.API.Context;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TurnosDiariosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TurnosDiariosController(AppDbContext context)
        {
            _context = context;
        }
        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.TurnosDiarios.ToListAsync();
            return Ok(data);
        }

        // GET: api/Turnos/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var turno = await _context.TurnosDiarios.FindAsync(id);

            if (turno == null)
                return NotFound();

            return Ok(turno);
        }

        // POST: api/Turnos
        [HttpPost("RegistrarTurnoDiario")]
        public async Task<IActionResult> Create([FromBody] TurnosDiarios turno)
        {
            try
            {
                turno.Fecha.ToString("yyyy-MM-dd");
                _context.TurnosDiarios.Add(turno);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetById), new { id = turno.Id }, turno);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("ActualizarTurnoDiario/{id}")]
        public async Task<IActionResult> ActualizarTurnoDiario(int id, [FromBody] TurnosDiarios turno)
        {
            if (id != turno.Id)
                return BadRequest();

            _context.Entry(turno).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }


        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var turno = await _context.TurnosDiarios.FindAsync(id);

            if (turno == null)
                return NotFound();

            _context.TurnosDiarios.Remove(turno);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("ByFecha")]
        public async Task<IActionResult> GetByFecha([FromQuery] string fecha)
        {
            try
            {
                if (!DateTime.TryParse(fecha, out DateTime fechaParsed))
                    return BadRequest("Formato de fecha inválido");

                var inicio = fechaParsed.Date;
                var fin = inicio.AddDays(1);

                var data = await _context.TurnosDiarios
                    .Where(t => t.Fecha >= inicio && t.Fecha < fin)
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener los turnos por fecha: {ex.Message}");
            }
        }
    }
}
