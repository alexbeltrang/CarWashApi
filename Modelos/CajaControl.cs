using System.ComponentModel.DataAnnotations;

namespace Carwash.API.Modelos;

public class CajaControl
{
    [Key]
    public int IdCaja { get; set; }
    public long Revision { get; set; }
    public bool EnRevision { get; set; }
    public int? RevisionAbiertaPor { get; set; }
    public DateTimeOffset? RevisionAbiertaEnUtc { get; set; }
    public string? MotivoRevision { get; set; }
    public Guid? IdCierreVigente { get; set; }
}