using Carwash.API.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClienteCreditoController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ClienteCreditoController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/ClienteCredito
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.ClienteCredito.ToListAsync();
            return Ok(data);
        }

        // GET: api/ClienteCredito/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var cliente = await _context.ClienteCredito.FindAsync(id);

            if (cliente == null)
                return NotFound();

            return Ok(cliente);
        }

        // GET: api/CajaDiaria/5
        [HttpGet("ClienteCreditoActivos")]
        public async Task<IActionResult> getClienteCreditoActivos()
        {
            try
            {
                var clientes = await _context.ClienteCredito.Where(c => c.Estado == true).ToListAsync();

                if (clientes == null || !clientes.Any())
                    return NotFound();

                return Ok(clientes);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

        }
    }
}
