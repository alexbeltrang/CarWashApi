namespace Carwash.API.DTOs
{
    public class GestionVehiculosDTO
    {
        public int IdTurno { get; set; }
        public String Placa { get; set; }
        public string Marca { get; set; }
        public string NumeroOrden { get; set; }
        public String NombreCliente { get; set; }
        public String NumeroCelular { get; set; }
        public String NumeroTurno { get; set; }
        public String FechaHoraIngreso { get; set; }
        public String TipoVehiculo { get; set; }
        public String OperadorAsignado { get; set; }
        public bool Estado { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorBaseComision { get; set; }
        public decimal PorcentajeComision { get; set; }
        public String ValorCliente { get; set; }
        public int? idOperario { get; set; }
        public String Observaciones { get; set; }
        public bool OperadorOcupado { get; set; }

    }
}
