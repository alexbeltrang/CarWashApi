using System.ComponentModel.DataAnnotations;

namespace Carwash.API.DTOs;

/// <summary>Datos compartidos de la empresa; revision cero crea y revision vigente modifica.</summary>
public sealed record EmpresaDto
{
    [Range(0, int.MaxValue)] public int Revision { get; init; }
    [Required, MaxLength(120)] public string NombreComercial { get; init; } = "";
    [Required, MaxLength(160)] public string RazonSocial { get; init; } = "";
    [Required, RegularExpression("^(Natural|Juridica)$")] public string TipoPersona { get; init; } = "";
    [Required, RegularExpression("^[0-9]{5,15}$")] public string Nit { get; init; } = "";
    [Required, RegularExpression("^[0-9]$")] public string DigitoVerificacion { get; init; } = "";
    [Required, MaxLength(200)] public string Direccion { get; init; } = "";
    [Required, MaxLength(100)] public string Municipio { get; init; } = "";
    [Required, MaxLength(100)] public string Departamento { get; init; } = "";
    [Required, MaxLength(80)] public string Pais { get; init; } = "";
    [Required, RegularExpression("^[0-9+() .-]{7,30}$")] public string Telefono { get; init; } = "";
    [Required, EmailAddress, MaxLength(254)] public string Correo { get; init; } = "";
    [MaxLength(500)] public string ResponsabilidadesTributarias { get; init; } = "";
    [MaxLength(1400000)] public string? LogoBase64 { get; init; }
}