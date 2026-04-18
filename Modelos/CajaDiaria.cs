namespace Carwash.API.Modelos
{
    using System.ComponentModel.DataAnnotations;

    public class CajaDiaria
    {
        [Key]
        public int IdCaja { get; set; }

        public DateTime FechaApertura { get; set; }
        public DateTime? FechaCierre { get; set; }

        public decimal MontoInicial { get; set; }
        public decimal TotalIngresosEfectivo { get; set; }
        public decimal TotalIngresosTransferencias { get; set; }
        public decimal TotalIngresosDatafono { get; set; }
        public decimal TotalIngresosCredito { get; set; }

        public decimal TotalEgresos { get; set; }
        public decimal TotalFinal { get; set; }

        public bool Estado { get; set; } = false;

        public decimal TotalValesOperarios { get; set; }
    }
}
