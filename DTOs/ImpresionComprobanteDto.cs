namespace Carwash.API.DTOs;

/// <summary>Solicita una copia con identificador estable, sin elegir impresora ni texto.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record SolicitarImpresionRequest(Guid SolicitudId, int AnchoMm = 60);

/// <summary>Servicios confirmados antes de imprimir un ingreso con el ancho configurado en el servidor.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImprimirIngresoRequest(int[] IdServicios);

/// <summary>Estado y ancho solicitado; el ancho fisico se limita a Printing:PaperWidthMm. SUBMITTED no confirma salida fisica.</summary>
public sealed record ImpresionComprobanteDto(Guid SolicitudId, int IdTurno, int AnchoMm,
    string Impresora, string Estado, DateTimeOffset CreadoEn, DateTimeOffset ActualizadoEn, string? ErrorCodigo);