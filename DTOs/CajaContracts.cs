using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carwash.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaAperturaRequest(Guid IdOperacion, bool Confirmar, string MontoInicial);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaCierreRequest(Guid IdOperacion, bool Confirmar, long RevisionEsperada,
    string ResumenToken, string EfectivoContado, string? JustificacionDiferencia);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaReaperturaRequest(Guid IdOperacion, bool Confirmar, long RevisionEsperada, int VersionEsperada, string Motivo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaCobroRequest(Guid IdOperacion, bool Confirmar, int IdCaja, long RevisionEsperada,
    string Importe, int IdFormaPago, bool Finalizar, int? IdClienteCredito, string? Observaciones);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaGastoRequest(Guid IdOperacion, bool Confirmar, long RevisionEsperada,
    string Importe, int IdFormaPagoSalida, string Concepto, string? Observaciones);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaValeRequest(Guid IdOperacion, bool Confirmar, long RevisionEsperada,
    string Importe, int IdFormaPagoSalida, int IdOperario, string Motivo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaOperarioRequest(Guid IdOperacion, bool Confirmar, int IdCaja, long RevisionEsperada, int? IdOperario, bool Liberar);
public sealed record CajaSaldoResponse(int IdTurno, string SaldoPendiente, bool Finalizado, bool Pagado, int IdCajaOrigen);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaCorreccionCobroRequest(Guid IdOperacion, bool Confirmar, long RevisionEsperada,
    string ImpactoToken, string Importe, int IdFormaPago, string Motivo, IReadOnlyDictionary<int, long>? RevisionesEsperadas = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaBorradoRequest(Guid IdOperacion, bool Confirmar,
    IReadOnlyDictionary<int, long> RevisionesEsperadas, string ImpactoToken, string Motivo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CajaCorreccionSalidaRequest(Guid IdOperacion, bool Confirmar, long RevisionEsperada,
    string Importe, int IdFormaPagoSalida, string Motivo);

public sealed record CajaErrorResponse(string Code, string Message, string TraceId, IReadOnlyDictionary<string, string[]>? Errors = null);
public sealed record CajaRevisionResponse(int IdCaja, long Revision, int VersionVigente = 0);
public sealed record CajaPeriodoResponse(DateTimeOffset? Desde, DateTimeOffset? Hasta, string Zona);
public sealed record CajaFuenteInvalida(string Fuente, int? Id, string Codigo);
public sealed record CajaMedioResponse(int IdFormaPago, string Nombre, string Categoria);
public sealed record CajaMovimientoResponse(int Id, int IdTurno, int? IdCaja, DateTimeOffset? Fecha,
    string? Importe, int IdFormaPago, string? Categoria, string? Naturaleza);
public sealed record CajaSalidaResponse(int Id, int IdCaja, DateTimeOffset? Fecha, string? Importe,
    int? IdFormaPagoSalida, string? Categoria, bool? DesembolsoConfirmado);
public sealed record CajaConciliacionTotalFinalResponse(string? TotalFinalDeclarado, string? EfectivoProyectado,
    string? DiferenciaDocumental, string? EfectivoContado, string ProcedenciaDeclarado,
    string ProcedenciaProyectado, string ProcedenciaContado);
public sealed record CajaFuentesResponse(int IdCaja, string Estado, long Revision, string ResumenToken,
    CajaPeriodoResponse Periodo, string? Apertura, IReadOnlyList<CajaMedioResponse> Medios,
    IReadOnlyList<CajaMovimientoResponse> Movimientos, IReadOnlyList<CajaSalidaResponse> Gastos,
    IReadOnlyList<CajaSalidaResponse> Vales, IReadOnlyList<CajaFuenteInvalida> FuentesInvalidas,
    string? CreditoPendiente = null, IReadOnlyList<string>? DiscrepanciasAcumulados = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CajaConciliacionTotalFinalResponse? ConciliacionTotalFinal { get; init; }
    [JsonIgnore]
    internal bool BloqueoCierrePorCreditoSinReceptora { get; init; }
}
public sealed record CajaImporteMedioResponse(int IdFormaPago, string Nombre, string Categoria, string Importe);
public sealed record CajaResumenResponse(int IdCaja, string Estado, long Revision, string ResumenToken,
    CajaPeriodoResponse Periodo, string Apertura, IReadOnlyList<CajaImporteMedioResponse> CobrosPorMedio,
    string CreditoPendiente, IReadOnlyList<CajaImporteMedioResponse> GastosPorMedio,
    IReadOnlyList<CajaImporteMedioResponse> ValesPorMedio, string EfectivoEsperado,
    IReadOnlyList<string> DiscrepanciasAcumulados, IReadOnlyList<CajaFuenteInvalida> FuentesInvalidas, bool PuedeCerrar)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CajaConciliacionTotalFinalResponse? ConciliacionTotalFinal { get; init; }
}
public sealed record CajaResultadoOperacion(Guid IdOperacion, int HttpStatus, JsonElement Resultado,
    IReadOnlyDictionary<int, long> Revisiones, DateTimeOffset ConfirmadaEnUtc);
public sealed record CajaImpactoResponse(string ImpactoToken, IReadOnlyList<CajaRevisionResponse> Cajas,
    IReadOnlyDictionary<string, int[]> Identificadores, IReadOnlyList<CajaFuenteInvalida> Bloqueos,
    IReadOnlyList<CajaMovimientoResponse>? Movimientos = null, IReadOnlyList<string>? Trabajos = null);
public sealed record CajaCierreResponse(Guid Id, int IdCaja, int Version, string Origen, bool Vigente,
    string? Responsable, DateTimeOffset? ConfirmadoEnUtc, JsonElement Snapshot,
    string? EfectivoContado, string? Diferencia, string? JustificacionDiferencia,
    long Revision = 0, bool EnRevision = false, string? MotivoRevision = null);
public sealed record CajaPaginaResponse<T>(IReadOnlyList<T> Items, int Pagina, int TamanoPagina, long TotalCount,
    IReadOnlyList<CajaFuenteInvalida>? FuentesInvalidas = null);
public sealed record CajaSnapshotResponse(int SnapshotVersion, CajaResumenResponse Resumen,
    CajaFuentesResponse Detalle, int ConfirmadoPorId, string Responsable, DateTimeOffset ConfirmadoEnUtc,
    string EfectivoContado, string Diferencia, string? JustificacionDiferencia, string? MotivoRevision = null);

public static class CajaValidacion
{
    public static decimal Dinero(string? value, bool permiteCero = false)
    {
        if (value == null || value.Length is < 1 or > 32
            || !System.Text.RegularExpressions.Regex.IsMatch(value, @"^(0|[1-9][0-9]*)(\.[0-9]{1,2})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount.ToString("0.############################", CultureInfo.InvariantCulture)
                != (value.Contains('.') ? value.TrimEnd('0').TrimEnd('.') : value)
            || (permiteCero ? amount < 0 : amount <= 0))
            throw new CajaException(400, "INVALID_COMMAND", new Dictionary<string, string[]> { ["importe"] = ["Importe invariante con hasta dos decimales requerido."] });
        return amount;
    }

    public static string Motivo(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed == null || trimmed.Length is < 1 or > 500)
            throw new CajaException(400, "INVALID_COMMAND", new Dictionary<string, string[]> { ["motivo"] = ["Motivo de 1 a 500 caracteres requerido."] });
        return trimmed;
    }
}

public sealed class CajaException(int status, string code, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}