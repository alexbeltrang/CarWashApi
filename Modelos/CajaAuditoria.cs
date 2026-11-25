using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public class CajaAuditoria
{
    [Key]
    public Guid Id { get; set; }
    public Guid IdOperacion { get; set; }
    public string Accion { get; set; } = "";
    public int AutorId { get; set; }
    public DateTimeOffset FechaUtc { get; set; }
    public string? Motivo { get; set; }
    public string IdsCajaAfectadasJson { get; set; } = "[]";
    public string IdentificadoresJson { get; set; } = "{}";
    public string AntesJson { get; set; } = "{}";
    public string DespuesJson { get; set; } = "{}";
}