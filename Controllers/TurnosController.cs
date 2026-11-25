using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Carwash.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

namespace Carwash.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TurnosController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly CajaDiariaService _caja;

        [HttpGet("{id:int}/impacto-borrado")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> ImpactoBorrado(int id, CancellationToken token)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.ImpactoAsync(User, id, false, token)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/borrado")]
        [Authorize(Policy = AdministradorAuthorizationHandler.PolicyName)]
        public async Task<IActionResult> Borrado(int id, CajaBorradoRequest request, CancellationToken token)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.BorrarAsync(User, id, false, request, token)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpGet("{id:int}/saldo")]
        [Authorize]
        public async Task<IActionResult> Saldo(int id, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try { return Ok(await _caja.SaldoAsync(User, id, cancellationToken)); }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/operario")]
        [Authorize]
        public async Task<IActionResult> Operario(int id, CajaOperarioRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var result = await _caja.OperarioAsync(User, id, request, cancellationToken);
                return Created($"/api/CajaDiaria/operaciones/{result.IdOperacion}", result);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        [HttpPost("{id:int}/cobros")]
        [Authorize]
        public async Task<IActionResult> Cobro(int id, CajaCobroRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                var result = await _caja.CobrarAsync(User, id, request, cancellationToken);
                return Created($"/api/CajaDiaria/operaciones/{result.IdOperacion}", result);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        public TurnosController(AppDbContext context, CajaDiariaService caja)
        {
            _context = context;
            _caja = caja;
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
        [Authorize]
        public async Task<IActionResult> Create([FromBody] Turnos turno, CancellationToken cancellationToken)
        {
            try
            {
                await _caja.RegistrarTurnoAsync(User, turno, cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = turno.IdTurno }, turno);
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        // PUT: api/Turnos/5
        [HttpPut("ActualizarTurno/{id}")]
        [Authorize]
        public async Task<IActionResult> Update(int id, [FromBody] JsonElement turno, CancellationToken cancellationToken)
        {
            try
            {
                await _caja.ActualizarDatosTurnoAsync(User, id, turno, cancellationToken);
                return NoContent();
            }
            catch (CajaException error) { return CajaHttp.Error(HttpContext, error.Status, error.Code, error.Errors); }
        }

        // DELETE: api/turno/5
        [HttpDelete("{id}")]
        [CajaLegacyWrite]
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
                        t.CorreoElectronico,
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


        [HttpGet("EnProcesoPorPlaca")]
        public async Task<IActionResult> EnProcesoPorPlaca([FromQuery] string placa)
        {
            if (string.IsNullOrWhiteSpace(placa))
                return BadRequest("La placa es obligatoria");

            var placaNormalizada = placa.Trim().ToUpper();
            var turno = await _context.Turnos
                .Where(t => t.Placa.ToUpper() == placaNormalizada && t.Estado == false)
                .OrderByDescending(t => t.FechaHoraIngreso)
                .FirstOrDefaultAsync();

            if (turno == null)
                return NotFound();

            var servicios = await _context.TurnoServicios
                .Where(s => s.IdTurno == turno.IdTurno && !s.IsDeleted)
                .Select(s => s.idServicios)
                .Distinct()
                .ToListAsync();

            return Ok(new TurnoEnProcesoDto
            {
                IdTurno = turno.IdTurno,
                NumeroTurno = turno.NumeroTurno,
                NombreCliente = turno.NombreCliente,
                NumeroCelular = turno.NumeroCelular,
                CorreoElectronico = turno.CorreoElectronico,
                Placa = turno.Placa,
                Marca = turno.Marca,
                Observaciones = turno.Observaciones,
                IdTipoVehiculo = turno.IdTipoVehiculo,
                Valor = turno.Valor,
                ValorBaseComision = turno.ValorBaseComision,
                IdServicios = servicios,
            });
        }

        [HttpPut("{id:int}/Operario")]
        [CajaLegacyWrite]
        public async Task<IActionResult> AsignarOperario(int id, [FromBody] AsignarOperarioRequest request)
        {
            if (request == null || request.IdOperario <= 0 || request.IdUsuario <= 0)
                return BadRequest("Datos de asignación inválidos");

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.IdUser == request.IdUsuario && !u.IsDelete);
            var esAdministrador = usuario != null
                && await PerfilAdministrador.EsAdministradorAsync(_context, usuario.PerfilId);
            if (!esAdministrador)
                return StatusCode(StatusCodes.Status403Forbidden, "Se requiere el perfil Administrador");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var turno = await _context.Turnos.FindAsync(id);
            if (turno == null)
                return NotFound("Turno no encontrado");
            if (turno.Estado)
                return Conflict("El turno ya no está en proceso");

            var operario = await _context.Operarios
                .FirstOrDefaultAsync(o => o.idOperario == request.IdOperario && !o.isDelete);
            if (operario == null)
                return NotFound("Operario no encontrado");

            if (turno.idOperario == request.IdOperario && turno.OperadorOcupado == true)
                return NoContent();

            var ocupado = await _context.Turnos.AnyAsync(t =>
                t.IdTurno != id && t.idOperario == request.IdOperario && t.OperadorOcupado == true);
            if (ocupado)
                return Conflict("El operario está ocupado en otro turno");

            // Misma regla que el WinForm: se requiere comisión válida para el día actual.
            var diaSemana = (int)DateTime.Now.DayOfWeek;
            var comision = await _context.OperarioComisiones
                .FirstOrDefaultAsync(c => c.DiaSemana == diaSemana && c.idOperario == request.IdOperario);
            if (comision == null || comision.Porcentaje <= 0)
                return Conflict("El operario no tiene comisión válida para el día actual");

            turno.idOperario = request.IdOperario;
            turno.PorcentajeComision = comision.Porcentaje;
            turno.FechaHoraAsignacionOperario = DateTime.Now;
            turno.OperadorOcupado = true;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return NoContent();
        }

        [HttpPut("{id:int}/EnProceso")]
        [CajaLegacyWrite]
        public async Task<IActionResult> ActualizarEnProceso(int id, [FromBody] ActualizarTurnoEnProcesoRequest request)
        {
            var servicios = request.IdServicios.Distinct().ToList();
            if (servicios.Count == 0 || request.IdTipoVehiculo <= 0 || request.Valor < 0 || request.ValorBaseComision < 0)
                return BadRequest("Datos del turno inválidos");

            var turno = await _context.Turnos.FindAsync(id);
            if (turno == null)
                return NotFound();
            if (turno.Estado)
                return Conflict("El turno ya no está en proceso");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            turno.NombreCliente = request.NombreCliente;
            turno.NumeroCelular = request.NumeroCelular;
            turno.CorreoElectronico = request.CorreoElectronico;
            turno.Marca = request.Marca;
            turno.Observaciones = request.Observaciones;
            turno.IdTipoVehiculo = request.IdTipoVehiculo;
            turno.Valor = request.Valor;
            turno.ValorBaseComision = request.ValorBaseComision;

            var existentes = await _context.TurnoServicios.Where(s => s.IdTurno == id).ToListAsync();
            foreach (var relacion in existentes)
                relacion.IsDeleted = !servicios.Contains(relacion.idServicios);

            foreach (var idServicio in servicios.Where(s => existentes.All(e => e.idServicios != s)))
                _context.TurnoServicios.Add(new TurnoServicios { idServicios = idServicio, IdTurno = id, IsDeleted = false });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return NoContent();
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
                ).Take(5).ToListAsync();

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
                        tur.idOperario,
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
                    OperadorOcupado = x.OperadorOcupado ?? false,
                    idOperario = x.idOperario,
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
