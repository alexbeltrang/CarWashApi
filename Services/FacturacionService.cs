using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Services;

public sealed class FacturacionException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IFacturacionService
{
    Task<ConfiguracionFacturacionDto?> ConfigurationAsync(CancellationToken token);
    Task<ConfiguracionFacturacionDto> SaveConfigurationAsync(ConfiguracionFacturacionDto request, CancellationToken token);
    Task<PreparacionFacturaDto> PrepareAsync(int? idTurno, string? placa, CancellationToken token);
    Task<FacturaDto?> GetAsync(int idTurno, CancellationToken token);
    Task<FacturaDto> GetByIdAsync(Guid id, CancellationToken token);
    Task<List<FacturaDto>> ListAsync(string? placa, CancellationToken token);
    Task<(FacturaDto Invoice, bool Created)> IssueAsync(int idTurno, EmitirFacturaRequest request, int userId, CancellationToken token);
}

public sealed class FacturacionService(AppDbContext context, EmpresaService companies,
    ReporteVentasService reports, TimeProvider clock) : IFacturacionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");

    public async Task<ConfiguracionFacturacionDto?> ConfigurationAsync(CancellationToken token)
    {
        var row = await context.ConfiguracionFacturacion.AsNoTracking().SingleOrDefaultAsync(token);
        return row is null ? null : ReadConfiguration(row);
    }

    public async Task<ConfiguracionFacturacionDto> SaveConfigurationAsync(ConfiguracionFacturacionDto request, CancellationToken token)
    {
        var normalized = ValidateConfiguration(request);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var row = await context.ConfiguracionFacturacion.SingleOrDefaultAsync(token);
        if ((row?.Revision ?? 0) != request.Revision)
            throw new FacturacionException(409, "La configuracion cambio. Actualice antes de guardar.");
        var last = await context.Facturas.Where(invoice => invoice.Prefijo == normalized.Prefijo)
            .Select(invoice => (long?)invoice.Numero).MaxAsync(token);
        if (last.HasValue && normalized.SiguienteNumero <= last.Value)
            throw new FacturacionException(409, "El siguiente numero ya esta utilizado. No se permite retroceder.");
        row ??= new ConfiguracionFacturacion { Id = 1 };
        normalized = normalized with { Revision = checked(row.Revision + 1) };
        row.Revision = normalized.Revision;
        row.SiguienteNumero = normalized.SiguienteNumero;
        row.DatosJson = JsonSerializer.Serialize(normalized, Json);
        if (context.Entry(row).State == EntityState.Detached) context.ConfiguracionFacturacion.Add(row);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return normalized;
    }

    public async Task<PreparacionFacturaDto> PrepareAsync(int? idTurno, string? placa, CancellationToken token)
    {
        if (idTurno.HasValue == !string.IsNullOrWhiteSpace(placa) || idTurno <= 0 || placa?.Length > 20)
            throw new FacturacionException(400, "Seleccione un turno o una placa.");
        var plate = placa?.Trim().ToUpperInvariant();
        var query = context.Turnos.AsNoTracking();
        var turn = idTurno.HasValue
            ? await query.SingleOrDefaultAsync(item => item.IdTurno == idTurno, token)
            : await query.Where(item => item.Placa.ToUpper() == plate).OrderByDescending(item => item.FechaHoraIngreso)
                .ThenByDescending(item => item.IdTurno).FirstOrDefaultAsync(token);
        if (turn is null) throw new FacturacionException(404, "Turno no encontrado.");
        return await PreparationAsync(turn, token);
    }

    private async Task<PreparacionFacturaDto> PreparationAsync(Turnos turn, CancellationToken token)
    {
        if (!turn.Pagado || !turn.Estado || turn.Valor <= 0 || turn.Valor > 999999999999m
            || decimal.Round(turn.Valor, 2) != turn.Valor)
            throw new FacturacionException(409, "El turno debe estar finalizado y completamente pagado, con total valido.");
        var company = await companies.GetAsync(token)
            ?? throw new FacturacionException(409, "Configure primero los datos de la empresa.");
        var configuration = await ConfigurationAsync(token)
            ?? throw new FacturacionException(409, "Configure primero la facturacion en papel.");
        var today = BusinessDate();
        if (!configuration.Habilitada || today < Date(configuration.VigenteDesde) || today > Date(configuration.VigenteHasta))
            throw new FacturacionException(409, "La autorizacion de facturacion no esta habilitada o vigente.");
        if (configuration.SiguienteNumero > configuration.HastaNumero)
            throw new FacturacionException(409, "El rango autorizado esta agotado.");
        var names = await (from relation in context.TurnoServicios.AsNoTracking()
            join service in context.Servicios on relation.idServicios equals service.idServicio
            where relation.IdTurno == turn.IdTurno && !relation.IsDeleted
            select new { service.idServicio, service.Nombre }).Distinct().OrderBy(item => item.Nombre).ToListAsync(token);
        var expected = await context.TurnoServicios.Where(item => item.IdTurno == turn.IdTurno && !item.IsDeleted)
            .Select(item => item.idServicios).Distinct().CountAsync(token);
        if (names.Count == 0 || names.Count != expected || names.Any(item => string.IsNullOrWhiteSpace(item.Nombre)))
            throw new FacturacionException(409, "El turno no tiene servicios validos para facturar.");
        var concept = string.Join(", ", names.Select(item => item.Nombre));
        if (concept.Length > 4000 || concept.Any(char.IsControl))
            throw new FacturacionException(409, "Revise los nombres de los servicios antes de facturar.");
        return new(turn.IdTurno, turn.Placa, concept, turn.Valor, company, configuration,
            new CompradorFacturaDto { Nombre = turn.NombreCliente ?? "", Telefono = turn.NumeroCelular, Correo = turn.CorreoElectronico });
    }

    public async Task<FacturaDto?> GetAsync(int idTurno, CancellationToken token)
    {
        var row = await context.Facturas.AsNoTracking().SingleOrDefaultAsync(item => item.IdTurno == idTurno, token);
        return row is null ? null : Read(row);
    }

    public async Task<FacturaDto> GetByIdAsync(Guid id, CancellationToken token)
    {
        var row = await context.Facturas.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new FacturacionException(404, "Factura no encontrada.");
        return Read(row);
    }

    public async Task<List<FacturaDto>> ListAsync(string? placa, CancellationToken token)
    {
        if (placa?.Length > 20) throw new FacturacionException(400, "Revise la placa.");
        var rows = context.Facturas.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(placa))
        {
            var plate = placa.Trim().ToUpperInvariant();
            rows = rows.Where(item => item.Placa.ToUpper() == plate);
        }
        return (await rows.OrderByDescending(item => item.Fecha).ThenByDescending(item => item.Numero)
            .Take(100).ToListAsync(token)).Select(Read).ToList();
    }

    public async Task<(FacturaDto Invoice, bool Created)> IssueAsync(int idTurno, EmitirFacturaRequest request, int userId, CancellationToken token)
    {
        if (idTurno <= 0 || request.SolicitudId == Guid.Empty || userId <= 0)
            throw new FacturacionException(400, "Solicitud de factura invalida.");
        var buyer = ValidateBuyer(request.Comprador);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var existing = await context.Facturas.AsNoTracking().FirstOrDefaultAsync(item => item.IdTurno == idTurno || item.Id == request.SolicitudId, token);
        if (existing is not null)
        {
            if (existing.IdTurno != idTurno || existing.Id != request.SolicitudId)
                throw new FacturacionException(409, "Ya existe una factura. Consulte el documento original.");
            return (Read(existing), false);
        }
        var preparation = await PrepareAsync(idTurno, null, token);
        if (request.EmpresaRevision != preparation.Empresa.Revision || request.ConfiguracionRevision != preparation.Configuracion.Revision)
            throw new FacturacionException(409, "Empresa o autorizacion cambiaron. Revise nuevamente antes de emitir.");
        var settings = preparation.Configuracion;
        var rate = settings.TasaIva!.Value;
        var net = decimal.Round(preparation.Total / (1 + rate / 100), 2, MidpointRounding.AwayFromZero);
        var invoice = new FacturaDto(request.SolicitudId, idTurno, settings.Prefijo, settings.SiguienteNumero,
            BusinessDate().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), clock.GetUtcNow(), userId,
            preparation.Placa, preparation.Concepto, 1, preparation.Empresa, settings, buyer, net, rate,
            preparation.Total - net, preparation.Total, "");
        invoice = invoice with { Texto = Text(invoice) };
        context.Facturas.Add(new Factura { Id = invoice.Id, IdTurno = idTurno, Prefijo = invoice.Prefijo,
            Numero = invoice.Numero, Fecha = invoice.Fecha, Placa = invoice.Placa, DatosJson = JsonSerializer.Serialize(invoice, Json) });
        var row = await context.ConfiguracionFacturacion.SingleAsync(token);
        row.SiguienteNumero++;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return (invoice, true);
    }

    private DateOnly BusinessDate() => Date(reports.GetBusinessDate().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    private static DateOnly Date(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new FacturacionException(400, "Las fechas deben usar formato AAAA-MM-DD.");
        return date;
    }

    private static ConfiguracionFacturacionDto ValidateConfiguration(ConfiguracionFacturacionDto data)
    {
        if (data.Revision < 0 || data.Revision == int.MaxValue || data.TasaIva is null or < 0 or > 100
            || decimal.Round(data.TasaIva.Value, 4) != data.TasaIva.Value
            || data.DesdeNumero < 1 || data.HastaNumero > 999999999 || data.HastaNumero < data.DesdeNumero
            || data.SiguienteNumero < data.DesdeNumero || data.SiguienteNumero > data.HastaNumero + 1)
            throw new FacturacionException(400, "Revise tasa de IVA, rango y siguiente numero.");
        var prefix = Clean(data.Prefijo, 20, false).ToUpperInvariant();
        if (!Regex.IsMatch(prefix, "^[A-Z0-9-]*$", RegexOptions.CultureInvariant))
            throw new FacturacionException(400, "El prefijo solo permite letras, numeros y guion.");
        var resolution = Clean(data.NumeroResolucion, 80);
        if (Date(data.VigenteHasta) < Date(data.VigenteDesde) || Date(data.FechaResolucion) > Date(data.VigenteDesde))
            throw new FacturacionException(400, "La vigencia y fecha de autorizacion no son coherentes.");
        return data with { Prefijo = prefix, NumeroResolucion = resolution };
    }

    private static CompradorFacturaDto ValidateBuyer(CompradorFacturaDto? buyer)
    {
        if (buyer is null) throw new FacturacionException(400, "Identifique al comprador.");
        var type = Clean(buyer.TipoDocumento, 3).ToUpperInvariant();
        if (!new[] { "CC", "NIT", "CE", "TI", "PAS", "PEP", "PPT" }.Contains(type))
            throw new FacturacionException(400, "Seleccione un tipo de documento valido.");
        var number = Clean(buyer.NumeroDocumento, 30);
        if (!Regex.IsMatch(number, "^[A-Za-z0-9-]+$", RegexOptions.CultureInvariant))
            throw new FacturacionException(400, "Revise el documento del comprador.");
        var email = Clean(buyer.Correo, 254, false);
        if (email.Length > 0 && (!MailAddress.TryCreate(email, out var address) || address.Address != email))
            throw new FacturacionException(400, "Revise el correo del comprador.");
        return buyer with { Nombre = Clean(buyer.Nombre, 160), TipoDocumento = type, NumeroDocumento = number,
            Direccion = Clean(buyer.Direccion, 200, false), Telefono = Clean(buyer.Telefono, 30, false), Correo = email.Length == 0 ? null : email };
    }

    private static string Clean(string? text, int max, bool required = true)
    {
        var value = text?.Trim() ?? "";
        if ((required && value.Length == 0) || value.Length > max || (text?.Any(char.IsControl) ?? false))
            throw new FacturacionException(400, "Revise los campos obligatorios y su longitud.");
        return value;
    }

    private static ConfiguracionFacturacionDto ReadConfiguration(ConfiguracionFacturacion row) =>
        (JsonSerializer.Deserialize<ConfiguracionFacturacionDto>(row.DatosJson, Json) ?? throw new JsonException())
        with { Revision = row.Revision, SiguienteNumero = row.SiguienteNumero };
    private static FacturaDto Read(Factura row) => JsonSerializer.Deserialize<FacturaDto>(row.DatosJson, Json) ?? throw new JsonException();

    private static string Text(FacturaDto invoice)
    {
        var company = invoice.Empresa;
        var settings = invoice.Configuracion;
        return string.Join("\n", company.NombreComercial, company.RazonSocial, $"NIT: {company.Nit}-{company.DigitoVerificacion}",
            company.Direccion, $"{company.Municipio}, {company.Departamento}, {company.Pais}", $"Tel: {company.Telefono}", company.Correo,
            company.ResponsabilidadesTributarias, "FACTURA DE VENTA EN PAPEL", $"No. {invoice.Prefijo}{invoice.Numero}", $"Fecha: {invoice.Fecha}",
            $"Autorizacion: {settings.NumeroResolucion}", $"Fecha autorizacion: {settings.FechaResolucion}",
            $"Rango: {settings.Prefijo}{settings.DesdeNumero} a {settings.Prefijo}{settings.HastaNumero}",
            $"Vigencia: {settings.VigenteDesde} a {settings.VigenteHasta}", $"Comprador: {invoice.Comprador.Nombre}",
            $"{invoice.Comprador.TipoDocumento}: {invoice.Comprador.NumeroDocumento}", invoice.Comprador.Direccion,
            invoice.Comprador.Telefono, invoice.Comprador.Correo, $"Placa: {invoice.Placa}", $"Cantidad: 1\n{invoice.Concepto}",
            $"Base: $ {invoice.Base.ToString("N2", Culture)}", $"IVA {invoice.TasaIva.ToString("0.####", Culture)}%: $ {invoice.Iva.ToString("N2", Culture)}",
            $"TOTAL: $ {invoice.Total.ToString("N2", Culture)}", "Moneda: COP", "");
    }
}