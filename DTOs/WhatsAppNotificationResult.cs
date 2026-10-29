namespace Carwash.API.DTOs;

public sealed record WhatsAppNotificationResult(
    int IdTurno,
    string Estado,
    string Accion);
