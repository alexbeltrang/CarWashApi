using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OperariosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public OperariosController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Operarios
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.Operarios.ToListAsync();
            return Ok(data);
        }

        // GET: api/Operarios/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var operario = await _context.Operarios.FindAsync(id);

            if (operario == null)
                return NotFound();

            return Ok(operario);
        }

        // GET: api/Operarios/getOperariosActivos
        [HttpGet("getOperariosActivos")]
        public async Task<IActionResult> getOperariosActivos()
        {
            var operarios = await _context.Operarios.Where(o => o.isDelete == false).ToListAsync();

            if (operarios == null || operarios.Count == 0)
                return NotFound();

            return Ok(operarios);
        }

        [HttpGet("OperadoresDisponibles")]
        public async Task<IActionResult> OperadoresDisponibles()
        {
            try
            {
                var ocupados = _context.Turnos
                    .Where(t => t.idOperario != null && t.OperadorOcupado == true)
                    .Select(t => t.idOperario);

                var operadoresDisponibles = await _context.Operarios
                    .Where(o => o.isDelete == false && !ocupados.Contains(o.idOperario))
                    .Select(o => new OperariosDTO
                    {
                        idOperario = o.idOperario,
                        Nombres = o.Nombres,
                        Apellidos = o.Apellidos
                    })
                    .ToListAsync();

                return Ok(operadoresDisponibles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener operadores disponibles: {ex.Message}");
            }
        }

        // POST: api/Operarios
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Operarios operario)
        {

            _context.Operarios.Add(operario);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = operario.idOperario }, operario);
        }

        // PUT: api/Operarios/5
        [HttpPut("ActualizarOperario/{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] Operarios operario)
        {
            if (id != operario.idOperario)
                return BadRequest();

            _context.Entry(operario).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/Operarios/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var operario = await _context.Operarios.FindAsync(id);

            if (operario == null)
                return NotFound();

            _context.Operarios.Remove(operario);
            await _context.SaveChangesAsync();

            return NoContent();
        }

    }
}
