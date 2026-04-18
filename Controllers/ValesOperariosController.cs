using Carwash.API.Context;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ValesOperariosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ValesOperariosController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll([FromQuery] int idCaja)
        {
            try
            {
                var data = await _context.valesOperarios
                    .Where(v => v.idCaja == idCaja)
                    .ToListAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("GetById")]
        public async Task<IActionResult> GetById([FromQuery] int id)
        {
            try
            {
                var turno = await _context.valesOperarios.FindAsync(id);

                if (turno == null)
                    return NotFound();

                return Ok(turno);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpPost("RegistrarValesOperarios")]
        public async Task<IActionResult> Create([FromBody] ValesOperarios valeOperario)
        {

            _context.valesOperarios.Add(valeOperario);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = valeOperario.IdValeOperario }, valeOperario);
        }
    }
}
