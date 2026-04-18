using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class AsistenciaOperario
    {
        [Key]
        public int idAsistenciaOperario { get; set; }
        public int idOperario { get; set; }
        public DateTime Fecha { get; set; }
        public bool Asistio { get; set; }
        public bool Autorizado { get; set; }
        public string Observacion { get; set; }
    }

}
