using Carwash.API.Context;
using Carwash.API.Modelos;
using Carwash.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Authorize]
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

        private async Task<IActionResult?> GuardAsync(params int[] identifiers)
        {
            foreach (var identifier in identifiers.Distinct())
            {
                var turn = await _context.Turnos.AsNoTracking().SingleOrDefaultAsync(row => row.IdTurno == identifier);
                if (turn == null) return CajaHttp.Error(HttpContext, 404, "NOT_FOUND");
                if (turn.Estado || turn.PagadoNomina || await _context.Facturas.AnyAsync(row => row.IdTurno == identifier))
                    return CajaHttp.Error(HttpContext, 409, "DEPENDENCY_LOCKED");
            }
            return null;
        }

        // POST: api/Turnos
        [HttpPost("RegistrarTurnoServicio")]
        public async Task<IActionResult> Create([FromBody] TurnoServicios turnoServicio)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var rejection = await GuardAsync(turnoServicio.IdTurno);
                if (rejection != null) return rejection;
                _context.TurnoServicios.Add(turnoServicio);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
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


            await using var transaction = await _context.Database.BeginTransactionAsync();
            var current = await _context.TurnoServicios.AsNoTracking().SingleOrDefaultAsync(row => row.idTunoServicios == id);
            if (current == null) return NotFound();
            var rejection = await GuardAsync(current.IdTurno, turnoServicio.IdTurno);
            if (rejection != null) return rejection;
            _context.Entry(turnoServicio).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return NoContent();
        }

        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var turno = await _context.TurnoServicios.SingleOrDefaultAsync(row => row.idTunoServicios == id);

            if (turno == null)
                return NotFound();
            var rejection = await GuardAsync(turno.IdTurno);
            if (rejection != null) return rejection;
            _context.TurnoServicios.Remove(turno);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return NoContent();
        }
    }
}
