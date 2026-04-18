using Carwash.API.Context;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TurnoServiciosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TurnoServiciosController(AppDbContext context)
        {
            _context = context;
        }
        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.TurnoServicios.ToListAsync();
            return Ok(data);
        }

        // GET: api/Turnos/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var turno = await _context.TurnoServicios.FindAsync(id);

            if (turno == null)
                return NotFound();

            return Ok(turno);
        }

        // POST: api/Turnos
        [HttpPost("RegistrarTurnoServicio")]
        public async Task<IActionResult> Create([FromBody] TurnoServicios turnoServicio)
        {
            try
            {

                _context.TurnoServicios.Add(turnoServicio);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetById), new { id = turnoServicio.idTunoServicios }, turnoServicio);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al crear el turno servicio: {ex.Message}");
            }
        }

        // PUT: api/Turnos/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] TurnoServicios turnoServicio)
        {
            if (id != turnoServicio.idTunoServicios)
                return BadRequest();


            _context.Entry(turnoServicio).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var turno = await _context.TurnoServicios.FindAsync(id);

            if (turno == null)
                return NotFound();

            _context.TurnoServicios.Remove(turno);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
