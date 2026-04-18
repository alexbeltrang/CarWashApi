using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TurnosMovimientosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TurnosMovimientosController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.TurnosMovimientos.ToListAsync();
            return Ok(data);
        }

        // GET: api/Turnos/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var turno = await _context.TurnosMovimientos.FindAsync(id);

            if (turno == null)
                return NotFound();

            return Ok(turno);
        }

        // POST: api/Turnos
        [HttpPost("RegistrarTurnoMovimiento")]
        public async Task<IActionResult> Create([FromBody] TurnosMovimientos turnoMovimiento)
        {
            try
            {
                _context.TurnosMovimientos.Add(turnoMovimiento);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetById), new { id = turnoMovimiento.IdTurnoMovimientos }, turnoMovimiento);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("ActualizarTurnoMovimiento/{id}")]
        public async Task<IActionResult> ActualizarTurnoMovimiento(int id, [FromBody] TurnosMovimientos turnoMovimiento)
        {
            if (id != turnoMovimiento.IdTurnoMovimientos)
                return BadRequest();

            _context.Entry(turnoMovimiento).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }


        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var turno = await _context.TurnosMovimientos.FindAsync(id);

            if (turno == null)
                return NotFound();

            _context.TurnosMovimientos.Remove(turno);
            await _context.SaveChangesAsync();

            return NoContent();
        }


        [HttpGet("GetHistoricoMovimientos")]
        public async Task<IActionResult> GetHistoricoMovimientos(
            DateTime fechaInicial,
            DateTime fechaFinal,
            int? idFormaPago,
            int? idOperario)
        {
            try
            {
                var query = from tur in _context.Turnos
                            join tum in _context.TurnosMovimientos
                                on tur.IdTurno equals tum.IdTurno
                            join frp in _context.FormaPagos
                                on tum.IdFormaPago equals frp.IdFormaPago
                            join tpv in _context.TipoVehiculo
                                on tur.IdTipoVehiculo equals tpv.IdTipoVehiculo
                            join opr in _context.Operarios
                                on tur.idOperario equals opr.idOperario
                            join clc in _context.ClienteCredito
                                on tur.idClienteCredito equals clc.idClienteCredito into clcGroup
                            from clc in clcGroup.DefaultIfEmpty() // LEFT JOIN

                            select new
                            {
                                tur.Placa,
                                tur.FechaHoraIngreso,
                                Valor = tum.MontoPagado,
                                tur.ValorBaseComision,
                                tur.Pagado,
                                tur.Observaciones,
                                FormaPago = frp.Nombre,
                                TipoVehiculo = tpv.Nombre,
                                Operario = (opr.Nombres ?? "") + " " + (opr.Apellidos ?? ""),
                                ClienteCredito = clc != null ? clc.Nombre : ""
                            };

                // 🔥 Filtros dinámicos
                if (idFormaPago.HasValue && idFormaPago > 0)
                {
                    query = query.Where(x => x.FormaPago != null &&
                                             _context.FormaPagos
                                             .Any(f => f.IdFormaPago == idFormaPago && f.Nombre == x.FormaPago));
                }

                if (idOperario.HasValue && idOperario > 0)
                {
                    query = query.Where(x =>
                        _context.Operarios.Any(o =>
                            o.idOperario == idOperario &&
                            ((o.Nombres ?? "") + " " + (o.Apellidos ?? "")) == x.Operario));
                }

                // 🔥 Filtro por rango de fechas (FORMA CORRECTA)
                var inicio = fechaInicial.Date;
                var fin = fechaFinal.Date.AddDays(1);

                query = query.Where(x => x.FechaHoraIngreso >= inicio && x.FechaHoraIngreso < fin);

                var data = await query.ToListAsync();

                // 🔥 Mapear a DTO + formatear
                var historial = data.Select(x => new ConsultaMovimientosDTO
                {
                    Placa = x.Placa,
                    FechaHoraIngreso = x.FechaHoraIngreso.ToString("yyyy-MM-dd HH:mm"),
                    Valor = x.Valor,
                    ValorBaseComision = x.ValorBaseComision,
                    Pagado = x.Pagado,
                    Observaciones = x.Observaciones,
                    FormaPago = x.FormaPago,
                    TipoVehiculo = x.TipoVehiculo,
                    Operario = x.Operario.Trim(),
                    ClienteCredito = x.ClienteCredito
                }).ToList();

                return Ok(historial);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener histórico: {ex.Message}");
            }
        }
    }
}
