using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Carwash.API.Configuration;
using Carwash.API.Context;
using Carwash.API.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Carwash.API.Services;

public interface IImpresionComprobantesService
{
    Task<ImpresionComprobanteDto> RequestAsync(int idTurno, SolicitarImpresionRequest request, int userId, CancellationToken cancellationToken);
    Task<ImpresionComprobanteDto> GetAsync(Guid solicitudId, CancellationToken cancellationToken);
    Task<ImpresionComprobanteDto?> LatestAsync(int idTurno, CancellationToken cancellationToken);
    Task<ImpresionComprobanteDto> EntryAsync(int idTurno, ImprimirIngresoRequest request, int userId, CancellationToken cancellationToken);
    Task<ImpresionComprobanteDto> InvoiceAsync(FacturaDto invoice, SolicitarImpresionRequest request, int userId, CancellationToken cancellationToken);
    Task<ImpresionComprobanteDto?> LatestInvoiceAsync(FacturaDto invoice, CancellationToken cancellationToken);
}

public sealed class ImpresionComprobantesService(AppDbContext context, RetiroVehiculosService receipts,
    IOptions<PrintingOptions> options, IReceiptPrinter printer, TimeProvider clock) : IImpresionComprobantesService
{
    public async Task<ImpresionComprobanteDto> EntryAsync(int idTurno, ImprimirIngresoRequest request, int userId, CancellationToken cancellationToken)
    {
        if (idTurno <= 0 || request.IdServicios is not { Length: > 0 } || request.IdServicios.Any(id => id <= 0))
            throw new RetiroException(400, "INVALID_PRINT_INPUT");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("CARWASH:ENTRY:1:" + idTurno.ToString(CultureInfo.InvariantCulture)));
        var id = new Guid(hash.AsSpan(0, 16));
        var key = id.ToString("D");
        var existing = await context.ImpresionesComprobante.AsNoTracking().SingleOrDefaultAsync(job => job.SolicitudId == key, cancellationToken);
        if (existing is not null)
        {
            if (ReceiptPrintLayout.IsInvoice(existing.Texto)) throw new RetiroException(409, "PRINT_REQUEST_CONFLICT");
            return Map(existing);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var services = await context.TurnoServicios.Where(item => item.IdTurno == idTurno && !item.IsDeleted)
            .Select(item => item.idServicios).Distinct().ToListAsync(cancellationToken);
        if (!services.ToHashSet().SetEquals(request.IdServicios)) throw new RetiroException(409, "SERVICES_NOT_CONFIRMED");
        var result = await RequestAsync(idTurno, new(id, 80), userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ImpresionComprobanteDto> RequestAsync(int idTurno, SolicitarImpresionRequest request, int userId, CancellationToken cancellationToken)
    {
        if (idTurno <= 0 || request.SolicitudId == Guid.Empty || request.AnchoMm is not (58 or 80)) throw new RetiroException(400, "INVALID_PRINT_INPUT");
        var key = request.SolicitudId.ToString("D");
        var existing = await context.ImpresionesComprobante.AsNoTracking().SingleOrDefaultAsync(job => job.SolicitudId == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.IdTurno != idTurno || existing.AnchoMm != request.AnchoMm || ReceiptPrintLayout.IsInvoice(existing.Texto)) throw new RetiroException(409, "PRINT_REQUEST_CONFLICT");
            return Map(existing);
        }
        if (request.AnchoMm != 80) throw new RetiroException(400, "INVALID_PRINT_INPUT");
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.PrinterName)) throw new RetiroException(503, "PRINT_NOT_CONFIGURED");
        var receipt = (await receipts.IssueAsync(new EmitirComprobanteRequest(IdTurno: idTurno), cancellationToken)).Receipt;
        var text = ReceiptPrintLayout.Snapshot(receipt, await new EmpresaService(context).GetAsync(cancellationToken));
        return await EnqueueAsync(idTurno, request, userId, text, null, cancellationToken);
    }

    public async Task<ImpresionComprobanteDto> InvoiceAsync(FacturaDto invoice, SolicitarImpresionRequest request, int userId, CancellationToken cancellationToken)
    {
        if (request.SolicitudId == Guid.Empty || request.AnchoMm != 80) throw new RetiroException(400, "INVALID_PRINT_INPUT");
        var key = request.SolicitudId.ToString("D");
        var existing = await context.ImpresionesComprobante.AsNoTracking().SingleOrDefaultAsync(job => job.SolicitudId == key, cancellationToken);
        if (existing is not null)
        {
            if (!ReceiptPrintLayout.IsInvoice(existing.Texto) || ReceiptPrintLayout.ReadSnapshot(existing.Texto).DocumentId != invoice.Id.ToString("D"))
                throw new RetiroException(409, "PRINT_REQUEST_CONFLICT");
            return Map(existing);
        }
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.PrinterName)) throw new RetiroException(503, "PRINT_NOT_CONFIGURED");
        return await EnqueueAsync(invoice.IdTurno, request, userId, ReceiptPrintLayout.Snapshot(invoice), invoice.Id.ToString("D"), cancellationToken);
    }

    private async Task<ImpresionComprobanteDto> EnqueueAsync(int idTurno, SolicitarImpresionRequest request, int userId,
        string text, string? documentId, CancellationToken cancellationToken)
    {
        var key = request.SolicitudId.ToString("D");
        var now = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var printerName = options.Value.PrinterName;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ImpresionesComprobante (SolicitudId, IdTurno, AnchoMm, SolicitadoPor, Impresora, Estado, Texto, CreadoEn, ActualizadoEn)
            VALUES ({key}, {idTurno}, {request.AnchoMm}, {userId}, {printerName}, 'PENDING', {text}, {now}, {now})
            ON CONFLICT(SolicitudId) DO NOTHING
            """, cancellationToken);
        var result = await context.ImpresionesComprobante.AsNoTracking().SingleAsync(job => job.SolicitudId == key, cancellationToken);
        if (result.IdTurno != idTurno || result.AnchoMm != request.AnchoMm || ReceiptPrintLayout.ReadSnapshot(result.Texto).DocumentId != documentId)
            throw new RetiroException(409, "PRINT_REQUEST_CONFLICT");
        return Map(result);
    }

    public async Task<ImpresionComprobanteDto> GetAsync(Guid solicitudId, CancellationToken cancellationToken)
    {
        var key = solicitudId.ToString("D");
        var job = await context.ImpresionesComprobante.AsNoTracking().SingleOrDefaultAsync(item => item.SolicitudId == key, cancellationToken)
            ?? throw new RetiroException(404, "PRINT_NOT_FOUND");
        return Map(job);
    }

    public async Task<ImpresionComprobanteDto?> LatestAsync(int idTurno, CancellationToken cancellationToken)
    {
        var job = await context.ImpresionesComprobante.FromSqlInterpolated($"""
            SELECT * FROM ImpresionesComprobante WHERE IdTurno = {idTurno} AND Texto NOT LIKE 'CARWASH-INVOICE-1%'
            ORDER BY CreadoEn DESC, rowid DESC LIMIT 1
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return job is null ? null : Map(job);
    }

    public async Task<ImpresionComprobanteDto?> LatestInvoiceAsync(FacturaDto invoice, CancellationToken cancellationToken)
    {
        var job = await context.ImpresionesComprobante.FromSqlInterpolated($"""
            SELECT * FROM ImpresionesComprobante WHERE IdTurno = {invoice.IdTurno} AND Texto LIKE 'CARWASH-INVOICE-1%'
            ORDER BY CreadoEn DESC, rowid DESC LIMIT 1
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (job is null) return null;
        if (ReceiptPrintLayout.ReadSnapshot(job.Texto).DocumentId != invoice.Id.ToString("D")) throw new IOException("PRINT_SNAPSHOT_INVALID");
        return Map(job);
    }

    public Task RecoverAsync(CancellationToken cancellationToken) => context.ImpresionesComprobante
        .Where(job => job.Estado == "SUBMITTING").ExecuteUpdateAsync(setters => setters
            .SetProperty(job => job.Estado, "UNKNOWN").SetProperty(job => job.ErrorCodigo, "RESTART_DURING_SUBMISSION")
            .SetProperty(job => job.ActualizadoEn, clock.GetUtcNow().UtcDateTime), cancellationToken);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return false;
        var key = await context.ImpresionesComprobante.AsNoTracking().Where(job => job.Estado == "PENDING")
            .OrderBy(job => job.CreadoEn).ThenBy(job => job.SolicitudId).Select(job => job.SolicitudId).FirstOrDefaultAsync(cancellationToken);
        if (key is null) return false;
        var claimed = await context.ImpresionesComprobante.Where(job => job.SolicitudId == key && job.Estado == "PENDING")
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Estado, "SUBMITTING")
                .SetProperty(job => job.ActualizadoEn, clock.GetUtcNow().UtcDateTime), cancellationToken);
        if (claimed == 0) return false;
        var job = await context.ImpresionesComprobante.AsNoTracking().SingleAsync(item => item.SolicitudId == key, cancellationToken);
        var state = "SUBMITTED";
        string? error = null;
        try { printer.Submit(job.Impresora, job.Texto, job.AnchoMm, "CarWash-" + key); }
        catch (PrinterUnavailableException) { state = "FAILED"; error = "PRINTER_UNAVAILABLE"; }
        catch (Exception) { state = "UNKNOWN"; error = "PRINT_RESULT_UNKNOWN"; }
        await context.ImpresionesComprobante.Where(item => item.SolicitudId == key && item.Estado == "SUBMITTING")
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Estado, state).SetProperty(item => item.ErrorCodigo, error)
                .SetProperty(item => item.ActualizadoEn, clock.GetUtcNow().UtcDateTime), CancellationToken.None);
        return true;
    }

    private static ImpresionComprobanteDto Map(Modelos.ImpresionComprobante job) => new(Guid.Parse(job.SolicitudId), job.IdTurno,
        job.AnchoMm, job.Impresora, job.Estado, new DateTimeOffset(DateTime.SpecifyKind(job.CreadoEn, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(job.ActualizadoEn, DateTimeKind.Utc)), job.ErrorCodigo);
}