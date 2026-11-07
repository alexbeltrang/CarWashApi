using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public sealed class ImpresionComprobante
{
    [Key] public string SolicitudId { get; set; } = "";
    public int IdTurno { get; set; }
    public int AnchoMm { get; set; }
    public int SolicitadoPor { get; set; }
    public string Impresora { get; set; } = "";
    public string Estado { get; set; } = "PENDING";
    public string Texto { get; set; } = "";
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public string? ErrorCodigo { get; set; }
}