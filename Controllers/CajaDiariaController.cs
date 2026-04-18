namespace Carwash.API.Controllers
{
    using Carwash.API.Context;
    using Carwash.API.Modelos;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;

    [ApiController]
    [Route("api/[controller]")]
    public class CajaDiariaController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CajaDiariaController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.CajaDiaria.ToListAsync();
            return Ok(data);
        }

        // GET: api/CajaDiaria/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var caja = await _context.CajaDiaria.FindAsync(id);

            if (caja == null)
                return NotFound();

            return Ok(caja);
        }

        // GET: api/CajaDiaria/5
        [HttpGet("getCajaActiva")]
        public async Task<IActionResult> getCajaActiva()
        {
            var caja = await _context.CajaDiaria.FirstOrDefaultAsync(c => c.Estado == true);

            if (caja == null)
                return NotFound();

            return Ok(caja);
        }

        // POST: api/CajaDiaria
        [HttpPost("CrearCaja")]
        public async Task<IActionResult> CrearCaja([FromBody] CajaDiaria caja)
        {
            caja.TotalFinal = CalcularTotalFinal(caja);
            caja.FechaApertura.ToString("yyyy-MM-dd HH:mm");

            _context.CajaDiaria.Add(caja);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = caja.IdCaja }, caja);
        }

        // PUT: api/CajaDiaria/5
        [HttpPut("ActualizarCajaDiaria/{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CajaDiaria caja)
        {
            if (id != caja.IdCaja)
                return BadRequest();

            caja.TotalFinal = CalcularTotalFinal(caja);

            _context.Entry(caja).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/CajaDiaria/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var caja = await _context.CajaDiaria.FindAsync(id);

            if (caja == null)
                return NotFound();

            _context.CajaDiaria.Remove(caja);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // POST: api/CajaDiaria/cerrar/5
        [HttpPost("cerrar/{id}")]
        public async Task<IActionResult> CerrarCaja(int id)
        {
            var caja = await _context.CajaDiaria.FindAsync(id);

            if (caja == null)
                return NotFound();

            caja.FechaCierre = DateTime.Now;
            caja.Estado = true;
            caja.TotalFinal = CalcularTotalFinal(caja);

            await _context.SaveChangesAsync();

            return Ok(caja);
        }

        private decimal CalcularTotalFinal(CajaDiaria caja)
        {
            return caja.MontoInicial
                + caja.TotalIngresosEfectivo
                + caja.TotalIngresosTransferencias
                + caja.TotalIngresosDatafono
                + caja.TotalIngresosCredito
                - caja.TotalEgresos
                - caja.TotalValesOperarios;
        }
    }
}
