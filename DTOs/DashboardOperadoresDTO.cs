namespace Carwash.API.DTOs
{
    public class DashboardOperadoresDTO
    {
        public string NombreOperador { get; set; }
        public string Placa { get; set; }
        public DateTime FechaInicio { get; set; }
        public int MinutosTranscurridos { get; set; }
        public int Horas { get; set; }
        public int MinutosRestantes { get; set; }
    }
}
