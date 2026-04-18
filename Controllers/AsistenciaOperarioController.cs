using Carwash.API.Context;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class AsistenciaOperarioController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AsistenciaOperarioController(AppDbContext context)
        {
            _context = context;
        }
        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.AsistenciaOperario.ToListAsync();
            return Ok(data);
        }



        // GET: api/Turnos/GetById?id=5
        [HttpGet("GetById")]
        public async Task<IActionResult> GetById([FromQuery] int id)
        {
            try
            {
                var asistencia = await _context.AsistenciaOperario.FindAsync(id);

                if (asistencia == null)
                    return NotFound();

                return Ok(asistencia);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // POST: api/Turnos
        [HttpPost("RegistrarAsistenciaOperario")]
        public async Task<IActionResult> Create([FromBody] AsistenciaOperario asistencia)
        {

            _context.AsistenciaOperario.Add(asistencia);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = asistencia.idAsistenciaOperario }, asistencia);
        }

        // PUT: api/Turnos/5
        [HttpPut("ActualizarAsistenciaOperario/{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] AsistenciaOperario asistencia)
        {
            if (id != asistencia.idAsistenciaOperario)
                return BadRequest();

            _context.Entry(asistencia).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var asistencia = await _context.AsistenciaOperario.FindAsync(id);

            if (asistencia == null)
                return NotFound();

            _context.AsistenciaOperario.Remove(asistencia);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
