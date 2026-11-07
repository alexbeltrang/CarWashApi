using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public sealed class ComprobanteRetiro
{
    [Key]
    public int IdTurno { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public DateTime CreadoEn { get; set; }
    public DateTime? EntregadoEn { get; set; }
    public int? EntregadoPor { get; set; }
}