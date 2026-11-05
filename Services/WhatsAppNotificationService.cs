using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Carwash.API.Configuration;
using Microsoft.Data.Sqlite;

namespace Carwash.API.Services;

public sealed class WhatsAppNotificationConflictException : Exception;

public sealed class WhatsAppNotificationService(
    AppDbContext context,
    WhatsAppTemplateBuilder templateBuilder,
    IWhatsAppClient client,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppNotificationService> logger)
{
    public async Task<WhatsAppNotificationResult?> NotifyAsync(
        int idTurno,
        CancellationToken cancellationToken)
    {
        var existing = await context.WhatsAppNotifications
            .SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
        if (existing is not null)
            return ToResult(existing);

        var turn = await context.Turnos.SingleOrDefaultAsync(
            item => item.IdTurno == idTurno,
            cancellationToken);
        if (turn is null)
            return null;

        var services = await (
            from relation in context.TurnoServicios
            join service in context.Servicios on relation.idServicios equals service.idServicio
            join price in context.PrecioServicioVehiculo
                on new { relation.idServicios, turn.IdTipoVehiculo }
                equals new { idServicios = price.idServicio, price.IdTipoVehiculo }
            where relation.IdTurno == idTurno && !relation.IsDeleted
            select new { service.Nombre, price.Precio }
        ).ToListAsync(cancellationToken);
        if (services.Count == 0)
            throw new WhatsAppNotificationConflictException();
        if (!options.Value.IsConfigured)
            return new(idTurno, "NOT_CONFIGURED", "NONE");

        var notification = new WhatsAppNotification
        {
            IdTurno = idTurno,
            Estado = "PENDING",
            Intentos = 0,
            CreadoEn = DateTime.UtcNow,
            ActualizadoEn = DateTime.UtcNow,
        };
        context.WhatsAppNotifications.Add(notification);

        if (!templateBuilder.TryBuild(
                turn,
                services.Select(item => (item.Nombre, item.Precio)).ToList(),
                out var recipient,
                out var templateName,
                out var language,
                out var parameters,
                out var reason))
        {
            notification.Estado = "NOT_APPLICABLE";
            notification.UltimoErrorCodigo = reason;
            notification.ActualizadoEn = DateTime.UtcNow;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            context.Entry(notification).State = EntityState.Detached;
            var concurrent = await context.WhatsAppNotifications.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
            if (concurrent is null) throw;
            return ToResult(concurrent);
        }
        if (notification.Estado == "NOT_APPLICABLE")
            return ToResult(notification);
        return await SendAsync(notification, recipient, templateName, language, parameters, cancellationToken);
    }

    public async Task<WhatsAppNotificationResult?> RetryAsync(
        int idTurno,
        CancellationToken cancellationToken)
    {
        var notification = await context.WhatsAppNotifications
            .SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
        if (notification is null)
            return await NotifyAsync(idTurno, cancellationToken);
        if (notification.MessageIdExterno is not null ||
            notification.Estado is "ACCEPTED" or "DELIVERED" or "NOT_APPLICABLE" or "UNKNOWN" or "SENDING")
            return ToResult(notification);
        if (!options.Value.IsConfigured)
            return new(idTurno, "NOT_CONFIGURED", "NONE");

        var turn = await context.Turnos.SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
        if (turn is null)
            return null;
        var services = await (
            from relation in context.TurnoServicios
            join service in context.Servicios on relation.idServicios equals service.idServicio
            join price in context.PrecioServicioVehiculo
                on new { relation.idServicios, turn.IdTipoVehiculo }
                equals new { idServicios = price.idServicio, price.IdTipoVehiculo }
            where relation.IdTurno == idTurno && !relation.IsDeleted
            select new { service.Nombre, price.Precio }
        ).ToListAsync(cancellationToken);
        if (!templateBuilder.TryBuild(
                turn, services.Select(item => (item.Nombre, item.Precio)).ToList(),
                out var recipient, out var templateName, out var language,
                out var parameters, out var reason))
        {
            notification.Estado = "NOT_APPLICABLE";
            notification.UltimoErrorCodigo = reason;
            notification.ActualizadoEn = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return ToResult(notification);
        }

        return await SendAsync(notification, recipient, templateName, language, parameters, cancellationToken);
    }

    public async Task<WhatsAppNotificationResult?> GetAsync(
        int idTurno,
        CancellationToken cancellationToken)
    {
        var notification = await context.WhatsAppNotifications
            .SingleOrDefaultAsync(item => item.IdTurno == idTurno, cancellationToken);
        return notification is null ? null : ToResult(notification);
    }

    private async Task<WhatsAppNotificationResult> SendAsync(
        WhatsAppNotification notification,
        string recipient,
        string templateName,
        string language,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken)
    {
        var maximumAttempts = Math.Max(1, options.Value.MaxAttempts);
        var claimed = await context.WhatsAppNotifications
            .Where(item => item.Id == notification.Id && item.MessageIdExterno == null &&
                item.Intentos < maximumAttempts &&
                (item.Estado == "PENDING" || item.Estado == "RETRYING" || item.Estado == "FAILED"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Estado, "SENDING")
                .SetProperty(item => item.Intentos, item => item.Intentos + 1)
                .SetProperty(item => item.ActualizadoEn, DateTime.UtcNow), cancellationToken);
        await context.Entry(notification).ReloadAsync(cancellationToken);
        if (claimed == 0) return ToResult(notification);

        WhatsAppSendResult result;
        try
        {
            result = await client.SendTemplateAsync(
                recipient, templateName, language, parameters, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            notification.Estado = "UNKNOWN";
            notification.UltimoErrorCodigo = "CANCELLED";
            notification.ProximoIntentoEn = null;
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        notification.MessageIdExterno = result.MessageId;
        notification.FbTraceId = result.FbTraceId;
        notification.UltimoErrorCodigo = result.ErrorCode;
        notification.Estado = result.Accepted
            ? "ACCEPTED"
            : result.ErrorCode is "TIMEOUT" or "NETWORK" or "INVALID_RESPONSE"
                ? "UNKNOWN"
            : result.Transient && notification.Intentos < maximumAttempts
                ? "RETRYING"
                : "FAILED";
        notification.ProximoIntentoEn = notification.Estado == "RETRYING"
            ? DateTime.UtcNow.AddSeconds(Math.Pow(2, notification.Intentos))
            : null;
        notification.ActualizadoEn = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        if (!result.Accepted)
            logger.LogWarning("No se pudo enviar la notificación de WhatsApp para el turno {Turno}. Código {Codigo}.",
                notification.IdTurno, result.ErrorCode);
        return ToResult(notification);
    }

    private WhatsAppNotificationResult ToResult(WhatsAppNotification notification) =>
        new(notification.IdTurno, notification.Estado,
            notification.Estado is "FAILED" or "RETRYING" &&
                notification.MessageIdExterno is null && notification.Intentos < Math.Max(1, options.Value.MaxAttempts)
                ? "RETRY_MANUAL" : "NONE");
}
