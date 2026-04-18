using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Carwash.API.Modelos
{
    public class Turnos
    {
        [Key]
        public int IdTurno { get; set; }
        public string? NumeroTurno { get; set; }
        public string? NombreCliente { get; set; }
        public string? NumeroCelular { get; set; }
        public string Placa { get; set; }
        public DateTime FechaHoraIngreso { get; set; }
        public DateTime? FechaHoraFin { get; set; }
        public string? Marca { get; set; }
        public string? NumeroOrden { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorBaseComision { get; set; }
        public decimal PorcentajeComision { get; set; }
        public decimal ValorComision { get; set; }
        public bool Pagado { get; set; }
        public bool PagadoNomina { get; set; }
        public string? Observaciones { get; set; }
        public DateTime? FechaHoraAsignacionOperario { get; set; }
        public bool Estado { get; set; }
        public int IdTipoVehiculo { get; set; }
        public int? idOperario { get; set; }
        public bool? OperadorOcupado { get; set; }
        public int? idCajaDiaria { get; set; }
        public int? idClienteCredito { get; set; }
    }
}
