using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TurnosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TurnosController(AppDbContext context)
        {
            _context = context;
        }
        // GET: api/CajaDiaria
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _context.Turnos.ToListAsync();
            return Ok(data);
        }



        [HttpGet("GetallActivos")]
        public async Task<IActionResult> GetallActivos()
        {
            var data = await _context.Turnos
                .Where(t => t.Estado == false)
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/Turnos/GetById?id=5
        [HttpGet("GetById")]
        public async Task<IActionResult> GetById([FromQuery] int id)
        {
            try
            {
                var turno = await _context.Turnos.FindAsync(id);

                if (turno == null)
                    return NotFound();

                return Ok(turno);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // POST: api/Turnos
        [HttpPost("RegistrarTurno")]
        public async Task<IActionResult> Create([FromBody] Turnos turno)
        {

            _context.Turnos.Add(turno);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = turno.IdTurno }, turno);
        }

        // PUT: api/Turnos/5
        [HttpPut("ActualizarTurno/{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] Turnos turno)
        {
            if (id != turno.IdTurno)
                return BadRequest();


            _context.Entry(turno).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var turno = await _context.Turnos.FindAsync(id);

            if (turno == null)
                return NotFound();

            _context.Turnos.Remove(turno);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("BuscarPorPlaca")]
        public async Task<IActionResult> BuscarPorPlaca([FromQuery] string placa)
        {
            try
            {
                if (string.IsNullOrEmpty(placa))
                    return BadRequest("La placa es obligatoria");

                var placaNormalizada = placa.Trim().ToUpper();

                var vehiculo = await _context.Turnos
                    .Where(t => t.Placa.ToUpper() == placaNormalizada)
                    .OrderByDescending(t => t.FechaHoraIngreso)
                    .Select(t => new
                    {
                        t.IdTurno,
                        t.NombreCliente,
                        t.NumeroCelular,
                        t.Placa,
                        t.IdTipoVehiculo,
                        t.Estado,
                        t.Marca
                    })
                    .FirstOrDefaultAsync();

                if (vehiculo == null)
                    return NotFound();

                return Ok(vehiculo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al buscar por placa: {ex.Message}");
            }
        }


        [HttpGet("HistorialPorPlaca")]
        public async Task<IActionResult> HistorialPorPlaca([FromQuery] string placa)
        {
            try
            {
                if (string.IsNullOrEmpty(placa))
                    return BadRequest("La placa es obligatoria");

                var placaNormalizada = placa.Trim().ToUpper();

                var historial = await (
                    from tur in _context.Turnos
                    join tvh in _context.TipoVehiculo
                        on tur.IdTipoVehiculo equals tvh.IdTipoVehiculo
                    where tur.Placa.ToUpper() == placaNormalizada
                    orderby tur.FechaHoraIngreso descending
                    select new IngresoVehiculoDTO
                    {
                        IdTurno = tur.IdTurno,
                        NumeroTurno = tur.NumeroTurno,

                        // Formato de fecha
                        FechaHoraIngreso = tur.FechaHoraIngreso
                            .ToString("yyyy-MM-dd HH:mm"),

                        NombreCliente = tur.NombreCliente,
                        Placa = tur.Placa,

                        TipoVehiculo = tvh.Nombre,

                        // Formato de dinero
                        ValorPagado = "$ " + tur.Valor.ToString("N2"),

                        Marca = tur.Marca
                    }
                ).ToListAsync();

                if (historial == null || historial.Count == 0)
                    return NotFound();

                return Ok(historial);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener historial: {ex.Message}");
            }
        }

        [HttpGet("VehiculoEnProceso")]
        public async Task<IActionResult> VehiculoEnProceso([FromQuery] int idTurno)
        {
            try
            {
                var data = await (
                    from tur in _context.Turnos
                    join tvh in _context.TipoVehiculo
                        on tur.IdTipoVehiculo equals tvh.IdTipoVehiculo

                    // LEFT JOIN Operarios
                    join ope in _context.Operarios
                        on tur.idOperario equals ope.idOperario into operariosGroup
                    from ope in operariosGroup.DefaultIfEmpty()

                    where tur.IdTurno == idTurno

                    select new
                    {
                        tur.IdTurno,
                        tur.Placa,
                        tur.NombreCliente,
                        tur.NumeroCelular,
                        tur.NumeroTurno,
                        tur.FechaHoraIngreso,
                        TipoVehiculo = tvh.Nombre,

                        OperadorAsignado =
                            ((ope != null ? ope.Nombres : "") ?? "") + " " +
                            ((ope != null ? ope.Apellidos : "") ?? ""),

                        tur.Valor,
                        tur.idOperario,
                        tur.Observaciones,
                        tur.ValorBaseComision
                    }
                ).FirstOrDefaultAsync();

                if (data == null)
                    return NotFound();

                var result = new GestionVehiculosDTO
                {
                    IdTurno = data.IdTurno,
                    Placa = data.Placa,
                    NombreCliente = data.NombreCliente,
                    NumeroCelular = data.NumeroCelular,
                    NumeroTurno = data.NumeroTurno,
                    FechaHoraIngreso = data.FechaHoraIngreso.ToString("yyyy-MM-dd HH:mm"),
                    TipoVehiculo = data.TipoVehiculo,
                    OperadorAsignado = data.OperadorAsignado.Trim(),
                    Valor = data.Valor,
                    idOperario = data.idOperario,
                    Observaciones = data.Observaciones,
                    ValorBaseComision = data.ValorBaseComision
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener vehículo en proceso: {ex.Message}");
            }
        }

        [HttpGet("VehiculosEnProceso")]
        public async Task<IActionResult> VehiculosEnProceso()
        {
            try
            {
                var data = await (
                    from tur in _context.Turnos
                    join tvh in _context.TipoVehiculo
                        on tur.IdTipoVehiculo equals tvh.IdTipoVehiculo

                    join ope in _context.Operarios
                        on tur.idOperario equals ope.idOperario into operariosGroup
                    from ope in operariosGroup.DefaultIfEmpty() // LEFT JOIN

                    where tur.Estado == false
                    orderby tur.FechaHoraIngreso ascending

                    select new
                    {
                        tur.IdTurno,
                        tur.Placa,
                        tur.NombreCliente,
                        tur.NumeroCelular,
                        tur.NumeroTurno,
                        tur.FechaHoraIngreso,
                        TipoVehiculo = tvh.Nombre,

                        OperadorAsignado =
                            ((ope != null ? ope.Nombres : "") ?? "") + " " +
                            ((ope != null ? ope.Apellidos : "") ?? ""),

                        tur.Valor,
                        tur.OperadorOcupado,
                        tur.Marca,
                        tur.NumeroOrden
                    }
                ).ToListAsync();

                var vehiculosProceso = data.Select(x => new GestionVehiculosDTO
                {
                    IdTurno = x.IdTurno,
                    Placa = x.Placa,
                    NombreCliente = x.NombreCliente,
                    NumeroCelular = x.NumeroCelular,
                    NumeroTurno = x.NumeroTurno,
                    FechaHoraIngreso = x.FechaHoraIngreso.ToString("yyyy-MM-dd HH:mm"),
                    TipoVehiculo = x.TipoVehiculo,
                    OperadorAsignado = x.OperadorAsignado.Trim(),
                    ValorCliente = "$ " + x.Valor.ToString("N2"),
                    OperadorOcupado = (bool)x.OperadorOcupado,
                    Marca = x.Marca,
                    NumeroOrden = x.NumeroOrden
                }).ToList();

                return Ok(vehiculosProceso);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener vehículos en proceso: {ex.Message}");
            }
        }

        [HttpGet("DashboardOperadores")]
        public async Task<IActionResult> DashboardOperadores()
        {
            try
            {
                var ahora = DateTime.Now;

                var data = await (
                    from t in _context.Turnos
                    join o in _context.Operarios
                        on t.idOperario equals o.idOperario
                    where t.Estado == false
                       && t.OperadorOcupado == true
                       && t.FechaHoraAsignacionOperario != null
                    select new
                    {
                        NombreOperador = (o.Nombres ?? "") + " " + (o.Apellidos ?? ""),
                        t.Placa,
                        t.FechaHoraAsignacionOperario
                    }
                ).ToListAsync();

                var lista = data
                    .Select(x =>
                    {
                        var segundos = (int)(ahora - x.FechaHoraAsignacionOperario.Value).TotalSeconds;

                        return new DashboardOperadoresDTO
                        {
                            NombreOperador = x.NombreOperador.Trim(),
                            Placa = x.Placa,
                            MinutosTranscurridos = segundos / 60,
                            Horas = segundos / 3600,
                            MinutosRestantes = (segundos % 3600) / 60
                        };
                    })
                    .OrderByDescending(x => x.MinutosTranscurridos)
                    .ToList();

                return Ok(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener dashboard de operadores: {ex.Message}");
            }
        }

        [HttpGet("PendientesNomina")]
        public async Task<IActionResult> PendientesNomina(
            [FromQuery] int idOperario,
            [FromQuery] DateTime fechaInicial,
            [FromQuery] DateTime fechaFinal)
        {
            try
            {
                var inicio = fechaInicial.Date;
                var fin = fechaFinal.Date.AddDays(1);

                var data = await _context.Turnos
                    .Where(t =>
                        t.idOperario == idOperario &&
                        t.PagadoNomina == false &&
                        t.FechaHoraIngreso >= inicio &&
                        t.FechaHoraIngreso < fin)
                    .Select(t => new
                    {
                        t.idOperario,
                        t.FechaHoraIngreso,
                        ValorServicio = t.ValorBaseComision
                    })
                    .ToListAsync();

                var resultado = data.Select(x => new
                {
                    x.idOperario,
                    FechaHoraIngreso = x.FechaHoraIngreso.ToString("yyyy-MM-dd HH:mm"),
                    x.ValorServicio
                }).ToList();

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al consultar pendientes de nómina: {ex.Message}");
            }
        }
    }
}
