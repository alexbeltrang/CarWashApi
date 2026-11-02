namespace Carwash.API.DTOs;

public sealed class TurnoEnProcesoDto
{
    public int IdTurno { get; set; }
    public string? NumeroTurno { get; set; }
    public string? NombreCliente { get; set; }
    public string? NumeroCelular { get; set; }
    public string? CorreoElectronico { get; set; }
    public string Placa { get; set; } = "";
    public string? Marca { get; set; }
    public string? Observaciones { get; set; }
    public int IdTipoVehiculo { get; set; }
    public decimal Valor { get; set; }
    public decimal ValorBaseComision { get; set; }
    public List<int> IdServicios { get; set; } = [];
}

public sealed class ActualizarTurnoEnProcesoRequest
{
    public string? NombreCliente { get; set; }
    public string? NumeroCelular { get; set; }
    public string? CorreoElectronico { get; set; }
    public string? Marca { get; set; }
    public string? Observaciones { get; set; }
    public int IdTipoVehiculo { get; set; }
    public decimal Valor { get; set; }
    public decimal ValorBaseComision { get; set; }
    public List<int> IdServicios { get; set; } = [];
}
