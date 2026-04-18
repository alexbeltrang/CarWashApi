using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class ClienteCredito
    {
        [Key]
        public int idClienteCredito { get; set; }
        public string? Nombre { get; set; }
        public string? Telefono { get; set; }
        public string? Direcion { get; set; }
        public string? Correo { get; set; }
        public string? ciudad { get; set; }
        public bool Estado { get; set; }
    }
}
