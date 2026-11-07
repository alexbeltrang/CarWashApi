namespace Carwash.API.DTOs;

/// <summary>Solicita una copia con identificador estable, sin elegir impresora ni texto.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record SolicitarImpresionRequest(Guid SolicitudId, int AnchoMm = 80);

/// <summary>Servicios confirmados por el cliente antes de imprimir un nuevo ingreso de 80 mm.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImprimirIngresoRequest(int[] IdServicios);

/// <summary>Estado del trabajo; SUBMITTED no confirma salida fisica de papel.</summary>
public sealed record ImpresionComprobanteDto(Guid SolicitudId, int IdTurno, int AnchoMm,
    string Impresora, string Estado, DateTimeOffset CreadoEn, DateTimeOffset ActualizadoEn, string? ErrorCodigo);