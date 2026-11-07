using System.ComponentModel.DataAnnotations;

namespace Carwash.API.DTOs;

public sealed record EmitirComprobanteRequest(int? IdTurno = null, string? Placa = null);
public sealed record ConfirmarRetiroRequest([MaxLength(64)] string? Codigo = null, bool RevisionConfirmada = false);
public sealed record ServicioComprobanteDto(string Nombre, decimal Precio);
public sealed record ComprobanteRetiroDto(
    int IdTurno, string Codigo, string Placa, string? Marca, string? Cliente,
    DateTimeOffset FechaIngreso, decimal Total, IReadOnlyList<ServicioComprobanteDto> Servicios,
    DateTimeOffset? EntregadoEn, int? EntregadoPor);