using System.Globalization;
using System.Security.Cryptography;
using Carwash.API.Context;
using Carwash.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Services;

public sealed class RetiroException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class RetiroVehiculosService(AppDbContext context, TimeProvider clock)
{
    public async Task<(ComprobanteRetiroDto Receipt, bool Created)> IssueAsync(
        EmitirComprobanteRequest request, CancellationToken cancellationToken)
    {
        var hasId = request.IdTurno.HasValue;
        var hasPlate = !string.IsNullOrWhiteSpace(request.Placa);
        if (hasId == hasPlate || hasId && request.IdTurno <= 0 || request.Placa?.Length > 20)
            throw new RetiroException(400, "INVALID_INPUT");
        var plate = request.Placa?.Trim().ToUpperInvariant();
        var query = context.Turnos.AsNoTracking();
        var turn = hasId
            ? await query.SingleOrDefaultAsync(item => item.IdTurno == request.IdTurno, cancellationToken)
            : await query.Where(item => item.Placa.ToUpper() == plate)
                .OrderByDescending(item => item.FechaHoraIngreso).ThenByDescending(item => item.IdTurno)
                .FirstOrDefaultAsync(cancellationToken);
        if (turn == null) throw new RetiroException(404, "TURN_NOT_FOUND");
        var services = await ServicesAsync(turn.IdTurno, turn.IdTipoVehiculo, cancellationToken);
        var expectedServices = await context.TurnoServicios.Where(item => item.IdTurno == turn.IdTurno && !item.IsDeleted)
            .Select(item => item.idServicios).Distinct().CountAsync(cancellationToken);
        if (services.Count == 0 || services.Count != expectedServices) throw new RetiroException(409, "SERVICES_REQUIRED");
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var created = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var changed = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ComprobantesRetiro (IdTurno, Codigo, CreadoEn)
            VALUES ({turn.IdTurno}, {code}, {created})
            ON CONFLICT(IdTurno) DO NOTHING
            """, cancellationToken);
        return (await GetAsync(turn.IdTurno, cancellationToken), changed == 1);
    }

    public async Task<ComprobanteRetiroDto> GetAsync(int idTurno, CancellationToken cancellationToken)
    {
        var receipt = await context.ComprobantesRetiro.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
        var turn = await context.Turnos.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
        if (receipt == null || turn == null) throw new RetiroException(404, "RECEIPT_NOT_FOUND");
        return new ComprobanteRetiroDto(turn.IdTurno, receipt.Codigo, turn.Placa,
            turn.Marca, turn.NombreCliente, new DateTimeOffset(turn.FechaHoraIngreso), turn.Valor,
            await ServicesAsync(turn.IdTurno, turn.IdTipoVehiculo, cancellationToken),
            receipt.EntregadoEn.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(receipt.EntregadoEn.Value, DateTimeKind.Utc)) : null,
            receipt.EntregadoPor);
    }

    public async Task<ComprobanteRetiroDto> ConfirmAsync(int idTurno, ConfirmarRetiroRequest request,
        int userId, CancellationToken cancellationToken)
    {
        if (!request.RevisionConfirmada) throw new RetiroException(400, "REVIEW_REQUIRED");
        var receipt = await context.ComprobantesRetiro.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
        if (receipt == null) throw new RetiroException(404, "RECEIPT_NOT_FOUND");
        var delivered = clock.GetUtcNow().UtcDateTime;
        var changed = await context.ComprobantesRetiro.Where(item => item.IdTurno == idTurno
                && item.EntregadoEn == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.EntregadoEn, delivered)
                .SetProperty(item => item.EntregadoPor, userId), cancellationToken);
        if (changed == 0) throw new RetiroException(409, "ALREADY_DELIVERED");
        return await GetAsync(idTurno, cancellationToken);
    }

    private async Task<List<ServicioComprobanteDto>> ServicesAsync(int idTurno, int vehicleType,
        CancellationToken cancellationToken) => await (
            from relation in context.TurnoServicios.AsNoTracking()
            join service in context.Servicios on relation.idServicios equals service.idServicio
            join price in context.PrecioServicioVehiculo on
                new { Service = relation.idServicios, Vehicle = vehicleType } equals
                new { Service = price.idServicio, Vehicle = price.IdTipoVehiculo }
            where relation.IdTurno == idTurno && !relation.IsDeleted
            select new { service.idServicio, service.Nombre, price.Precio })
            .Distinct().OrderBy(service => service.Nombre)
            .Select(service => new ServicioComprobanteDto(service.Nombre ?? "", service.Precio))
            .ToListAsync(cancellationToken);
}