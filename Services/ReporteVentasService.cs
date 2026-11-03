using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Carwash.API.Context;
using Carwash.API.DTOs;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Services;

public sealed class ReporteVentasService(AppDbContext database, TimeProvider clock, IConfiguration configuration)
{
    public DateOnly GetBusinessDate() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), BusinessZone()).DateTime);

    public async Task<ReporteVentasDto> GetAsync(ReporteVentasRequest request, CancellationToken cancellationToken)
    {
        var zone = BusinessZone();
        var fingerprint = Fingerprint(request.Criterios);
        var cursor = ReadCursor(request, fingerprint);
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection()).BeginTransaction(IsolationLevel.Serializable, deferred: true);
            await using var registration = await database.Database.UseTransactionAsync(transaction, cancellationToken);
            var query = Filtered(request.Criterios);
            await ValidateKeysAsync(query, cancellationToken);
            var count = await query.LongCountAsync(cancellationToken);
            var total = await query.Select(row => (decimal?)row.Amount).SumAsync(cancellationToken) ?? 0m;
            var groups = await query.GroupBy(row => row.PaymentMethodId)
                .Select(group => new { Id = group.Key, Name = group.Max(row => row.PaymentMethod), Count = group.Count(), Total = group.Sum(row => (decimal?)row.Amount) })
                .ToListAsync(cancellationToken);
            var summary = groups.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(row => new ReporteVentasResumenDto(row.Id, NameOrNull(row.Name), row.Count, (row.Total ?? 0m).ToString(CultureInfo.InvariantCulture))).ToArray();
            if (cursor != null)
                query = query.Where(row => row.Date < cursor.Date || (row.Date == cursor.Date && row.Id < cursor.Id));
            var rows = await query.OrderByDescending(row => row.Date).ThenByDescending(row => row.Id)
                .Take(request.PageSize + 1).ToListAsync(cancellationToken);
            var page = rows.Take(request.PageSize).ToArray();
            var items = page.Select(row => new ReporteVentasMovimientoDto(
                row.Id, row.TurnId, ToBusinessTime(row, zone), row.Plate,
                row.VehicleTypeId, row.VehicleType, row.OperatorId, row.Operator,
                row.PaymentMethodId, row.PaymentMethod, row.Amount.ToString(CultureInfo.InvariantCulture))).ToArray();
            var next = rows.Count > request.PageSize
                ? WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new CursorKey(page[^1].Date, page[^1].Id, fingerprint)))
                : null;
            var now = clock.GetUtcNow();
            await transaction.CommitAsync(cancellationToken);
            return new(request.Criterios, count, total.ToString(CultureInfo.InvariantCulture), items, next, request.PageSize,
                now, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime), summary);
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public async Task<ReporteVentasFiltrosDto> GetOptionsAsync(ReporteVentasCriteria criteria, CancellationToken cancellationToken)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = ((SqliteConnection)database.Database.GetDbConnection()).BeginTransaction(IsolationLevel.Serializable, deferred: true);
            await using var registration = await database.Database.UseTransactionAsync(transaction, cancellationToken);
            var payments = await database.FormaPagos.AsNoTracking().Where(row => row.IdFormaPago > 0)
                .Select(row => new { Id = row.IdFormaPago, Name = row.Nombre }).ToListAsync(cancellationToken);
            var employees = await database.Operarios.AsNoTracking().Where(row => row.idOperario > 0 && !row.isDelete)
                .Select(row => new { Id = row.idOperario, Name = ((row.Nombres ?? "") + " " + (row.Apellidos ?? "")).Trim() }).ToListAsync(cancellationToken);
            var vehicles = await database.TipoVehiculo.AsNoTracking().Where(row => row.IdTipoVehiculo > 0 && !row.IsDelete)
                .Select(row => new { Id = row.IdTipoVehiculo, Name = row.Nombre }).ToListAsync(cancellationToken);
            payments = payments.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            employees = employees.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            vehicles = vehicles.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            await transaction.CommitAsync(cancellationToken);
            return new(criteria.FechaInicial, criteria.FechaFinal, GetBusinessDate(),
                payments.Select(row => new ReporteFiltroDto(row.Id, NameOrNull(row.Name))).ToArray(),
                employees.Select(row => new ReporteFiltroDto(row.Id, NameOrNull(row.Name))).ToArray(),
                vehicles.Select(row => new ReporteFiltroDto(row.Id, NameOrNull(row.Name))).ToArray());
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    private static string? NameOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task ValidateKeysAsync(IQueryable<ReportRow> query, CancellationToken cancellationToken)
    {
        if (await query.AnyAsync(row => row.Id <= 0 || row.TurnId <= 0 || row.PaymentMethodId <= 0, cancellationToken))
            throw new ReporteVentasException(503, "INVALID_SOURCE_DATA");
    }

    private IQueryable<ReportRow> Filtered(ReporteVentasCriteria criteria)
    {
        var start = criteria.FechaInicial.ToDateTime(TimeOnly.MinValue);
        var end = criteria.FechaFinal.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var query = from movement in database.TurnosMovimientos.AsNoTracking()
                    where movement.FechaMovimiento >= start && movement.FechaMovimiento < end
                    join turn in database.Turnos.AsNoTracking() on movement.IdTurno equals turn.IdTurno into turns
                    from turn in turns.DefaultIfEmpty()
                    join payment in database.FormaPagos.AsNoTracking() on movement.IdFormaPago equals payment.IdFormaPago into payments
                    from payment in payments.DefaultIfEmpty()
                    join vehicle in database.TipoVehiculo.AsNoTracking() on turn.IdTipoVehiculo equals vehicle.IdTipoVehiculo into vehicles
                    from vehicle in vehicles.DefaultIfEmpty()
                    join employee in database.Operarios.AsNoTracking() on turn.idOperario equals employee.idOperario into employees
                    from employee in employees.DefaultIfEmpty()
                    select new ReportRow
                    {
                        Id = movement.IdTurnoMovimientos, TurnId = movement.IdTurno,
                        Date = movement.FechaMovimiento, Amount = movement.MontoPagado,
                        Plate = turn == null ? null : turn.Placa,
                        VehicleTypeId = turn == null ? null : turn.IdTipoVehiculo,
                        VehicleType = vehicle == null ? null : vehicle.Nombre,
                        OperatorId = turn == null ? null : turn.idOperario,
                        Operator = employee == null ? null : ((employee.Nombres ?? "") + " " + (employee.Apellidos ?? "")).Trim(),
                        PaymentMethodId = movement.IdFormaPago,
                        PaymentMethod = payment == null ? null : payment.Nombre,
                    };
        if (criteria.IdFormaPago.HasValue) query = query.Where(row => row.PaymentMethodId == criteria.IdFormaPago.Value);
        if (criteria.IdOperario.HasValue) query = query.Where(row => row.OperatorId == criteria.IdOperario.Value);
        if (criteria.IdTipoVehiculo.HasValue) query = query.Where(row => row.VehicleTypeId == criteria.IdTipoVehiculo.Value);
        if (criteria.Placa != null) query = query.Where(row => row.Plate != null && row.Plate.Trim().ToUpper() == criteria.Placa);
        return query;
    }

    private TimeZoneInfo BusinessZone()
    {
        var identifier = configuration["BusinessTimeZoneId"];
        try { return string.IsNullOrWhiteSpace(identifier) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(identifier); }
        catch (TimeZoneNotFoundException) { throw new ReporteVentasException(503, "REPORT_UNAVAILABLE"); }
        catch (InvalidTimeZoneException) { throw new ReporteVentasException(503, "REPORT_UNAVAILABLE"); }
    }

    private static DateTimeOffset ToBusinessTime(ReportRow row, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(row.Date, DateTimeKind.Unspecified);
        if (row.Id <= 0 || row.TurnId <= 0 || row.PaymentMethodId <= 0 || zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ReporteVentasException(503, "INVALID_SOURCE_DATA");
        try { return new DateTimeOffset(local, zone.GetUtcOffset(local)); }
        catch (ArgumentException) { throw new ReporteVentasException(503, "INVALID_SOURCE_DATA"); }
    }

    private static string Fingerprint(ReporteVentasCriteria criteria) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(criteria)));

    private static CursorKey? ReadCursor(ReporteVentasRequest request, string fingerprint)
    {
        if (request.Cursor == null) return null;
        try
        {
            if (request.Cursor.Length is < 1 or > 2048) throw new FormatException();
            var bytes = WebEncoders.Base64UrlDecode(request.Cursor);
            if (WebEncoders.Base64UrlEncode(bytes) != request.Cursor) throw new FormatException();
            var key = JsonSerializer.Deserialize<CursorKey>(bytes);
            if (key == null || key.Fingerprint != fingerprint || key.Id <= 0 || key.Date.Kind != DateTimeKind.Unspecified
                || key.Date.Ticks % TimeSpan.TicksPerMillisecond != 0
                || DateOnly.FromDateTime(key.Date) < request.Criterios.FechaInicial
                || DateOnly.FromDateTime(key.Date) > request.Criterios.FechaFinal) throw new FormatException();
            return key;
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException)
        {
            throw new ReporteVentasException(400, "INVALID_CURSOR", new Dictionary<string, string[]> { ["cursor"] = ["El cursor no corresponde a estos criterios."] });
        }
    }

    private sealed record CursorKey(DateTime Date, int Id, string Fingerprint);

    private sealed class ReportRow
    {
        public int Id { get; init; }
        public int TurnId { get; init; }
        public DateTime Date { get; init; }
        public decimal Amount { get; init; }
        public string? Plate { get; init; }
        public int? VehicleTypeId { get; init; }
        public string? VehicleType { get; init; }
        public int? OperatorId { get; init; }
        public string? Operator { get; init; }
        public int PaymentMethodId { get; init; }
        public string? PaymentMethod { get; init; }
    }
}