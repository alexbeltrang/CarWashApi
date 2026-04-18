using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class FormaPago
    {
        [Key]
        public int IdFormaPago { get; set; }
        public string? Nombre { get; set; }
    }
}
