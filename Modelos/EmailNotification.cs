using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public sealed class EmailNotification
{
    [Key]
    public int Id { get; set; }
    public int IdTurno { get; set; }
    public string Destinatario { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDING";
    public int Intentos { get; set; }
    public string? UltimoErrorCodigo { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public DateTime? ProximoIntentoEn { get; set; }
}
