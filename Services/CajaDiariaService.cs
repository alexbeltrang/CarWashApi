using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carwash.API.Services;

public sealed record CajaEscritura(Guid IdOperacion, string Tipo, string Objetivo,
    IReadOnlyDictionary<int, long> RevisionesEsperadas, JsonElement Payload, bool Confirmar,
    bool Administrativa = false, bool Correccion = false, string? Motivo = null);
public sealed record CajaEfecto(int HttpStatus, JsonElement Resultado, JsonElement Identificadores,
    JsonElement Antes, JsonElement Despues, string? Motivo = null);

public sealed class CajaDiariaService(AppDbContext database, TimeProvider clock,
    IConfiguration configuration, IAuthorizationService authorization)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CajaResultadoOperacion> ExecuteAsync(ClaimsPrincipal actor, CajaEscritura command,
        Func<CancellationToken, Task<CajaEfecto>> effect, CancellationToken cancellationToken = default)
    {
        try
        {
        var author = await AuthorizeAsync(actor, command.Administrativa || command.Correccion, cancellationToken);
        if (command.IdOperacion == Guid.Empty || !command.Confirmar || string.IsNullOrWhiteSpace(command.Tipo)
            || string.IsNullOrWhiteSpace(command.Objetivo) || command.RevisionesEsperadas == null
            || command.RevisionesEsperadas.Any(pair => pair.Key <= 0 || pair.Value < 0)
            || (command.RevisionesEsperadas.Count == 0 && command.Tipo != "APERTURA"))
            throw new CajaException(400, "INVALID_COMMAND");
        var reason = command.Administrativa || command.Correccion ? CajaValidacion.Motivo(command.Motivo) : command.Motivo?.Trim();
        var hash = Fingerprint(JsonSerializer.SerializeToElement(new
        {
            command.Tipo, command.Objetivo, command.RevisionesEsperadas, command.Payload,
            command.Confirmar, command.Administrativa, command.Correccion, Motivo = reason
        }, JsonOptions));
        await database.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection())
                .BeginTransaction(IsolationLevel.Serializable, deferred: false);
            await using var registration = await database.Database.UseTransactionAsync(transaction, cancellationToken);
            var previous = await database.CajaOperaciones.AsNoTracking().SingleOrDefaultAsync(row => row.IdOperacion == command.IdOperacion, cancellationToken);
            if (previous != null)
            {
                if (previous.AutorId != author || previous.Tipo != command.Tipo || previous.Objetivo != command.Objetivo || previous.PayloadHash != hash)
                    throw new CajaException(409, "IDEMPOTENCY_CONFLICT");
                return JsonSerializer.Deserialize<CajaResultadoOperacion>(previous.ResultadoJson, JsonOptions)
                    ?? throw new CajaException(503, "CAJA_UNAVAILABLE");
            }
            if (await database.CajaDiaria.CountAsync(row => row.Estado, cancellationToken) > 1)
                throw new CajaException(409, "MULTIPLE_CAJAS_ACTIVAS");
            var controls = new List<CajaControl>();
            foreach (var expected in command.RevisionesEsperadas.OrderBy(pair => pair.Key))
                controls.Add(await ValidateWriteAsync(expected.Key, expected.Value, command.Correccion, cancellationToken, command.Tipo == "REAPERTURA"));
            var change = await effect(cancellationToken);
            if (change.HttpStatus is < 200 or >= 300) throw new InvalidOperationException("CAJA_EFFECT_NOT_CONFIRMED");
            controls.AddRange(database.ChangeTracker.Entries<CajaControl>()
                .Where(entry => entry.State == EntityState.Added && !controls.Contains(entry.Entity)).Select(entry => entry.Entity));
            foreach (var control in controls)
            {
                control.Revision = checked(control.Revision + 1);
                if (database.Entry(control).State == EntityState.Detached) database.CajaControl.Add(control);
            }
            var now = clock.GetUtcNow();
            var revisions = controls.ToDictionary(row => row.IdCaja, row => row.Revision);
            var result = new CajaResultadoOperacion(command.IdOperacion, change.HttpStatus, change.Resultado.Clone(), revisions, now);
            var boxes = JsonSerializer.Serialize(revisions.Keys.OrderBy(identifier => identifier));
            database.CajaOperaciones.Add(new CajaOperacion
            {
                IdOperacion = command.IdOperacion, Tipo = command.Tipo, Objetivo = command.Objetivo, AutorId = author,
                PayloadHash = hash, IdsCajaAfectadasJson = boxes, ConfirmadaEnUtc = now,
                HttpStatus = change.HttpStatus, ResultadoJson = JsonSerializer.Serialize(result, JsonOptions)
            });
            database.CajaAuditoria.Add(new CajaAuditoria
            {
                Id = Guid.NewGuid(), IdOperacion = command.IdOperacion, Accion = command.Tipo, AutorId = author,
                FechaUtc = now, Motivo = change.Motivo ?? reason, IdsCajaAfectadasJson = boxes,
                IdentificadoresJson = change.Identificadores.GetRawText(), AntesJson = change.Antes.GetRawText(), DespuesJson = change.Despues.GetRawText()
            });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqliteException) { database.ChangeTracker.Clear(); throw new CajaException(503, "CAJA_UNAVAILABLE"); }
        catch (DbUpdateException) { database.ChangeTracker.Clear(); throw new CajaException(503, "CAJA_UNAVAILABLE"); }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException or JsonException)
        { database.ChangeTracker.Clear(); throw new CajaException(503, "INVALID_SOURCE_DATA"); }
        catch { database.ChangeTracker.Clear(); throw; }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public async Task<CajaResultadoOperacion> GetOperacionAsync(ClaimsPrincipal actor, Guid identifier, CancellationToken cancellationToken = default)
    {
        try
        {
            var author = await AuthorizeAsync(actor, false, cancellationToken);
            var operation = await database.CajaOperaciones.AsNoTracking().SingleOrDefaultAsync(row => row.IdOperacion == identifier, cancellationToken)
                ?? throw new CajaException(404, "OPERATION_UNKNOWN");
            if (operation.AutorId != author) await AuthorizeAsync(actor, true, cancellationToken);
            return JsonSerializer.Deserialize<CajaResultadoOperacion>(operation.ResultadoJson, JsonOptions)
                ?? throw new CajaException(503, "CAJA_UNAVAILABLE");
        }
        catch (Exception error) when (error is SqliteException or JsonException or FormatException or InvalidCastException)
        { throw new CajaException(503, "CAJA_UNAVAILABLE"); }
    }

    private DateTime LocalNow() => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), BusinessZone()).DateTime;
    private static string Money(decimal amount) => amount.ToString("0.############################", CultureInfo.InvariantCulture);
    private static decimal Amount(string? value) => decimal.Parse(value ?? "0", CultureInfo.InvariantCulture);
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    public async Task<CajaPaginaResponse<CajaMovimientoResponse>> MovimientosAsync(ClaimsPrincipal actor, int identifier, int pagina = 1, int tamanoPagina = 50, CancellationToken token = default)
    {
        await AuthorizeAsync(actor, true, token);
        if (pagina < 1 || tamanoPagina is < 1 or > 200 || (long)(pagina - 1) * tamanoPagina > int.MaxValue) throw new CajaException(400, "INVALID_COMMAND");
        var source = await LeerFuentesAsync(actor, identifier, token);
        var rows = source.Movimientos.OrderByDescending(row => row.Fecha).ThenByDescending(row => row.Id).ToArray();
        return new(rows.Skip((pagina - 1) * tamanoPagina).Take(tamanoPagina).ToArray(), pagina, tamanoPagina, rows.LongLength, source.FuentesInvalidas);
    }

    public async Task<CajaImpactoResponse> ImpactoAsync(ClaimsPrincipal actor, int identifier, bool movimiento, CancellationToken token = default)
    {
        try
        {
            await AuthorizeAsync(actor, true, token);
            await database.Database.OpenConnectionAsync(token);
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection()).BeginTransaction(IsolationLevel.Serializable, deferred: true);
            await using var registration = await database.Database.UseTransactionAsync(transaction, token);
            var turnId = movimiento ? await database.TurnosMovimientos.Where(row => row.IdTurnoMovimientos == identifier)
                .Select(row => (int?)row.IdTurno).SingleOrDefaultAsync(token) ?? throw new CajaException(404, "NOT_FOUND") : identifier;
            var result = await LeerImpactoAsync(turnId, movimiento ? identifier : null, token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception error) when (error is SqliteException or FormatException or InvalidCastException or OverflowException)
        { throw new CajaException(503, "INVALID_SOURCE_DATA"); }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    private async Task<CajaImpactoResponse> LeerImpactoAsync(int identifier, int? movimiento, CancellationToken token)
    {
        var transaction = (SqliteTransaction)database.Database.CurrentTransaction!.GetDbTransaction();
        var balance = await LeerSaldoAsync(identifier, transaction, token);
        var turn = balance.Turno;
        var payments = await database.TurnosMovimientos.AsNoTracking().Where(row => row.IdTurno == identifier).OrderBy(row => row.IdTurnoMovimientos).ToListAsync(token);
        if (movimiento.HasValue && !payments.Any(row => row.IdTurnoMovimientos == movimiento)) throw new CajaException(404, "NOT_FOUND");
        var services = await database.TurnoServicios.AsNoTracking().Where(row => row.IdTurno == identifier).OrderBy(row => row.idTunoServicios).ToListAsync(token);
        var emails = await database.EmailNotifications.AsNoTracking().Where(row => row.IdTurno == identifier).ToListAsync(token);
        var messages = await database.WhatsAppNotifications.AsNoTracking().Where(row => row.IdTurno == identifier).ToListAsync(token);
        var jobs = await database.ImpresionesComprobante.AsNoTracking().Where(row => row.IdTurno == identifier).OrderBy(row => row.SolicitudId).ToListAsync(token);
        var receipt = await database.ComprobantesRetiro.AsNoTracking().SingleOrDefaultAsync(row => row.IdTurno == identifier, token);
        var invoice = await database.Facturas.AsNoTracking().SingleOrDefaultAsync(row => row.IdTurno == identifier, token);
        var boxes = payments.Select(row => row.IdCaja!.Value).Append(turn.idCajaDiaria!.Value).Distinct().OrderBy(value => value).ToArray();
        var revisions = new List<CajaRevisionResponse>();
        var controls = new List<CajaControl?>();
        var issues = new List<CajaFuenteInvalida>();
        foreach (var boxId in boxes)
        {
            var box = await database.CajaDiaria.AsNoTracking().SingleAsync(row => row.IdCaja == boxId, token);
            var control = await database.CajaControl.AsNoTracking().SingleOrDefaultAsync(row => row.IdCaja == boxId, token);
            var version = control?.IdCierreVigente == null ? 0 : await database.CajaCierres.Where(row => row.Id == control.IdCierreVigente).Select(row => row.Version).SingleAsync(token);
            revisions.Add(new(boxId, control?.Revision ?? 0, version));
            controls.Add(control);
            if (!box.Estado && control?.EnRevision != true) issues.Add(new("CajaDiaria", boxId, "CAJA_CERRADA"));
        }
        if (invoice != null) issues.Add(new("Factura", identifier, "FACTURA_EMITIDA"));
        if (turn.PagadoNomina) issues.Add(new("Turnos", identifier, "DEPENDENCY_LOCKED"));
        issues.AddRange(await DependenciasNoSoportadasAsync(identifier, turn.idOperario, transaction, token));
        foreach (var email in emails.Where(row => row.Estado is not ("ACCEPTED" or "FAILED" or "NOT_APPLICABLE"))) issues.Add(new("EmailNotifications", email.Id, "DEPENDENCY_LOCKED"));
        foreach (var message in messages.Where(row => row.Estado is not ("ACCEPTED" or "DELIVERED" or "FAILED" or "NOT_APPLICABLE"))) issues.Add(new("WhatsAppNotifications", message.Id, "DEPENDENCY_LOCKED"));
        if (jobs.Any(row => row.Estado is not ("SUBMITTED" or "FAILED"))) issues.Add(new("ImpresionesComprobante", identifier, "DEPENDENCY_LOCKED"));
        var ids = new Dictionary<string, int[]>
        {
            ["turnos"] = [identifier], ["movimientos"] = payments.Where(row => !movimiento.HasValue || row.IdTurnoMovimientos == movimiento).Select(row => row.IdTurnoMovimientos).ToArray(),
            ["servicios"] = movimiento.HasValue ? [] : services.Select(row => checked((int)row.idTunoServicios)).ToArray(),
            ["emails"] = movimiento.HasValue ? [] : emails.Select(row => row.Id).ToArray(),
            ["whatsapp"] = movimiento.HasValue ? [] : messages.Select(row => row.Id).ToArray(),
            ["comprobantes"] = !movimiento.HasValue && receipt != null ? [identifier] : []
        };
        var media = await database.FormaPagos.AsNoTracking().OrderBy(row => row.IdFormaPago).ToListAsync(token);
        var fingerprint = Fingerprint(Json(new { identifier, movimiento, turn, payments, services, emails, messages, jobs, receipt, invoice, revisions, controls, media, issues }));
        return new(fingerprint, revisions, ids, issues, payments.Where(row => !movimiento.HasValue || row.IdTurnoMovimientos == movimiento)
            .Select(row => new CajaMovimientoResponse(row.IdTurnoMovimientos, identifier, row.IdCaja,
                new DateTimeOffset(row.FechaMovimiento, BusinessZone().GetUtcOffset(row.FechaMovimiento)), Money(row.MontoPagado), row.IdFormaPago,
                Category(media.Single(item => item.IdFormaPago == row.IdFormaPago).Nombre), row.Naturaleza)).ToArray(),
            movimiento.HasValue ? [] : jobs.Select(row => row.SolicitudId).ToArray());
    }

    private static void ValidarImpacto(CajaImpactoResponse preview, string expected, IReadOnlyDictionary<int, long> revisions)
    {
        if (preview.Cajas.Count != revisions.Count || preview.Cajas.Any(row => !revisions.TryGetValue(row.IdCaja, out var revision) || revision != row.Revision)
            || string.IsNullOrEmpty(expected) || preview.ImpactoToken != expected) throw new CajaException(409, "STALE_IMPACT");
        if (preview.Bloqueos.Count > 0) throw new CajaException(409, preview.Bloqueos[0].Codigo);
    }

    private async Task<IReadOnlyList<CajaFuenteInvalida>> DependenciasNoSoportadasAsync(int identifier, int? operario, SqliteTransaction transaction, CancellationToken token)
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Turnos", "TurnosMovimientos", "TurnoServicios", "EmailNotifications", "WhatsAppNotifications", "ComprobantesRetiro", "ImpresionesComprobante", "Facturas" };
        var tables = new List<string>();
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) tables.Add(reader.GetString(0));
        var issues = new List<CajaFuenteInvalida>();
        foreach (var table in tables.Where(name => !supported.Contains(name)))
        {
            var columns = new List<string>();
            command.CommandText = "PRAGMA table_info(" + SqlName(table) + ")";
            await using (var reader = await command.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token)) columns.Add(reader.GetString(1));
            var relations = columns.Where(name => name.Equals("IdTurno", StringComparison.OrdinalIgnoreCase)).Select(name => (Column: name, Parent: "Turnos")).ToList();
            command.CommandText = "PRAGMA foreign_key_list(" + SqlName(table) + ")";
            await using (var reader = await command.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token))
                    if (supported.Contains(reader.GetString(2))) relations.Add((reader.GetString(3), reader.GetString(2)));
            foreach (var relation in relations.Distinct())
            {
                var match = relation.Parent.Equals("Turnos", StringComparison.OrdinalIgnoreCase) ? "$id"
                    : relation.Parent.Equals("TurnosMovimientos", StringComparison.OrdinalIgnoreCase) ? "(SELECT IdTurnoMovimientos FROM TurnosMovimientos WHERE IdTurno=$id)"
                    : relation.Parent.Equals("TurnoServicios", StringComparison.OrdinalIgnoreCase) ? "(SELECT idTunoServicios FROM TurnoServicios WHERE IdTurno=$id)" : null;
                if (match == null) { issues.Add(new(table, identifier, "DEPENDENCY_LOCKED")); continue; }
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", identifier);
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM " + SqlName(table) + " WHERE " + SqlName(relation.Column) + " IN " + (match == "$id" ? "($id)" : match) + ")";
                if (Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0) issues.Add(new(table, identifier, "DEPENDENCY_LOCKED"));
            }
            if (table.Contains("Liquidacion", StringComparison.OrdinalIgnoreCase) || table.Equals("JornadaOperario", StringComparison.OrdinalIgnoreCase))
            {
                var column = columns.SingleOrDefault(name => name.Equals("idOperario", StringComparison.OrdinalIgnoreCase));
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$operator", operario ?? 0);
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM " + SqlName(table) + (column == null ? "" : " WHERE " + SqlName(column) + "=$operator") + ")";
                if (Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0) issues.Add(new(table, identifier, "DEPENDENCY_LOCKED"));
            }
        }
        return issues;
    }

    private static string SqlName(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private async Task RecalcularEstadoAsync(int identifier, CancellationToken token)
    {
        var source = await LeerSaldoAsync(identifier, (SqliteTransaction)database.Database.CurrentTransaction!.GetDbTransaction(), token);
        var credits = await database.TurnosMovimientos.Where(row => row.IdTurno == identifier && row.Naturaleza == "CREDITO").ToListAsync(token);
        var completed = source.Pendiente == 0 || credits.Sum(row => row.MontoPagado) >= source.Pendiente;
        source.Turno.Pagado = completed;
        source.Turno.Estado = completed;
        source.Turno.FechaHoraFin = completed ? source.Turno.FechaHoraFin ?? LocalNow() : null;
        if (completed) source.Turno.OperadorOcupado = false;
        source.Turno.ValorComision = checked(source.Turno.ValorBaseComision * source.Turno.PorcentajeComision / 100m);
    }

    public Task<CajaResultadoOperacion> CorregirCobroAsync(ClaimsPrincipal actor, int identifier, CajaCorreccionCobroRequest request, CancellationToken token = default)
    {
        var amount = CajaValidacion.Dinero(request.Importe);
        var revisions = request.RevisionesEsperadas ?? new Dictionary<int, long>();
        return ExecuteAsync(actor, new(request.IdOperacion, "CORRECCION_COBRO", "movimiento:" + identifier, revisions, Json(request), request.Confirmar,
            Administrativa: true, Correccion: true, Motivo: request.Motivo), async cancellation =>
        {
            var movement = await database.TurnosMovimientos.SingleOrDefaultAsync(row => row.IdTurnoMovimientos == identifier, cancellation) ?? throw new CajaException(404, "NOT_FOUND");
            var preview = await LeerImpactoAsync(movement.IdTurno, identifier, cancellation);
            ValidarImpacto(preview, request.ImpactoToken, revisions);
            if (request.RevisionEsperada != revisions[movement.IdCaja!.Value]) throw new CajaException(409, "STALE_IMPACT");
            var category = await ValidateMediumAsync(request.IdFormaPago, true, cancellation);
            var turn = await database.Turnos.SingleAsync(row => row.IdTurno == movement.IdTurno, cancellation);
            var others = await database.TurnosMovimientos.AsNoTracking().Where(row => row.IdTurno == movement.IdTurno && row.IdTurnoMovimientos != identifier).ToListAsync(cancellation);
            if (category != "CREDITO" && others.Where(row => row.Naturaleza == "COBRO").Sum(row => row.MontoPagado) + amount > turn.Valor
                || category == "CREDITO" && others.Any(row => row.Naturaleza == "CREDITO")) throw new CajaException(400, "INVALID_COMMAND");
            if (category == "CREDITO" && (turn.idClienteCredito is not > 0 || !await database.ClienteCredito.AnyAsync(row => row.idClienteCredito == turn.idClienteCredito && row.Estado, cancellation)))
                throw new CajaException(400, "INVALID_COMMAND");
            var before = Json(new { idMovimiento = identifier, importe = Money(movement.MontoPagado), movement.IdFormaPago, movement.Naturaleza, movement.IdCaja, movement.IdTurno });
            movement.MontoPagado = amount;
            movement.IdFormaPago = request.IdFormaPago;
            movement.Naturaleza = category == "CREDITO" ? "CREDITO" : "COBRO";
            await database.SaveChangesAsync(cancellation);
            await RecalcularEstadoAsync(movement.IdTurno, cancellation);
            var result = Json(new { idMovimiento = identifier, idTurno = movement.IdTurno, importe = Money(amount), movement.IdFormaPago, movement.Naturaleza, movement.IdCaja });
            return new CajaEfecto(200, result, Json(preview.Identificadores), before, result);
        }, token);
    }

    public Task<CajaResultadoOperacion> BorrarAsync(ClaimsPrincipal actor, int identifier, bool movimiento, CajaBorradoRequest request, CancellationToken token = default) =>
        ExecuteAsync(actor, new(request.IdOperacion, movimiento ? "BORRADO_COBRO" : "BORRADO_TURNO", (movimiento ? "movimiento:" : "turno:") + identifier,
            request.RevisionesEsperadas, Json(request), request.Confirmar, Administrativa: true, Correccion: true, Motivo: request.Motivo), async cancellation =>
        {
            var payment = movimiento ? await database.TurnosMovimientos.SingleOrDefaultAsync(row => row.IdTurnoMovimientos == identifier, cancellation)
                ?? throw new CajaException(404, "NOT_FOUND") : null;
            var turnId = payment?.IdTurno ?? identifier;
            var preview = await LeerImpactoAsync(turnId, movimiento ? identifier : null, cancellation);
            ValidarImpacto(preview, request.ImpactoToken, request.RevisionesEsperadas);
            if (movimiento)
            {
                database.TurnosMovimientos.Remove(payment!);
                await database.SaveChangesAsync(cancellation);
                await RecalcularEstadoAsync(turnId, cancellation);
            }
            else
            {
                await database.ImpresionesComprobante.Where(row => row.IdTurno == turnId).ExecuteDeleteAsync(cancellation);
                await database.EmailNotifications.Where(row => row.IdTurno == turnId).ExecuteDeleteAsync(cancellation);
                await database.WhatsAppNotifications.Where(row => row.IdTurno == turnId).ExecuteDeleteAsync(cancellation);
                await database.ComprobantesRetiro.Where(row => row.IdTurno == turnId).ExecuteDeleteAsync(cancellation);
                await database.TurnoServicios.Where(row => row.IdTurno == turnId).ExecuteDeleteAsync(cancellation);
                await database.TurnosMovimientos.Where(row => row.IdTurno == turnId).ExecuteDeleteAsync(cancellation);
                await database.Turnos.Where(row => row.IdTurno == turnId).ExecuteDeleteAsync(cancellation);
            }
            var result = Json(new { idMovimiento = movimiento ? (int?)identifier : null, idTurno = turnId, eliminado = true });
            return new CajaEfecto(200, result, Json(preview.Identificadores), Json(new { movimientos = preview.Movimientos }), result);
        }, token);

    private async Task<string> ValidateMediumAsync(int identifier, bool allowCredit, CancellationToken cancellationToken)
    {
        var media = await database.FormaPagos.AsNoTracking().ToListAsync(cancellationToken);
        var selected = media.SingleOrDefault(row => row.IdFormaPago == identifier);
        var category = Category(selected?.Nombre);
        if (selected == null || category.Length == 0 || (!allowCredit && category == "CREDITO")
            || media.Count(row => Category(row.Nombre) == category) != 1)
            throw new CajaException(400, "INVALID_COMMAND");
        return category;
    }

    public Task<CajaResultadoOperacion> CobrarAsync(ClaimsPrincipal actor, int identifier, CajaCobroRequest request, CancellationToken cancellationToken = default)
    {
        var amount = CajaValidacion.Dinero(request.Importe);
        if (request.Observaciones?.Length > 2000) throw new CajaException(400, "INVALID_COMMAND");
        return ExecuteAsync(actor, new(request.IdOperacion, "COBRO", "turno:" + identifier,
            new Dictionary<int, long> { [request.IdCaja] = request.RevisionEsperada }, Json(request), request.Confirmar), async token =>
        {
            var category = await ValidateMediumAsync(request.IdFormaPago, true, token);
            var transaction = (SqliteTransaction)database.Database.CurrentTransaction!.GetDbTransaction();
            var source = await LeerSaldoAsync(identifier, transaction, token);
            var turn = source.Turno;
            var balance = source.Pendiente;
            if (balance <= 0 || amount > balance) throw new CajaException(400, "INVALID_COMMAND");
            if (turn.idOperario is not > 0 || !await database.Operarios.AnyAsync(row => row.idOperario == turn.idOperario && !row.isDelete, token)
                || turn.PorcentajeComision is < 0 or > 100 || turn.ValorBaseComision < 0)
                throw new CajaException(503, "INVALID_SOURCE_DATA");
            if (!turn.Estado && request.Finalizar != (amount == balance)) throw new CajaException(400, "INVALID_COMMAND");
            if (category == "CREDITO")
            {
                if (!request.Finalizar || amount != balance || source.TieneCredito
                    || request.IdClienteCredito is not > 0 || !await database.Set<ClienteCredito>().AnyAsync(row => row.idClienteCredito == request.IdClienteCredito && row.Estado, token))
                    throw new CajaException(400, "INVALID_COMMAND");
                turn.idClienteCredito = request.IdClienteCredito;
            }
            else if (request.IdClienteCredito.HasValue && request.IdClienteCredito != turn.idClienteCredito)
                throw new CajaException(400, "INVALID_COMMAND");
            var before = Json(new { idTurno = identifier, saldo = Money(balance), turn.Pagado, turn.Estado });
            var movement = new TurnosMovimientos { IdTurno = identifier, IdCaja = request.IdCaja, IdFormaPago = request.IdFormaPago,
                FechaMovimiento = LocalNow(), MontoPagado = amount, Naturaleza = category == "CREDITO" ? "CREDITO" : "COBRO" };
            database.TurnosMovimientos.Add(movement);
            turn.ValorComision = checked(turn.ValorBaseComision * turn.PorcentajeComision / 100m);
            if (request.Observaciones != null) turn.Observaciones = request.Observaciones.Trim();
            if (request.Finalizar && !turn.Estado)
            {
                turn.Estado = true;
                turn.Pagado = true;
                turn.OperadorOcupado = false;
                turn.FechaHoraFin = LocalNow();
            }
            var remaining = category == "CREDITO" ? balance : balance - amount;
            await database.SaveChangesAsync(token);
            var result = Json(new { idTurno = identifier, idMovimiento = movement.IdTurnoMovimientos, idCaja = request.IdCaja,
                saldoPendiente = Money(remaining), finalizado = turn.Estado, pagado = turn.Pagado, naturaleza = movement.Naturaleza,
                importe = Money(amount), idFormaPago = request.IdFormaPago });
            return new CajaEfecto(201, result, Json(new { idTurno = identifier, idMovimiento = movement.IdTurnoMovimientos }), before, result);
        }, cancellationToken);
    }

    public Task<CajaResultadoOperacion> GastoAsync(ClaimsPrincipal actor, int identifier, CajaGastoRequest request, CancellationToken cancellationToken = default)
    {
        var amount = CajaValidacion.Dinero(request.Importe);
        var concept = CajaValidacion.Motivo(request.Concepto);
        if (request.Observaciones?.Length > 2000) throw new CajaException(400, "INVALID_COMMAND");
        return ExecuteAsync(actor, new(request.IdOperacion, "GASTO", "caja:" + identifier,
            new Dictionary<int, long> { [identifier] = request.RevisionEsperada }, Json(request), request.Confirmar), async token =>
        {
            await ValidateMediumAsync(request.IdFormaPagoSalida, false, token);
            var expense = new GastosCaja { idCaja = identifier, FechaRegistro = LocalNow(), Valor = amount, Concepto = concept,
                Observaciones = request.Observaciones?.Trim() ?? "", IdFormaPagoSalida = request.IdFormaPagoSalida, DesembolsoConfirmado = true };
            database.GastosCaja.Add(expense);
            await database.SaveChangesAsync(token);
            var result = Json(new { idGasto = expense.IdGasto, idCaja = identifier, importe = Money(amount), idFormaPagoSalida = request.IdFormaPagoSalida });
            return new CajaEfecto(201, result, Json(new { idGasto = expense.IdGasto }), Json(new { }), result);
        }, cancellationToken);
    }

    public Task<CajaResultadoOperacion> CorregirSalidaAsync(ClaimsPrincipal actor, int boxId, int identifier, bool voucher, CajaCorreccionSalidaRequest request, CancellationToken token = default)
    {
        var amount = CajaValidacion.Dinero(request.Importe);
        return ExecuteAsync(actor, new(request.IdOperacion, voucher ? "CORRECCION_VALE" : "CORRECCION_GASTO", (voucher ? "vale:" : "gasto:") + identifier,
            new Dictionary<int, long> { [boxId] = request.RevisionEsperada }, Json(request), request.Confirmar, Administrativa: true, Correccion: true, Motivo: request.Motivo), async cancellation =>
        {
            await ValidateMediumAsync(request.IdFormaPagoSalida, false, cancellation);
            JsonElement before;
            if (voucher)
            {
                var row = await database.valesOperarios.SingleOrDefaultAsync(item => item.IdValeOperario == identifier && item.idCaja == boxId, cancellation) ?? throw new CajaException(404, "NOT_FOUND");
                if (row.Descontado != false || row.FechaDescuento != null) throw new CajaException(409, "DEPENDENCY_LOCKED");
                if (row.DesembolsoConfirmado != true) throw new CajaException(409, "INVALID_SOURCE_DATA");
                before = Json(new { id = identifier, importe = Money(row.Valor), row.IdFormaPagoSalida, row.idCaja, row.Descontado });
                row.Valor = amount;
                row.IdFormaPagoSalida = request.IdFormaPagoSalida;
            }
            else
            {
                var row = await database.GastosCaja.SingleOrDefaultAsync(item => item.IdGasto == identifier && item.idCaja == boxId, cancellation) ?? throw new CajaException(404, "NOT_FOUND");
                if (row.DesembolsoConfirmado != true) throw new CajaException(409, "INVALID_SOURCE_DATA");
                before = Json(new { id = identifier, importe = Money(row.Valor), row.IdFormaPagoSalida, row.idCaja });
                row.Valor = amount;
                row.IdFormaPagoSalida = request.IdFormaPagoSalida;
            }
            var result = Json(new { id = identifier, idCaja = boxId, importe = Money(amount), request.IdFormaPagoSalida });
            return new CajaEfecto(200, result, Json(new { id = identifier, idCaja = boxId }), before, result);
        }, token);
    }

    private sealed record SaldoLectura(Turnos Turno, decimal Pendiente, bool TieneCredito);

    private async Task<SaldoLectura> LeerSaldoAsync(int identifier, SqliteTransaction transaction, CancellationToken token,
        bool permitirCreditoSinReceptora = false)
    {
        var turn = await database.Turnos.SingleOrDefaultAsync(row => row.IdTurno == identifier, token) ?? throw new CajaException(404, "NOT_FOUND");
        var issues = new List<CajaFuenteInvalida>();
        var rawTurn = (await ReadRowsAsync("Turnos", ["IdTurno", "Valor", "ValorBaseComision"], transaction, token)).Single(row => Identifier(row, "IdTurno") == identifier);
        var total = Amount(SourceMoney(rawTurn, "Valor", issues, "Turnos", identifier, true));
        SourceMoney(rawTurn, "ValorBaseComision", issues, "Turnos", identifier, true);
        var payments = (await ReadRowsAsync("TurnosMovimientos", ["IdTurnoMovimientos", "IdTurno", "IdCaja", "IdFormaPago", "Naturaleza", "MontoPagado", "FechaMovimiento"], transaction, token))
            .Where(row => Identifier(row, "IdTurno") == identifier).ToArray();
        var received = 0m;
        foreach (var payment in payments)
        {
            var id = Identifier(payment, "IdTurnoMovimientos");
            var nature = payment["Naturaleza"].Texto;
            string medium;
            try { medium = await ValidateMediumAsync(Identifier(payment, "IdFormaPago"), true, token); }
            catch (CajaException) { throw new CajaException(503, "INVALID_SOURCE_DATA"); }
            var unreceivedCredit = permitirCreditoSinReceptora && payment["IdCaja"].Tipo == "null"
                && nature == "CREDITO" && medium == "CREDITO";
            if (nature is not ("COBRO" or "CREDITO") || (nature == "CREDITO") != (medium == "CREDITO")
                || (!unreceivedCredit && !await database.CajaDiaria.AnyAsync(row => row.IdCaja == Identifier(payment, "IdCaja"), token)))
                throw new CajaException(503, "INVALID_SOURCE_DATA");
            SourceDate(payment, "FechaMovimiento", BusinessZone(), issues, "TurnosMovimientos", id);
            var paid = Amount(SourceMoney(payment, "MontoPagado", issues, "TurnosMovimientos", id));
            if (nature == "COBRO") received = checked(received + paid);
        }
        if (issues.Count > 0 || turn.idCajaDiaria is not > 0 || received > total
            || !await database.CajaDiaria.AnyAsync(row => row.IdCaja == turn.idCajaDiaria, token)) throw new CajaException(503, "INVALID_SOURCE_DATA");
        return new(turn, checked(total - received), payments.Any(row => row["Naturaleza"].Texto == "CREDITO"));
    }

    public async Task<CajaSaldoResponse> SaldoAsync(ClaimsPrincipal actor, int identifier, CancellationToken token = default)
    {
        try
        {
            await AuthorizeAsync(actor, false, token);
            await database.Database.OpenConnectionAsync(token);
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection()).BeginTransaction(IsolationLevel.Serializable, deferred: true);
            await using var registration = await database.Database.UseTransactionAsync(transaction, token);
            var source = await LeerSaldoAsync(identifier, transaction, token, permitirCreditoSinReceptora: true);
            await transaction.CommitAsync(token);
            return new(identifier, Money(source.Pendiente), source.Turno.Estado, source.Turno.Pagado, source.Turno.idCajaDiaria!.Value);
        }
        catch (Exception error) when (error is SqliteException or FormatException or InvalidCastException or OverflowException)
        { throw new CajaException(503, "INVALID_SOURCE_DATA"); }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public Task<CajaResultadoOperacion> OperarioAsync(ClaimsPrincipal actor, int identifier, CajaOperarioRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(actor, new(request.IdOperacion, "OPERARIO", "turno:" + identifier,
            new Dictionary<int, long> { [request.IdCaja] = request.RevisionEsperada }, Json(request), request.Confirmar), async token =>
        {
            var turn = await database.Turnos.SingleOrDefaultAsync(row => row.IdTurno == identifier, token) ?? throw new CajaException(404, "NOT_FOUND");
            if (turn.idCajaDiaria != request.IdCaja || turn.Estado || turn.PagadoNomina) throw new CajaException(409, "INVALID_COMMAND");
            var before = Json(new { idTurno = identifier, turn.idOperario, turn.OperadorOcupado, turn.PorcentajeComision });
            if (request.Liberar)
            {
                if (request.IdOperario.HasValue || turn.idOperario is not > 0) throw new CajaException(400, "INVALID_COMMAND");
                turn.OperadorOcupado = false;
            }
            else
            {
                if (request.IdOperario is not > 0 || !await database.Operarios.AnyAsync(row => row.idOperario == request.IdOperario && !row.isDelete, token)
                    || await database.Turnos.AnyAsync(row => row.IdTurno != identifier && row.idOperario == request.IdOperario && row.OperadorOcupado == true, token))
                    throw new CajaException(409, "INVALID_COMMAND");
                var day = (int)LocalNow().DayOfWeek;
                var rules = await database.Set<OperarioComisiones>().Where(row => row.idOperario == request.IdOperario && row.DiaSemana == day).ToListAsync(token);
                if (rules.Count != 1 || rules[0].Porcentaje is <= 0 or > 100) throw new CajaException(409, "INVALID_SOURCE_DATA");
                turn.idOperario = request.IdOperario;
                turn.PorcentajeComision = rules[0].Porcentaje;
                turn.FechaHoraAsignacionOperario = LocalNow();
                turn.OperadorOcupado = true;
            }
            var result = Json(new { idTurno = identifier, idCaja = request.IdCaja, turn.idOperario, turn.OperadorOcupado, turn.PorcentajeComision });
            return new CajaEfecto(201, result, Json(new { idTurno = identifier }), before, result);
        }, cancellationToken);

    public Task<CajaResultadoOperacion> ValeAsync(ClaimsPrincipal actor, int identifier, CajaValeRequest request, CancellationToken cancellationToken = default)
    {
        var amount = CajaValidacion.Dinero(request.Importe);
        var reason = CajaValidacion.Motivo(request.Motivo);
        return ExecuteAsync(actor, new(request.IdOperacion, "VALE", "caja:" + identifier,
            new Dictionary<int, long> { [identifier] = request.RevisionEsperada }, Json(request), request.Confirmar, Motivo: reason), async token =>
        {
            await ValidateMediumAsync(request.IdFormaPagoSalida, false, token);
            if (!await database.Operarios.AnyAsync(row => row.idOperario == request.IdOperario && !row.isDelete, token)) throw new CajaException(400, "INVALID_COMMAND");
            var voucher = new ValesOperarios { idCaja = identifier, idOperario = request.IdOperario, FechaRegsitro = LocalNow(), Valor = amount,
                Motivo = reason, IdFormaPagoSalida = request.IdFormaPagoSalida, DesembolsoConfirmado = true, Descontado = false };
            database.Set<ValesOperarios>().Add(voucher);
            await database.SaveChangesAsync(token);
            var result = Json(new { idValeOperario = voucher.IdValeOperario, idCaja = identifier, importe = Money(amount), idFormaPagoSalida = request.IdFormaPagoSalida });
            return new CajaEfecto(201, result, Json(new { idValeOperario = voucher.IdValeOperario }), Json(new { }), result);
        }, cancellationToken);
    }

    public Task<CajaResultadoOperacion> AbrirAsync(ClaimsPrincipal actor, CajaAperturaRequest request, CancellationToken cancellationToken = default)
    {
        var amount = CajaValidacion.Dinero(request.MontoInicial, true);
        return ExecuteAsync(actor, new(request.IdOperacion, "APERTURA", "apertura", new Dictionary<int, long>(), Json(request), request.Confirmar), async token =>
        {
            if (await database.CajaDiaria.AnyAsync(row => row.Estado, token)) throw new CajaException(409, "CAJA_YA_ABIERTA");
            var box = new CajaDiaria { FechaApertura = LocalNow(), Estado = true, MontoInicial = amount };
            database.CajaDiaria.Add(box);
            await database.SaveChangesAsync(token);
            database.CajaControl.Add(new CajaControl { IdCaja = box.IdCaja });
            var value = Json(new { idCaja = box.IdCaja, montoInicial = Money(amount) });
            return new CajaEfecto(201, value, Json(new { idCaja = box.IdCaja }), Json(new { }), value);
        }, cancellationToken);
    }

    public async Task<CajaResumenResponse> ResumenAsync(ClaimsPrincipal actor, int identifier, CancellationToken cancellationToken = default) =>
        BuildSummary(await LeerFuentesAsync(actor, identifier, cancellationToken));

    private static CajaResumenResponse BuildSummary(CajaFuentesResponse sources)
    {
        CajaImporteMedioResponse[] Totals(IEnumerable<(int Id, string? Category, string? Value)> rows) => sources.Medios
            .Where(medium => medium.Categoria != "CREDITO" && medium.Categoria.Length > 0)
            .Select(medium => new CajaImporteMedioResponse(medium.IdFormaPago, medium.Nombre, medium.Categoria,
                Money(rows.Where(row => row.Id == medium.IdFormaPago).Sum(row => Amount(row.Value))))).ToArray();
        try
        {
            var payments = Totals(sources.Movimientos.Where(row => row.IdCaja == sources.IdCaja && row.Naturaleza == "COBRO")
                .Select(row => (row.IdFormaPago, row.Categoria, row.Importe)));
            var expenses = Totals(sources.Gastos.Where(row => row.IdCaja == sources.IdCaja && row.DesembolsoConfirmado == true)
                .Select(row => (row.IdFormaPagoSalida ?? 0, row.Categoria, row.Importe)));
            var vouchers = Totals(sources.Vales.Where(row => row.IdCaja == sources.IdCaja && row.DesembolsoConfirmado == true)
                .Select(row => (row.IdFormaPagoSalida ?? 0, row.Categoria, row.Importe)));
            decimal Cash(IEnumerable<CajaImporteMedioResponse> rows) => rows.Where(row => row.Categoria == "EFECTIVO").Sum(row => Amount(row.Importe));
            return new(sources.IdCaja, sources.Estado, sources.Revision, sources.ResumenToken, sources.Periodo,
                sources.Apertura ?? "0", payments, sources.CreditoPendiente ?? "0", expenses, vouchers,
                Money(checked(Amount(sources.Apertura) + Cash(payments) - Cash(expenses) - Cash(vouchers))),
                sources.DiscrepanciasAcumulados ?? [], sources.FuentesInvalidas,
                sources.Estado is "ABIERTA" or "EN_REVISION" && sources.FuentesInvalidas.Count == 0
                    && !sources.BloqueoCierrePorCreditoSinReceptora)
            { ConciliacionTotalFinal = sources.ConciliacionTotalFinal };
        }
        catch (OverflowException) { throw new CajaException(503, "INVALID_SOURCE_DATA"); }
    }

    public Task<CajaResultadoOperacion> CerrarAsync(ClaimsPrincipal actor, int identifier, CajaCierreRequest request, CancellationToken cancellationToken = default, bool recierre = false)
    {
        var counted = CajaValidacion.Dinero(request.EfectivoContado, true);
        return ExecuteAsync(actor, new(request.IdOperacion, recierre ? "RECIERRE" : "CIERRE", "caja:" + identifier,
            new Dictionary<int, long> { [identifier] = request.RevisionEsperada }, Json(request), request.Confirmar,
            Administrativa: recierre, Correccion: recierre, Motivo: recierre ? "Recierre administrativo" : null), async token =>
        {
            var sources = await LeerFuentesCoreAsync(actor, identifier, token);
            var summary = BuildSummary(sources);
            if (!summary.PuedeCerrar) throw new CajaException(503, "INVALID_SOURCE_DATA");
            if (string.IsNullOrEmpty(request.ResumenToken) || request.ResumenToken != summary.ResumenToken) throw new CajaException(409, "STALE_SUMMARY");
            var difference = checked(counted - Amount(summary.EfectivoEsperado));
            var reason = difference != 0 ? CajaValidacion.Motivo(request.JustificacionDiferencia)
                : string.IsNullOrWhiteSpace(request.JustificacionDiferencia) ? null : CajaValidacion.Motivo(request.JustificacionDiferencia);
            var authorId = await AuthorizeAsync(actor, false, token);
            var author = await database.Usuarios.AsNoTracking().SingleAsync(row => row.IdUser == authorId, token);
            var box = await database.CajaDiaria.SingleAsync(row => row.IdCaja == identifier, token);
            var control = database.CajaControl.Local.Single(row => row.IdCaja == identifier);
            var previous = control.IdCierreVigente == null ? null : await database.CajaCierres.AsNoTracking().SingleAsync(row => row.Id == control.IdCierreVigente, token);
            if (recierre ? !control.EnRevision || box.Estado || previous == null : control.IdCierreVigente != null || await database.CajaCierres.AnyAsync(row => row.IdCaja == identifier, token))
                throw new CajaException(409, "CAJA_CERRADA");
            box.Estado = false;
            if (!recierre) box.FechaCierre = LocalNow();
            var period = summary.Periodo with { Hasta = recierre ? summary.Periodo.Hasta : clock.GetUtcNow() };
            summary = summary with { Periodo = period };
            var snapshot = new CajaSnapshotResponse(1, summary, sources, authorId, author.DisplayName ?? author.UserName,
                clock.GetUtcNow(), Money(counted), Money(difference), reason, recierre ? control.MotivoRevision : null);
            var closing = new CajaCierre { Id = Guid.NewGuid(), IdCaja = identifier, Version = checked((previous?.Version ?? 0) + 1), IdCierreAnterior = previous?.Id,
                ConfirmadoPorId = authorId, Responsable = snapshot.Responsable, ConfirmadoEnUtc = snapshot.ConfirmadoEnUtc,
                RevisionFuentes = summary.Revision, ResumenToken = summary.ResumenToken, PeriodoInicio = box.FechaApertura,
                PeriodoFin = box.FechaCierre, Zona = summary.Periodo.Zona, SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
                EfectivoContado = counted, Diferencia = difference, JustificacionDiferencia = reason, MotivoRevision = snapshot.MotivoRevision };
            database.CajaCierres.Add(closing);
            control.IdCierreVigente = closing.Id;
            control.EnRevision = false;
            var result = Json(new CajaCierreResponse(closing.Id, identifier, closing.Version, "VERIFICADO", true, closing.Responsable,
                closing.ConfirmadoEnUtc, Json(snapshot), Money(counted), Money(difference), reason));
            return new CajaEfecto(201, result, Json(new { idCaja = identifier, idCierre = closing.Id }),
                Json(new { estado = recierre ? "EN_REVISION" : "ABIERTA", revision = summary.Revision }), Json(new { estado = "CERRADA", contado = Money(counted), diferencia = Money(difference), motivo = reason }), snapshot.MotivoRevision);
        }, cancellationToken);
    }

    private CajaCierreResponse LeerCierre(CajaCierre row, CajaControl? control) => new(row.Id, row.IdCaja, row.Version, row.Origen,
        control?.IdCierreVigente == row.Id, row.Responsable, row.ConfirmadoEnUtc,
        JsonSerializer.Deserialize<JsonElement>(row.SnapshotJson, JsonOptions), row.EfectivoContado.HasValue ? Money(row.EfectivoContado.Value) : null,
        row.Diferencia.HasValue ? Money(row.Diferencia.Value) : null, row.JustificacionDiferencia,
        control?.Revision ?? 0, control?.EnRevision ?? false, row.MotivoRevision);

    private CajaCierreResponse Legado(CajaDiaria box, CajaControl? control) => new(Guid.Empty, box.IdCaja, 0, "LEGADO_NO_VERIFICADO", true, null, null,
        Json(new { snapshotVersion = 1, origen = "LEGADO_NO_VERIFICADO", idCaja = box.IdCaja,
            fechaApertura = box.FechaApertura, fechaCierre = box.FechaCierre, apertura = Money(box.MontoInicial),
            totalIngresosEfectivo = Money(box.TotalIngresosEfectivo), totalIngresosTransferencias = Money(box.TotalIngresosTransferencias),
            totalIngresosDatafono = Money(box.TotalIngresosDatafono), totalIngresosCredito = Money(box.TotalIngresosCredito),
            totalEgresos = Money(box.TotalEgresos), totalValesOperarios = Money(box.TotalValesOperarios), totalFinal = Money(box.TotalFinal),
            efectivoContado = (string?)null, diferencia = (string?)null, responsable = (string?)null, confirmadoEnUtc = (DateTimeOffset?)null }),
        null, null, null, control?.Revision ?? 0, control?.EnRevision ?? false);

    public async Task<CajaPaginaResponse<CajaCierreResponse>> CierresAsync(ClaimsPrincipal actor, string? desde = null, string? hasta = null,
        int pagina = 1, int tamanoPagina = 50, CancellationToken token = default)
    {
        await AuthorizeAsync(actor, true, token);
        static DateOnly? Date(string? value) => value == null ? null : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new CajaException(400, "INVALID_COMMAND");
        var start = Date(desde);
        var end = Date(hasta);
        if (start > end || pagina < 1 || tamanoPagina is < 1 or > 200 || (long)(pagina - 1) * tamanoPagina > int.MaxValue) throw new CajaException(400, "INVALID_COMMAND");
        var rows = await LeerHistoricoAsync(actor, null, token);
        var filtered = rows.Where(row => (!start.HasValue || row.Date >= start) && (!end.HasValue || row.Date <= end))
            .OrderByDescending(row => row.Date).ThenByDescending(row => row.Value.IdCaja).ThenByDescending(row => row.Value.Version).ToArray();
        return new(filtered.Skip((pagina - 1) * tamanoPagina).Take(tamanoPagina).Select(row => row.Value).ToArray(), pagina, tamanoPagina, filtered.LongLength);
    }

    public async Task<CajaCierreResponse> CierreAsync(ClaimsPrincipal actor, int identifier, int version, CancellationToken token = default) =>
        (await LeerHistoricoAsync(actor, identifier, token)).Select(row => row.Value).SingleOrDefault(row => row.Version == version) ?? throw new CajaException(404, "NOT_FOUND");

    private async Task<List<(DateOnly Date, CajaCierreResponse Value)>> LeerHistoricoAsync(ClaimsPrincipal actor, int? identifier, CancellationToken token)
    {
        try
        {
            await AuthorizeAsync(actor, true, token);
            await database.Database.OpenConnectionAsync(token);
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection()).BeginTransaction(IsolationLevel.Serializable, deferred: true);
            await using var registration = await database.Database.UseTransactionAsync(transaction, token);
            var closures = await database.CajaCierres.AsNoTracking().Where(row => identifier == null || row.IdCaja == identifier).ToListAsync(token);
            var controls = await database.CajaControl.AsNoTracking().ToDictionaryAsync(row => row.IdCaja, token);
            var result = closures.Select(row => (DateOnly.FromDateTime(row.PeriodoFin ?? row.PeriodoInicio ?? throw new CajaException(503, "INVALID_SOURCE_DATA")), LeerCierre(row, controls.GetValueOrDefault(row.IdCaja)))).ToList();
            var legacy = await database.CajaDiaria.AsNoTracking().Where(row => !row.Estado && (identifier == null || row.IdCaja == identifier)).ToListAsync(token);
            result.AddRange(legacy.Where(box => !closures.Any(row => row.IdCaja == box.IdCaja)).Select(box => (DateOnly.FromDateTime(box.FechaCierre ?? box.FechaApertura), Legado(box, controls.GetValueOrDefault(box.IdCaja)))));
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception error) when (error is SqliteException or JsonException or FormatException or InvalidCastException or OverflowException)
        { throw new CajaException(503, "INVALID_SOURCE_DATA"); }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public Task<CajaResultadoOperacion> ReabrirAsync(ClaimsPrincipal actor, int identifier, CajaReaperturaRequest request, CancellationToken token = default) =>
        ExecuteAsync(actor, new(request.IdOperacion, "REAPERTURA", "caja:" + identifier, new Dictionary<int, long> { [identifier] = request.RevisionEsperada },
            Json(request), request.Confirmar, Administrativa: true, Motivo: request.Motivo), async cancellation =>
        {
            var box = await database.CajaDiaria.SingleAsync(row => row.IdCaja == identifier, cancellation);
            var control = database.CajaControl.Local.Single(row => row.IdCaja == identifier);
            if (box.Estado || control.EnRevision) throw new CajaException(409, "INVALID_COMMAND");
            var current = control.IdCierreVigente == null ? null : await database.CajaCierres.AsNoTracking().SingleAsync(row => row.Id == control.IdCierreVigente, cancellation);
            if ((current?.Version ?? 0) != request.VersionEsperada) throw new CajaException(409, "STALE_SUMMARY");
            if (current == null)
            {
                if (await database.CajaCierres.AnyAsync(row => row.IdCaja == identifier, cancellation)) throw new CajaException(503, "INVALID_SOURCE_DATA");
                current = new CajaCierre { Id = Guid.NewGuid(), IdCaja = identifier, Version = 1, Origen = "LEGADO_NO_VERIFICADO",
                    PeriodoInicio = box.FechaApertura, PeriodoFin = box.FechaCierre, Zona = BusinessZone().Id, SnapshotJson = Legado(box, control).Snapshot.GetRawText() };
                database.CajaCierres.Add(current);
                control.IdCierreVigente = current.Id;
            }
            control.EnRevision = true;
            control.RevisionAbiertaPor = await AuthorizeAsync(actor, true, cancellation);
            control.RevisionAbiertaEnUtc = clock.GetUtcNow();
            control.MotivoRevision = CajaValidacion.Motivo(request.Motivo);
            var result = Json(new { idCaja = identifier, estado = "EN_REVISION", version = current.Version, motivoRevision = control.MotivoRevision });
            return new CajaEfecto(200, result, Json(new { idCaja = identifier, idCierre = current.Id }), Json(new { estado = "CERRADA", version = current.Version }), result);
        }, token);

    public async Task<CajaDiaria> GetActivaAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        try
        {
            await AuthorizeAsync(actor, false, cancellationToken);
            await database.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection())
                .BeginTransaction(IsolationLevel.Serializable, deferred: true);
            await using var registration = await database.Database.UseTransactionAsync(transaction, cancellationToken);
            var boxes = await database.CajaDiaria.AsNoTracking().Where(row => row.Estado).Take(2).ToListAsync(cancellationToken);
            if (boxes.Count > 1) throw new CajaException(409, "MULTIPLE_CAJAS_ACTIVAS");
            var box = boxes.SingleOrDefault() ?? throw new CajaException(404, "NOT_FOUND");
            if (await database.CajaControl.AsNoTracking().AnyAsync(row => row.IdCaja == box.IdCaja && row.EnRevision, cancellationToken))
                throw new CajaException(503, "INVALID_SOURCE_DATA");
            await transaction.CommitAsync(cancellationToken);
            return box;
        }
        catch (Exception error) when (error is SqliteException or FormatException or InvalidCastException)
        { throw new CajaException(503, "CAJA_UNAVAILABLE"); }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public async Task RegistrarTurnoAsync(ClaimsPrincipal actor, Turnos turn, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(actor, false, cancellationToken);
        if (turn.idCajaDiaria is not > 0) throw new CajaException(400, "INVALID_COMMAND");
        if (turn.IdTurno != 0 || turn.Pagado || turn.Estado || turn.PagadoNomina || turn.idClienteCredito.HasValue
            || turn.ValorComision != 0 || turn.PorcentajeComision != 0 || turn.FechaHoraFin.HasValue)
            throw new CajaException(409, "CLIENT_UPDATE_REQUIRED");
        CajaValidacion.Dinero(turn.Valor.ToString(CultureInfo.InvariantCulture), true);
        CajaValidacion.Dinero(turn.ValorBaseComision.ToString(CultureInfo.InvariantCulture), true);
        var revision = await database.CajaControl.AsNoTracking().Where(row => row.IdCaja == turn.idCajaDiaria.Value)
            .Select(row => (long?)row.Revision).SingleOrDefaultAsync(cancellationToken) ?? 0;
        var command = new CajaEscritura(Guid.NewGuid(), "REGISTRO_TURNO", "registro-turno",
            new Dictionary<int, long> { [turn.idCajaDiaria.Value] = revision }, JsonSerializer.SerializeToElement(turn, JsonOptions), true);
        await ExecuteAsync(actor, command, async token =>
        {
            if (turn.IdTipoVehiculo <= 0 || !await database.TipoVehiculo.AnyAsync(row => row.IdTipoVehiculo == turn.IdTipoVehiculo && !row.IsDelete, token)
                || string.IsNullOrWhiteSpace(turn.Placa) || turn.FechaHoraIngreso.Year <= 1)
                throw new CajaException(400, "INVALID_COMMAND");
            database.Turnos.Add(turn);
            await database.SaveChangesAsync(token);
            var ids = JsonSerializer.SerializeToElement(new { idTurno = turn.IdTurno });
            return new CajaEfecto(201, ids, ids, JsonSerializer.SerializeToElement(new { }),
                JsonSerializer.SerializeToElement(new { turn.IdTurno, IdCaja = turn.idCajaDiaria }));
        }, cancellationToken);
    }

    public async Task ActualizarDatosTurnoAsync(ClaimsPrincipal actor, int identifier, JsonElement payload, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(actor, false, cancellationToken);
        if (payload.ValueKind != JsonValueKind.Object) throw new CajaException(400, "INVALID_COMMAND");
        Turnos incoming;
        try { incoming = payload.Deserialize<Turnos>(JsonOptions) ?? throw new JsonException(); }
        catch (JsonException) { throw new CajaException(400, "INVALID_COMMAND"); }
        var initial = await database.Turnos.AsNoTracking().SingleOrDefaultAsync(row => row.IdTurno == identifier, cancellationToken)
            ?? throw new CajaException(404, "NOT_FOUND");
        if (initial.idCajaDiaria is not > 0) throw new CajaException(503, "INVALID_SOURCE_DATA");
        var boxId = initial.idCajaDiaria.Value;
        var revision = await database.CajaControl.AsNoTracking().Where(row => row.IdCaja == boxId)
            .Select(row => (long?)row.Revision).SingleOrDefaultAsync(cancellationToken) ?? 0;
        var command = new CajaEscritura(Guid.NewGuid(), "DATOS_TURNO", "turno:" + identifier.ToString(CultureInfo.InvariantCulture),
            new Dictionary<int, long> { [boxId] = revision }, payload, true);
        await ExecuteAsync(actor, command, async token =>
        {
            var current = await database.Turnos.SingleOrDefaultAsync(row => row.IdTurno == identifier, token)
                ?? throw new CajaException(404, "NOT_FOUND");
            if (current.idCajaDiaria != boxId) throw new CajaException(409, "STALE_SUMMARY");
            var allowed = new HashSet<string>(["numeroTurno", "nombreCliente", "numeroCelular", "correoElectronico", "placa", "marca", "numeroOrden", "observaciones"], StringComparer.OrdinalIgnoreCase);
            var existing = JsonSerializer.SerializeToElement(current, JsonOptions).EnumerateObject().ToDictionary(row => row.Name, row => row.Value, StringComparer.OrdinalIgnoreCase);
            var requested = JsonSerializer.SerializeToElement(incoming, JsonOptions).EnumerateObject().ToDictionary(row => row.Name, row => row.Value, StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in payload.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new CajaException(400, "INVALID_COMMAND");
                if (allowed.Contains(property.Name)) continue;
                if (!existing.TryGetValue(property.Name, out var before) || !requested.TryGetValue(property.Name, out var after)
                    || Fingerprint(before) != Fingerprint(after)) throw new CajaException(409, "CLIENT_UPDATE_REQUIRED");
            }
            static string? Merge(string? next, string? previous) => string.IsNullOrWhiteSpace(next) ? previous : next;
            current.NumeroTurno = Merge(incoming.NumeroTurno, current.NumeroTurno);
            current.NombreCliente = Merge(incoming.NombreCliente, current.NombreCliente);
            current.NumeroCelular = Merge(incoming.NumeroCelular, current.NumeroCelular);
            current.CorreoElectronico = Merge(incoming.CorreoElectronico, current.CorreoElectronico);
            current.Placa = Merge(incoming.Placa, current.Placa)!;
            current.Marca = Merge(incoming.Marca, current.Marca);
            current.NumeroOrden = Merge(incoming.NumeroOrden, current.NumeroOrden);
            current.Observaciones = Merge(incoming.Observaciones, current.Observaciones);
            var ids = JsonSerializer.SerializeToElement(new { idTurno = identifier });
            return new CajaEfecto(200, ids, ids, JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }));
        }, cancellationToken);
    }

    public Task<CajaFuentesResponse> LeerFuentesAsync(ClaimsPrincipal actor, int identifier, CancellationToken cancellationToken = default)
        => LeerFuentesCoreAsync(actor, identifier, cancellationToken, permitirCreditoSinReceptora: true);

    private async Task<CajaFuentesResponse> LeerFuentesCoreAsync(ClaimsPrincipal actor, int identifier,
        CancellationToken cancellationToken, bool permitirCreditoSinReceptora = false)
    {
        await AuthorizeAsync(actor, false, cancellationToken);
        var current = database.Database.CurrentTransaction?.GetDbTransaction() as SqliteTransaction;
        if (current == null) await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var owned = current == null ? ((SqliteConnection)database.Database.GetDbConnection())
                .BeginTransaction(IsolationLevel.Serializable, deferred: true) : null;
            var transaction = current ?? owned!;
            await using var registration = current == null ? await database.Database.UseTransactionAsync(transaction, cancellationToken) : null;
            var boxes = await ReadRowsAsync("CajaDiaria", ["IdCaja", "FechaApertura", "FechaCierre", "MontoInicial", "Estado",
                "TotalIngresosEfectivo", "TotalIngresosTransferencias", "TotalIngresosDatafono", "TotalIngresosCredito",
                "TotalEgresos", "TotalValesOperarios", "TotalFinal"], transaction, cancellationToken);
            var box = boxes.SingleOrDefault(row => Identifier(row, "IdCaja") == identifier) ?? throw new CajaException(404, "NOT_FOUND");
            var control = await database.CajaControl.AsNoTracking().SingleOrDefaultAsync(row => row.IdCaja == identifier, cancellationToken);
            var issues = new List<CajaFuenteInvalida>();
            var open = Boolean(box, "Estado");
            if (open != true) await AuthorizeAsync(actor, true, cancellationToken);
            if (boxes.Count(row => Boolean(row, "Estado") == true) > 1) issues.Add(new("CajaDiaria", null, "MULTIPLE_CAJAS_ACTIVAS"));
            if (open == null || control?.Revision < 0 || (open == true && control?.EnRevision == true))
                issues.Add(new("CajaDiaria", identifier, "ESTADO_INVALIDO"));
            var state = control?.EnRevision == true ? "EN_REVISION" : open == true ? "ABIERTA" : "CERRADA";
            var zone = BusinessZone();
            var start = SourceDate(box, "FechaApertura", zone, issues, "CajaDiaria", identifier);
            var end = box["FechaCierre"].Tipo == "null" ? null : SourceDate(box, "FechaCierre", zone, issues, "CajaDiaria", identifier);
            if ((open != true && end == null) || (start.HasValue && end < start)) issues.Add(new("CajaDiaria", identifier, "PERIODO_INVALIDO"));
            var opening = SourceMoney(box, "MontoInicial", issues, "CajaDiaria", identifier, true);
            var catalogue = await ReadRowsAsync("FormaPago", ["IdFormaPago", "Nombre"], transaction, cancellationToken);
            var media = catalogue.Select(row => new CajaMedioResponse(Identifier(row, "IdFormaPago"), row["Nombre"].Texto ?? "",
                Category(row["Nombre"].Texto))).OrderBy(row => row.IdFormaPago).ToArray();
            var ambiguous = media.Where(row => row.Categoria.Length > 0).GroupBy(row => row.Categoria)
                .Where(group => group.Count() > 1).SelectMany(group => group.Select(row => row.IdFormaPago)).ToHashSet();
            string? Payment(int payment, string source, int id)
            {
                var match = media.SingleOrDefault(row => row.IdFormaPago == payment);
                if (payment <= 0 || match == null || match.Categoria.Length == 0 || ambiguous.Contains(payment))
                {
                    issues.Add(new(source, id, "MEDIO_NO_VERIFICABLE"));
                    return null;
                }
                return match.Categoria;
            }
            var turns = await ReadRowsAsync("Turnos", ["IdTurno", "idCajaDiaria", "Valor", "Pagado", "Estado", "PagadoNomina",
                "idClienteCredito", "idOperario", "ValorBaseComision", "PorcentajeComision", "ValorComision", "FechaHoraIngreso", "FechaHoraFin"], transaction, cancellationToken);
            var movements = await ReadRowsAsync("TurnosMovimientos", ["IdTurnoMovimientos", "MontoPagado", "FechaMovimiento", "IdTurno", "IdFormaPago", "IdCaja", "Naturaleza"], transaction, cancellationToken);
            var expenses = await ReadRowsAsync("GastosCaja", ["IdGasto", "idCaja", "FechaRegistro", "Valor", "IdFormaPagoSalida", "DesembolsoConfirmado"], transaction, cancellationToken);
            var vouchers = await ReadRowsAsync("valesOperarios", ["IdValeOperario", "idOperario", "idCaja", "FechaRegsitro", "Valor", "IdFormaPagoSalida", "DesembolsoConfirmado", "Descontado", "FechaDescuento"], transaction, cancellationToken);
            var tips = await ReadRowsAsync("RegistroPropinasOperarios", ["idPropina", "idCaja", "idOperario", "fechaRegistro", "valorPropina", "isDelete"], transaction, cancellationToken);
            var knownBoxes = boxes.Select(row => Identifier(row, "IdCaja")).ToHashSet();
            var knownTurns = turns.Select(row => Identifier(row, "IdTurno")).ToHashSet();
            bool UnreceivedCredit(Dictionary<string, RawValue> movement, string? category) => permitirCreditoSinReceptora
                && movement["IdCaja"].Tipo == "null" && movement["Naturaleza"].Texto == "CREDITO" && category == "CREDITO"
                && turns.Any(turn => Identifier(turn, "IdTurno") == Identifier(movement, "IdTurno")
                    && Identifier(turn, "idCajaDiaria") > 0 && knownBoxes.Contains(Identifier(turn, "idCajaDiaria")));
            var rows = new List<CajaMovimientoResponse>();
            foreach (var movement in movements)
            {
                var id = Identifier(movement, "IdTurnoMovimientos");
                var boxId = Identifier(movement, "IdCaja");
                var turnId = Identifier(movement, "IdTurno");
                if (boxId != identifier && knownBoxes.Contains(boxId)) continue;
                var category = Payment(Identifier(movement, "IdFormaPago"), "TurnosMovimientos", id);
                var nature = movement["Naturaleza"].Texto;
                var unreceivedCredit = UnreceivedCredit(movement, category);
                if (unreceivedCredit && !turns.Any(turn => Identifier(turn, "IdTurno") == turnId && Identifier(turn, "idCajaDiaria") == identifier)) continue;
                if (id <= 0 || (!unreceivedCredit && (boxId <= 0 || !knownBoxes.Contains(boxId))))
                    issues.Add(new("TurnosMovimientos", id, "ASIGNACION_NO_VERIFICABLE"));
                if (turnId <= 0 || !knownTurns.Contains(turnId)) issues.Add(new("TurnosMovimientos", id, "REFERENCIA_TURNO_INVALIDA"));
                if (nature is not ("COBRO" or "CREDITO") || (nature == "CREDITO") != (category == "CREDITO"))
                    issues.Add(new("TurnosMovimientos", id, "NATURALEZA_NO_VERIFICABLE"));
                var date = SourceDate(movement, "FechaMovimiento", zone, issues, "TurnosMovimientos", id);
                if (boxId == identifier && (date < start || date > end)) issues.Add(new("TurnosMovimientos", id, "FECHA_FUERA_JORNADA"));
                rows.Add(new(id, turnId, boxId > 0 ? boxId : null, date,
                    SourceMoney(movement, "MontoPagado", issues, "TurnosMovimientos", id), Identifier(movement, "IdFormaPago"), category, nature));
            }
            var relatedTurns = rows.Select(row => row.IdTurno).ToHashSet();
            foreach (var turn in turns.Where(row => relatedTurns.Contains(Identifier(row, "IdTurno")) || Identifier(row, "idCajaDiaria") == identifier))
            {
                var id = Identifier(turn, "IdTurno");
                SourceMoney(turn, "Valor", issues, "Turnos", id, true);
                if (id <= 0 || !knownBoxes.Contains(Identifier(turn, "idCajaDiaria"))) issues.Add(new("Turnos", id, "ASIGNACION_NO_VERIFICABLE"));
                foreach (var payment in movements.Where(row => Identifier(row, "IdTurno") == id && Identifier(row, "IdCaja") != identifier))
                {
                    var paymentId = Identifier(payment, "IdTurnoMovimientos");
                    var category = Payment(Identifier(payment, "IdFormaPago"), "TurnosMovimientos", paymentId);
                    if (!UnreceivedCredit(payment, category) && !knownBoxes.Contains(Identifier(payment, "IdCaja")))
                        issues.Add(new("TurnosMovimientos", paymentId, "ASIGNACION_NO_VERIFICABLE"));
                    SourceMoney(payment, "MontoPagado", issues, "TurnosMovimientos", paymentId);
                    SourceDate(payment, "FechaMovimiento", zone, issues, "TurnosMovimientos", paymentId);
                }
            }
            CajaSalidaResponse[] Outflows(List<Dictionary<string, RawValue>> sources, string source, string key, string dateKey)
            {
                var output = new List<CajaSalidaResponse>();
                foreach (var row in sources)
                {
                    var id = Identifier(row, key);
                    var boxId = Identifier(row, "idCaja");
                    if (boxId != identifier && knownBoxes.Contains(boxId)) continue;
                    if (id <= 0 || !knownBoxes.Contains(boxId)) issues.Add(new(source, id, "ASIGNACION_NO_VERIFICABLE"));
                    var medium = Identifier(row, "IdFormaPagoSalida");
                    var category = Payment(medium, source, id);
                    var confirmed = Boolean(row, "DesembolsoConfirmado");
                    if (confirmed == null || category == "CREDITO") issues.Add(new(source, id, "DESEMBOLSO_NO_VERIFICABLE"));
                    var date = SourceDate(row, dateKey, zone, issues, source, id);
                    if (boxId == identifier && (date < start || date > end)) issues.Add(new(source, id, "FECHA_FUERA_JORNADA"));
                    output.Add(new(id, boxId, date, SourceMoney(row, "Valor", issues, source, id), medium > 0 ? medium : null, category, confirmed));
                }
                return output.OrderBy(row => row.Id).ToArray();
            }
            var expenseRows = Outflows(expenses, "GastosCaja", "IdGasto", "FechaRegistro");
            var voucherRows = Outflows(vouchers, "valesOperarios", "IdValeOperario", "FechaRegsitro");
            foreach (var tip in tips.Where(row => Identifier(row, "idCaja") == identifier && Boolean(row, "isDelete") != true))
                issues.Add(new("RegistroPropinasOperarios", Identifier(tip, "idPropina"), "CLASIFICACION_NO_SOPORTADA"));
            var debt = 0m;
            foreach (var turn in turns.Where(row => Identifier(row, "idCajaDiaria") == identifier || relatedTurns.Contains(Identifier(row, "IdTurno"))))
            {
                var turnId = Identifier(turn, "IdTurno");
                var credit = movements.Any(row => Identifier(row, "IdTurno") == turnId && row["Naturaleza"].Texto == "CREDITO");
                if (!credit && Identifier(turn, "idClienteCredito") <= 0) continue;
                var value = Amount(SourceMoney(turn, "Valor", issues, "Turnos", turnId, true));
                var received = 0m;
                foreach (var movement in movements.Where(row => Identifier(row, "IdTurno") == turnId))
                {
                    var movementId = Identifier(movement, "IdTurnoMovimientos");
                    var category = Payment(Identifier(movement, "IdFormaPago"), "TurnosMovimientos", movementId);
                    var nature = movement["Naturaleza"].Texto;
                    if (nature is not ("COBRO" or "CREDITO") || (nature == "CREDITO") != (category == "CREDITO"))
                        issues.Add(new("TurnosMovimientos", movementId, "NATURALEZA_NO_VERIFICABLE"));
                    if (nature == "COBRO") received = checked(received + Amount(SourceMoney(movement, "MontoPagado", issues, "TurnosMovimientos", movementId)));
                }
                if (received > value) issues.Add(new("Turnos", turnId, "SALDO_INVALIDO"));
                debt = checked(debt + Math.Max(0, value - received));
            }
            var warnings = new List<string>();
            decimal TotalCategory(string category) => rows.Where(row => row.IdCaja == identifier && row.Naturaleza == "COBRO" && row.Categoria == category).Sum(row => Amount(row.Importe));
            var expectedCaches = new Dictionary<string, decimal>
            {
                ["TotalIngresosEfectivo"] = TotalCategory("EFECTIVO"), ["TotalIngresosTransferencias"] = TotalCategory("TRANSFERENCIA"),
                ["TotalIngresosDatafono"] = TotalCategory("DATAFONO"), ["TotalIngresosCredito"] = debt,
                ["TotalEgresos"] = expenseRows.Where(row => row.IdCaja == identifier && row.DesembolsoConfirmado == true).Sum(row => Amount(row.Importe)),
                ["TotalValesOperarios"] = voucherRows.Where(row => row.IdCaja == identifier && row.DesembolsoConfirmado == true).Sum(row => Amount(row.Importe))
            };
            expectedCaches["TotalFinal"] = checked(Amount(opening) + TotalCategory("EFECTIVO")
                - expenseRows.Where(row => row.IdCaja == identifier && row.DesembolsoConfirmado == true && row.Categoria == "EFECTIVO").Sum(row => Amount(row.Importe))
                - voucherRows.Where(row => row.IdCaja == identifier && row.DesembolsoConfirmado == true && row.Categoria == "EFECTIVO").Sum(row => Amount(row.Importe)));
            foreach (var cache in expectedCaches)
                if (!decimal.TryParse(box[cache.Key].Texto, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var cached) || cached != cache.Value)
                    warnings.Add(cache.Key);
            var token = Fingerprint(JsonSerializer.SerializeToElement(new
            {
                IdCaja = identifier, Revision = control?.Revision ?? 0, control?.EnRevision, control?.IdCierreVigente,
                Zona = zone.Id, Boxes = boxes, Catalogue = catalogue, Turns = turns, Movements = movements,
                Expenses = expenses, Vouchers = vouchers, Tips = tips
            }, JsonOptions));
            var result = new CajaFuentesResponse(identifier, state, control?.Revision ?? 0, token,
                new(start, end, zone.Id), opening, media, rows.OrderBy(row => row.Id).ToArray(), expenseRows, voucherRows,
                issues.Distinct().OrderBy(row => row.Fuente, StringComparer.Ordinal).ThenBy(row => row.Id).ThenBy(row => row.Codigo, StringComparer.Ordinal).ToArray(), Money(debt), warnings)
            {
                ConciliacionTotalFinal = permitirCreditoSinReceptora
                    ? ReconcileTotalFinal(box["TotalFinal"], issues.Count == 0 ? Money(expectedCaches["TotalFinal"]) : null) : null,
                BloqueoCierrePorCreditoSinReceptora = permitirCreditoSinReceptora
                    && movements.Any(row => row["IdCaja"].Tipo == "null" && row["Naturaleza"].Texto == "CREDITO")
            };
            if (owned != null) await owned.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqliteException) { throw new CajaException(503, "CAJA_UNAVAILABLE"); }
        catch (Exception error) when (error is FormatException or OverflowException or InvalidCastException)
        { throw new CajaException(503, "INVALID_SOURCE_DATA"); }
        finally { if (current == null) await database.Database.CloseConnectionAsync(); }
    }

    private static CajaConciliacionTotalFinalResponse ReconcileTotalFinal(RawValue raw, string? projected)
    {
        string? declared = null;
        try
        {
            if (raw.Tipo is "text" or "integer") declared = Money(DecimalText.Parse(raw.Texto));
        }
        catch (Exception error) when (error is FormatException or OverflowException) { }
        var difference = declared != null && projected != null ? DocumentaryDifference(Amount(declared), Amount(projected)) : null;
        return new(declared, projected, difference, null, "CajaDiaria.TotalFinal", "FUENTES_CLASIFICADAS", "DESCONOCIDO_NO_CONFIRMADO");
    }

    private static string DocumentaryDifference(decimal declared, decimal projected)
    {
        var declaredBits = decimal.GetBits(declared);
        var projectedBits = decimal.GetBits(projected);
        var scale = Math.Max((declaredBits[3] >> 16) & 0xff, (projectedBits[3] >> 16) & 0xff);
        System.Numerics.BigInteger Units(int[] bits)
        {
            var coefficient = (System.Numerics.BigInteger)(uint)bits[0]
                | ((System.Numerics.BigInteger)(uint)bits[1] << 32)
                | ((System.Numerics.BigInteger)(uint)bits[2] << 64);
            return (bits[3] < 0 ? -coefficient : coefficient)
                * System.Numerics.BigInteger.Pow(10, scale - ((bits[3] >> 16) & 0xff));
        }
        var difference = Units(declaredBits) - Units(projectedBits);
        var digits = System.Numerics.BigInteger.Abs(difference).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        return (difference.Sign < 0 ? "-" : "")
            + (scale == 0 ? digits : digits.Insert(digits.Length - scale, ".").TrimEnd('0').TrimEnd('.'));
    }

    private async Task<List<Dictionary<string, RawValue>>> ReadRowsAsync(string table, string[] columns,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {string.Join(",", columns.Select(column => $"CAST(\"{column}\" AS TEXT),typeof(\"{column}\")"))} FROM \"{table}\" ORDER BY \"{columns[0]}\"";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, RawValue>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, RawValue>(StringComparer.Ordinal);
            for (var index = 0; index < columns.Length; index++)
                row.Add(columns[index], new(reader.IsDBNull(index * 2) ? null : reader.GetString(index * 2), reader.GetString(index * 2 + 1)));
            rows.Add(row);
        }
        return rows;
    }

    private static int Identifier(Dictionary<string, RawValue> row, string column) =>
        int.TryParse(row[column].Texto, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 0;
    private static bool? Boolean(Dictionary<string, RawValue> row, string column) => row[column].Texto switch { "0" => false, "1" => true, _ => null };

    private static string? SourceMoney(Dictionary<string, RawValue> row, string column, List<CajaFuenteInvalida> issues,
        string source, int id, bool zero = false)
    {
        var raw = row[column];
        if (raw.Tipo is "text" or "integer" && raw.Texto is { Length: > 0 and <= 60 } text
            && System.Text.RegularExpressions.Regex.IsMatch(text, @"^(0|[1-9][0-9]*)(\.[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && (zero ? amount >= 0 : amount > 0)
            && amount.ToString("0.############################", CultureInfo.InvariantCulture) == (text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text))
            return amount.ToString(CultureInfo.InvariantCulture);
        issues.Add(new(source, id, "IMPORTE_NO_VERIFICABLE"));
        return null;
    }

    private static DateTimeOffset? SourceDate(Dictionary<string, RawValue> row, string column, TimeZoneInfo zone,
        List<CajaFuenteInvalida> issues, string source, int id)
    {
        if (row[column].Tipo == "text" && DateTime.TryParseExact(row[column].Texto,
            ["yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1
            && !zone.IsInvalidTime(date) && !zone.IsAmbiguousTime(date))
            return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), zone.GetUtcOffset(date));
        issues.Add(new(source, id, "FECHA_NO_VERIFICABLE"));
        return null;
    }

    private static string Category(string? name)
    {
        var normalized = new string((name ?? "").Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray()).Trim().ToUpperInvariant();
        return normalized switch { "EFECTIVO" => "EFECTIVO", "TRANSFERENCIA" or "TRANSFERENCIAS" => "TRANSFERENCIA", "DATAFONO" => "DATAFONO", "CREDITO" => "CREDITO", _ => "" };
    }

    private TimeZoneInfo BusinessZone()
    {
        try
        {
            var identifier = configuration["BusinessTimeZoneId"];
            return string.IsNullOrWhiteSpace(identifier) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(identifier);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new CajaException(503, "CAJA_UNAVAILABLE"); }
    }

    private sealed record RawValue(string? Texto, string Tipo);

    private async Task<CajaControl> ValidateWriteAsync(int identifier, long revision, bool correction, CancellationToken cancellationToken, bool reopening = false)
    {
        var box = await database.CajaDiaria.AsNoTracking().SingleOrDefaultAsync(row => row.IdCaja == identifier, cancellationToken)
            ?? throw new CajaException(404, "NOT_FOUND");
        var control = await database.CajaControl.SingleOrDefaultAsync(row => row.IdCaja == identifier, cancellationToken)
            ?? new CajaControl { IdCaja = identifier };
        if (box.Estado && control.EnRevision) throw new CajaException(503, "INVALID_SOURCE_DATA");
        if (control.EnRevision && !correction) throw new CajaException(409, "REVISION_SOLO_CORRECCION");
        if (!box.Estado && !control.EnRevision && !reopening) throw new CajaException(409, "CAJA_CERRADA");
        if (control.Revision != revision) throw new CajaException(409, "STALE_SUMMARY");
        if (database.Entry(control).State == EntityState.Detached) database.CajaControl.Add(control);
        return control;
    }

    private async Task<int> AuthorizeAsync(ClaimsPrincipal actor, bool admin, CancellationToken cancellationToken)
    {
        if (actor.Identity?.IsAuthenticated != true
            || !int.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var identifier)
            || identifier <= 0 || !await database.Usuarios.AsNoTracking().AnyAsync(row => row.IdUser == identifier && !row.IsDelete, cancellationToken))
            throw new CajaException(401, "SESSION_REQUIRED");
        if (admin && !(await authorization.AuthorizeAsync(actor, AdministradorAuthorizationHandler.PolicyName)).Succeeded)
            throw new CajaException(403, "ADMIN_REQUIRED");
        return identifier;
    }

    private static string Fingerprint(JsonElement value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) CanonicalJson(writer, value);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    private static void CanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new CajaException(400, "INVALID_COMMAND");
                writer.WriteStartObject();
                foreach (var property in properties) { writer.WritePropertyName(property.Name); CanonicalJson(writer, property.Value); }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) CanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (!value.TryGetDecimal(out var number)) throw new CajaException(400, "INVALID_COMMAND");
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Undefined: throw new CajaException(400, "INVALID_COMMAND");
            default: value.WriteTo(writer); break;
        }
    }
}