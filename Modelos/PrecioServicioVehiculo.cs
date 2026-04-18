using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    using System.ComponentModel.DataAnnotations;
    public class PrecioServicioVehiculo
    {
        [Key]
        public int IdServicioVehiculo { get; set; }
        public int idServicio { get; set; }
        public int IdTipoVehiculo { get; set; }
        public decimal Precio { get; set; }
        public decimal PrecioBaseComision { get; set; }
    }
}
