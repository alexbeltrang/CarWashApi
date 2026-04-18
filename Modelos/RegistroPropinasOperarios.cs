using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class RegistroPropinasOperarios
    {
        [Key]
        public int idPropina { get; set; }
        public int idOperario { get; set; }
        public DateTime fechaRegistro { get; set; }
        public decimal valorPropina { get; set; }
        public string observaciones { get; set; } = string.Empty;
        public int idCaja { get; set; }
        public bool isDelete { get; set; }
    }
}
