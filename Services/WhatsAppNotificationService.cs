using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Carwash.API.Configuration;

namespace Carwash.API.Services;

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
            await context.SaveChangesAsync(cancellationToken);
            return ToResult(notification);
        }

        await context.SaveChangesAsync(cancellationToken);
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
            notification.Estado is "ACCEPTED" or "DELIVERED" or "NOT_APPLICABLE")
            return ToResult(notification);

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
        notification.Intentos++;
        notification.Estado = "RETRYING";
        notification.ActualizadoEn = DateTime.UtcNow;
        var result = await client.SendTemplateAsync(
            recipient, templateName, language, parameters, cancellationToken);

        notification.MessageIdExterno = result.MessageId;
        notification.FbTraceId = result.FbTraceId;
        notification.UltimoErrorCodigo = result.ErrorCode;
        notification.Estado = result.Accepted
            ? "ACCEPTED"
            : result.Transient && notification.Intentos < options.Value.MaxAttempts
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

    private static WhatsAppNotificationResult ToResult(WhatsAppNotification notification) =>
        new(notification.IdTurno, notification.Estado,
            notification.Estado is "FAILED" or "RETRYING" ? "RETRY_MANUAL" : "NONE");
}
