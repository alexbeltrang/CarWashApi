using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos
{
    public class OperarioComisiones
    {
        [Key]
        public int IdOperarioComision { get; set; }
        public int idOperario { get; set; }
        public decimal Porcentaje { get; set; }
        public int DiaSemana { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
    }
}
