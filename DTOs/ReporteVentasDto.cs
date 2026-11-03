using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace Carwash.API.DTOs;

public sealed record ReporteVentasQuery
{
    public override string ToString() => nameof(ReporteVentasQuery);

    public string? FechaInicial { get; init; }
    public string? FechaFinal { get; init; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? IdFormaPago { get; init; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? IdOperario { get; init; }
    public string? Placa { get; init; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? IdTipoVehiculo { get; init; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? PageSize { get; init; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Cursor { get; init; }

    public ReporteVentasRequest Parse()
    {
        var errors = new Dictionary<string, string[]>();
        var validInitial = DateOnly.TryParseExact(FechaInicial, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var initial);
        var validFinal = DateOnly.TryParseExact(FechaFinal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var final);
        if (!validInitial) errors["fechaInicial"] = ["La fecha inicial debe tener formato yyyy-MM-dd."];
        if (!validFinal || final == DateOnly.MaxValue) errors["fechaFinal"] = ["La fecha final debe permitir calcular el dia siguiente."];
        if (validInitial && validFinal && initial > final) errors["fechaFinal"] = ["La fecha final debe ser igual o posterior a la inicial."];
        var payment = ParseId(IdFormaPago, "idFormaPago", errors);
        var employee = ParseId(IdOperario, "idOperario", errors);
        var vehicle = ParseId(IdTipoVehiculo, "idTipoVehiculo", errors);
        var size = 50;
        if (PageSize != null && (!int.TryParse(PageSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 200))
            errors["pageSize"] = ["El tamano de pagina debe estar entre 1 y 200."];
        if (errors.Count != 0) throw new ReporteVentasException(400, "INVALID_FILTERS", errors);
        return new(new(initial, final, payment, employee, string.IsNullOrWhiteSpace(Placa) ? null : Placa.Trim().ToUpperInvariant(), vehicle), size, Cursor);
    }

    private static int? ParseId(string? value, string field, Dictionary<string, string[]> errors)
    {
        if (value == null) return null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) return parsed;
        errors[field] = ["El identificador debe ser un entero positivo."];
        return null;
    }
}

public sealed record ReporteVentasCriteria(DateOnly FechaInicial, DateOnly FechaFinal, int? IdFormaPago, int? IdOperario, string? Placa, int? IdTipoVehiculo)
{
    public override string ToString() => nameof(ReporteVentasCriteria);
}
public sealed record ReporteVentasRequest(ReporteVentasCriteria Criterios, int PageSize, string? Cursor);
public sealed record ReporteVentasMovimientoDto(
    int IdMovimiento, int IdTurno, DateTimeOffset FechaCobro, string? Placa,
    int? IdTipoVehiculo, string? TipoVehiculo, int? IdOperario, string? Operario,
    int IdFormaPago, string? FormaPago, string MontoPagado)
{
    public override string ToString() => nameof(ReporteVentasMovimientoDto);
}
public sealed record ReporteVentasDto(
    ReporteVentasCriteria Criterios, long CantidadCobros, string ImporteTotal,
    IReadOnlyList<ReporteVentasMovimientoDto> Items, string? NextCursor, int PageSize,
    DateTimeOffset ConsultadoEn, DateOnly FechaOperativa,
    IReadOnlyList<ReporteVentasResumenDto>? Resumen = null);
public sealed record ReporteVentasResumenDto(int IdFormaPago, string? FormaPago, long Cantidad, string Importe);

public sealed record ReporteFiltroDto(int Id, string? Nombre);
public sealed record ReporteVentasFiltrosDto(DateOnly FechaInicial, DateOnly FechaFinal, DateOnly FechaOperativa,
    IReadOnlyList<ReporteFiltroDto> FormasPago, IReadOnlyList<ReporteFiltroDto> Operarios, IReadOnlyList<ReporteFiltroDto> TiposVehiculo);

public sealed class ReporteVentasException(int status, string code, IDictionary<string, string[]>? errors = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; } = errors;
}