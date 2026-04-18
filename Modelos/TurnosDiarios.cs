using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos
{
    public class TurnosDiarios
    {
        [Key]
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int Consecutivo { get; set; }
    }
}
