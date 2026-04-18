namespace Carwash.API.DTOs
{
    public class IngresoVehiculoDTO
    {
        public int IdTurno { get; set; }
        public String NumeroTurno { get; set; }
        public String FechaHoraIngreso { get; set; }
        public String NombreCliente { get; set; }
        public String NumeroCelular { get; set; }
        public String Placa { get; set; }
        public String Marca { get; set; }
        public String TipoVehiculo { get; set; }
        public String Servicio { get; set; }
        public decimal Valor { get; set; }
        public String ValorPagado { get; set; }
    }
}
