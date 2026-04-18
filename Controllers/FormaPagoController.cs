using Carwash.API.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FormaPagoController : ControllerBase
    {
        private readonly AppDbContext _context;

        public FormaPagoController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.FormaPagos.ToListAsync();
            return Ok(data);
        }
    }
}
