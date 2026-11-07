using System.ComponentModel.DataAnnotations;

namespace Carwash.API.DTOs;

public sealed record ConfiguracionFacturacionDto
{
    public int Revision { get; init; }
    public bool Habilitada { get; init; }
    [MaxLength(20)] public string Prefijo { get; init; } = "";
    [Required, MaxLength(80)] public string NumeroResolucion { get; init; } = "";
    [Required, MaxLength(10)] public string FechaResolucion { get; init; } = "";
    [Required, MaxLength(10)] public string VigenteDesde { get; init; } = "";
    [Required, MaxLength(10)] public string VigenteHasta { get; init; } = "";
    public long DesdeNumero { get; init; }
    public long HastaNumero { get; init; }
    public long SiguienteNumero { get; init; }
    [Required] public decimal? TasaIva { get; init; }
}

public sealed record CompradorFacturaDto
{
    [Required, MaxLength(160)] public string Nombre { get; init; } = "";
    [Required, MaxLength(3)] public string TipoDocumento { get; init; } = "";
    [Required, MaxLength(30)] public string NumeroDocumento { get; init; } = "";
    [MaxLength(200)] public string? Direccion { get; init; }
    [MaxLength(30)] public string? Telefono { get; init; }
    [EmailAddress, MaxLength(254)] public string? Correo { get; init; }
}

public sealed record EmitirFacturaRequest(Guid SolicitudId, int EmpresaRevision,
    int ConfiguracionRevision, [Required] CompradorFacturaDto Comprador);

public sealed record PreparacionFacturaDto(int IdTurno, string Placa, string Concepto,
    decimal Total, EmpresaDto Empresa, ConfiguracionFacturacionDto Configuracion,
    CompradorFacturaDto Comprador);

public sealed record FacturaDto(Guid Id, int IdTurno, string Prefijo, long Numero,
    string Fecha, DateTimeOffset EmitidaEn, int EmitidaPor, string Placa, string Concepto,
    int Cantidad, EmpresaDto Empresa, ConfiguracionFacturacionDto Configuracion,
    CompradorFacturaDto Comprador, decimal Base, decimal TasaIva, decimal Iva,
    decimal Total, string Texto);