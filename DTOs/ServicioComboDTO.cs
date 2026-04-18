namespace Carwash.API.DTOs
{
    public class ServicioComboDTO
    {
        public int idServicio { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal? precio { get; set; }
        public decimal? precioBaseComision { get; set; }
        public int idTipoVehiculo { get; set; }
    }
}
