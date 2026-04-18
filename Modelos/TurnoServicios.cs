using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class TurnoServicios
    {
        [Key]
        public long idTunoServicios { get; set; }
        public int idServicios { get; set; }
        public int IdTurno { get; set; }
        public bool IsDeleted { get; set; }
    }
}
