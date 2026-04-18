using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class Servicios
    {
        [Key]
        public int idServicio { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool IsDelete { get; set; } = false;
    }
}
