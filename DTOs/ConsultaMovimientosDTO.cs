namespace Carwash.API.DTOs
{
    public class ConsultaMovimientosDTO
    {
        public string Placa { get; set; }
        public String? FechaHoraIngreso { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorBaseComision { get; set; }
        public bool Pagado { get; set; }
        public String? Observaciones { get; set; }
        public string? FormaPago { get; set; }
        public string? TipoVehiculo { get; set; }
        public string? Operario { get; set; }
        public string? ClienteCredito { get; set; }
    }
}
