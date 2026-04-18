using Carwash.API.Context;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GastosCajaController : ControllerBase
    {
        private readonly AppDbContext _context;

        public GastosCajaController(AppDbContext context)
        {
            _context = context;
        }
        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.GastosCaja.ToListAsync();
            return Ok(data);
        }

        // GET: api/Turnos/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var gasto = await _context.GastosCaja.FindAsync(id);

            if (gasto == null)
                return NotFound();

            return Ok(gasto);
        }

        // POST: api/Turnos
        [HttpPost("RegistrarGastoCaja")]
        public async Task<IActionResult> Create([FromBody] GastosCaja gasto)
        {
            try
            {
                gasto.FechaRegistro.ToString("yyyy-MM-dd");
                _context.GastosCaja.Add(gasto);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetById), new { id = gasto.IdGasto }, gasto);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("ActualizarGastoCaja/{id}")]
        public async Task<IActionResult> ActualizarGastoCaja(int id, [FromBody] GastosCaja gasto)
        {
            if (id != gasto.IdGasto)
                return BadRequest();

            _context.Entry(gasto).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }


        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var gasto = await _context.GastosCaja.FindAsync(id);

            if (gasto == null)
                return NotFound();

            _context.GastosCaja.Remove(gasto);
            await _context.SaveChangesAsync();

            return NoContent();
        }

    }
}
