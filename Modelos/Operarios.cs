using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class Operarios
    {
        [Key]
        public int idOperario { get; set; }
        public string? Nombres { get; set; }
        public string? Apellidos { get; set; }
        public string? Email { get; set; }
        public string? Celular { get; set; }
        public bool isDelete { get; set; } = false;
        public DateTime? UltimaAsignacion { get; set; }
        public int? CargaTrabajo { get; set; }
    }
}
