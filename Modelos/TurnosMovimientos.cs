using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class TurnosMovimientos
    {
        [Key]
        public int IdTurnoMovimientos { get; set; }
        public decimal MontoPagado { get; set; }
        public DateTime FechaMovimiento { get; set; }
        public int IdTurno { get; set; }
        public int IdFormaPago { get; set; }
    }
}
