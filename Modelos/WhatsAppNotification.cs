using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public class WhatsAppNotification
{
    [Key]
    public int Id { get; set; }
    public int IdTurno { get; set; }
    public string DestinatarioNormalizado { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDING";
    public int Intentos { get; set; }
    public string? MessageIdExterno { get; set; }
    public string? FbTraceId { get; set; }
    public string? UltimoErrorCodigo { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public DateTime? ProximoIntentoEn { get; set; }
}
