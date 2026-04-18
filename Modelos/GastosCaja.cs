using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class GastosCaja
    {
        [Key]
        public int IdGasto { get; set; }
        public int idCaja { get; set; }
        public DateTime FechaRegistro { get; set; }
        public string? Concepto { get; set; }
        public decimal Valor { get; set; }
        public string Observaciones { get; set; }
    }
}
