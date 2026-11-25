namespace Carwash.API.Controllers
{
    using Carwash.API.Context;
    using Carwash.API.Modelos;
    using Carwash.API.Services;
    using Carwash.API.DTOs;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CajaDiariaController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly CajaDiariaService _caja;

        [HttpGet("cierres")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> Cierres(CancellationToken token, string? desde = null, string? hasta = null, int pagina = 1, int tamanoPagina = 50)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.CierresAsync(User, desde, hasta, pagina, tamanoPagina, token)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpGet("{id:int}/cierres/{version:int}")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> CierreDetalle(int id, int version, CancellationToken token)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.CierreAsync(User, id, version, token)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/reapertura")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> Reapertura(int id, CajaReaperturaRequest request, CancellationToken token)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.ReabrirAsync(User, id, request, token)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/recierre")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> Recierre(int id, CajaCierreRequest request, CancellationToken token)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var result = await _caja.CerrarAsync(User, id, request, token, recierre: true);
                return CreatedAtAction(nameof(Operacion), new { id = result.IdOperacion }, result);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpGet("{id:int}/movimientos")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> Movimientos(int id, CancellationToken token, int pagina = 1, int tamanoPagina = 50)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.MovimientosAsync(User, id, pagina, tamanoPagina, token)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/gastos/{salida:int}/correccion")]
        [HttpPost("{id:int}/vales/{salida:int}/correccion")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> CorregirSalida(int id, int salida, CajaCorreccionSalidaRequest request, CancellationToken token)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.CorregirSalidaAsync(User, id, salida, Request.Path.Value!.Contains("/vales/", StringComparison.OrdinalIgnoreCase), request, token)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/gastos")]
        public async Task<IActionResult> Gasto(int id, CajaGastoRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var result = await _caja.GastoAsync(User, id, request, cancellationToken);
                return CreatedAtAction(nameof(Operacion), new { id = result.IdOperacion }, result);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/vales")]
        public async Task<IActionResult> Vale(int id, CajaValeRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var result = await _caja.ValeAsync(User, id, request, cancellationToken);
                return CreatedAtAction(nameof(Operacion), new { id = result.IdOperacion }, result);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpGet("{id:int}/resumen")]
        public async Task<IActionResult> Resumen(int id, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.ResumenAsync(User, id, cancellationToken)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpGet("operaciones/{id:guid}")]
        public async Task<IActionResult> Operacion(Guid id, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.GetOperacionAsync(User, id, cancellationToken)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("apertura")]
        public async Task<IActionResult> Apertura(CajaAperturaRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var result = await _caja.AbrirAsync(User, request, cancellationToken);
                return CreatedAtAction(nameof(Operacion), new { id = result.IdOperacion }, result);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/cierre")]
        public async Task<IActionResult> Cierre(int id, CajaCierreRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var result = await _caja.CerrarAsync(User, id, request, cancellationToken);
                return CreatedAtAction(nameof(Operacion), new { id = result.IdOperacion }, result);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        public CajaDiariaController(AppDbContext context, CajaDiariaService caja)
        {
            _context = context;
            _caja = caja;
        }

        // GET: api/CajaDiaria
        [HttpGet]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.CajaDiaria.ToListAsync();
            return Ok(data);
        }

        // GET: api/CajaDiaria/5
        [HttpGet("{id}")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> GetById(int id)
        {
            var caja = await _context.CajaDiaria.FindAsync(id);

            if (caja == null)
                return NotFound();

            return Ok(caja);
        }

        // GET: api/CajaDiaria/5
        [HttpGet("getCajaActiva")]
        public async Task<IActionResult> getCajaActiva(CancellationToken cancellationToken)
        {
            try { return Ok(await _caja.GetActivaAsync(User, cancellationToken)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        // POST: api/CajaDiaria
        [HttpPost("CrearCaja")]
        [CajaLegacyWrite]
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
        [CajaLegacyWrite]
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
        [CajaLegacyWrite]
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
        [CajaLegacyWrite]
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
