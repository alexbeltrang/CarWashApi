using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public sealed class ConfiguracionFacturacion
{
    [Key] public int Id { get; set; }
    public string DatosJson { get; set; } = "";
    public int Revision { get; set; }
    public long SiguienteNumero { get; set; }
}

public sealed class Factura
{
    [Key] public Guid Id { get; set; }
    public int IdTurno { get; set; }
    public string Prefijo { get; set; } = "";
    public long Numero { get; set; }
    public string Fecha { get; set; } = "";
    public string Placa { get; set; } = "";
    public string DatosJson { get; set; } = "";
}