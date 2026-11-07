using System.Globalization;
using System.Text.Json;
using Carwash.API.DTOs;

namespace Carwash.API.Services;

public static class ReceiptPrintLayout
{
    private const string SnapshotPrefix = "CARWASH-RECEIPT-1\n";
    public const string InvoicePrefix = "CARWASH-INVOICE-1\n";

    public static string Text(ComprobanteRetiroDto receipt, EmpresaDto? company = null)
    {
        var culture = CultureInfo.GetCultureInfo("es-CO");
        var lines = new List<string>
        {
            "RECIBO DE INGRESO", "",
            $"Placa: {receipt.Placa}", $"Marca: {receipt.Marca}",
            $"Cliente: {receipt.Cliente}",
            $"Ingreso: {receipt.FechaIngreso.ToLocalTime():dd/MM/yyyy HH:mm}",
            ""
        };
        lines.InsertRange(0, CompanyLines(company));
        foreach (var service in receipt.Servicios)
        {
            lines.Add(service.Nombre);
            lines.Add(service.Precio.ToString("C", culture));
        }
        lines.Add("");
        lines.Add($"TOTAL: {receipt.Total.ToString("C", culture)}");
        lines.Add("Gracias por su visita.");
        if (receipt.EntregadoEn is not null)
            lines.Add($"Entregado: {receipt.EntregadoEn.Value.ToLocalTime():dd/MM/yyyy HH:mm}");
        lines.Add("Software: CarWash");
        return string.Join(Environment.NewLine, lines);
    }

    public static IReadOnlyList<string> CompanyLines(EmpresaDto? company) => company is null ? [] :
        new[] { company.NombreComercial, company.RazonSocial,
            $"NIT: {company.Nit}-{company.DigitoVerificacion}", company.Direccion,
            string.Join(" / ", company.Municipio, company.Departamento, company.Pais),
            $"Telefono: {company.Telefono}", company.Correo, company.ResponsabilidadesTributarias, "" };

    public static string Snapshot(ComprobanteRetiroDto receipt, EmpresaDto? company) => company is null
        ? Text(receipt) : SnapshotPrefix + JsonSerializer.Serialize(new ReceiptPrintSnapshot(1, Text(receipt, company), company.LogoBase64));

    public static string Snapshot(FacturaDto invoice) => InvoicePrefix + JsonSerializer.Serialize(
        new ReceiptPrintSnapshot(1, invoice.Texto, invoice.Empresa.LogoBase64, invoice.Id.ToString("D")));
    public static bool IsInvoice(string payload) => payload.StartsWith(InvoicePrefix, StringComparison.Ordinal);

    public static ReceiptPrintSnapshot ReadSnapshot(string payload)
    {
        var prefix = IsInvoice(payload) ? InvoicePrefix : SnapshotPrefix;
        if (!payload.StartsWith(prefix, StringComparison.Ordinal)) return new(1, payload, null);
        var result = JsonSerializer.Deserialize<ReceiptPrintSnapshot>(payload[prefix.Length..]);
        return result is { Version: 1, Texto.Length: > 0 } ? result : throw new IOException("PRINT_SNAPSHOT_INVALID");
    }
}

public sealed record ReceiptPrintSnapshot(int Version, string Texto, string? LogoBase64, string? DocumentId = null);