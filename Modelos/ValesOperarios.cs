using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class ValesOperarios
    {
        [Key]
        public int IdValeOperario { get; set; }
        public int idOperario { get; set; }
        public int idCaja { get; set; }
        public DateTime? FechaRegsitro { get; set; }
        public decimal Valor { get; set; }
        public string? Motivo { get; set; }
        public bool? Descontado { get; set; }
        public DateTime? FechaDescuento { get; set; }
    }
}
