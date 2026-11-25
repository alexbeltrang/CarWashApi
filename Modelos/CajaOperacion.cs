using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public class CajaOperacion
{
    [Key]
    public Guid IdOperacion { get; set; }
    public string Tipo { get; set; } = "";
    public string Objetivo { get; set; } = "";
    public int AutorId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string IdsCajaAfectadasJson { get; set; } = "[]";
    public DateTimeOffset ConfirmadaEnUtc { get; set; }
    public int HttpStatus { get; set; }
    public string ResultadoJson { get; set; } = "{}";
}