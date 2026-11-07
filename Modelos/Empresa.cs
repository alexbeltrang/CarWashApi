using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public sealed class Empresa
{
    [Key] public int Id { get; set; } = 1;
    public string DatosJson { get; set; } = "";
    public int Revision { get; set; }
}