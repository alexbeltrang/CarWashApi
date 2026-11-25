using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public class CajaCierre
{
    [Key]
    public Guid Id { get; set; }
    public int IdCaja { get; set; }
    public int Version { get; set; }
    public Guid? IdCierreAnterior { get; set; }
    public string Origen { get; set; } = "VERIFICADO";
    public int? ConfirmadoPorId { get; set; }
    public string? Responsable { get; set; }
    public DateTimeOffset? ConfirmadoEnUtc { get; set; }
    public long? RevisionFuentes { get; set; }
    public string? ResumenToken { get; set; }
    public DateTime? PeriodoInicio { get; set; }
    public DateTime? PeriodoFin { get; set; }
    public string? Zona { get; set; }
    public int SnapshotVersion { get; set; } = 1;
    public string SnapshotJson { get; set; } = "{}";
    public decimal? EfectivoContado { get; set; }
    public decimal? Diferencia { get; set; }
    public string? JustificacionDiferencia { get; set; }
    public string? MotivoRevision { get; set; }
}