using System.Net.Mail;
using Carwash.API.Configuration;
using Carwash.API.Context;
using Carwash.API.DTOs;
using Carwash.API.Modelos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Carwash.API.Services;

public sealed class EmailNotificationConflictException : Exception;

public sealed class EmailNotificationService(
    AppDbContext context,
    IEmailClient client,
    IOptions<EmailOptions> options,
    ILogger<EmailNotificationService> logger)
{
    public async Task<EmailNotificationResult?> NotifyAsync(int idTurno, CancellationToken cancellationToken)
    {
        var existing = await context.EmailNotifications.SingleOrDefaultAsync(
            item => item.IdTurno == idTurno, cancellationToken);
        if (existing is not null)
            return ToResult(existing);

        var turn = await context.Turnos.SingleOrDefaultAsync(
            item => item.IdTurno == idTurno, cancellationToken);
        if (turn is null)
            return null;
        if (!IsValidEmail(turn.CorreoElectronico))
            return await SaveNotApplicableAsync(idTurno, "EMAIL_INVALID", cancellationToken);

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
            throw new EmailNotificationConflictException();

        var notification = new EmailNotification
        {
            IdTurno = idTurno,
            Destinatario = turn.CorreoElectronico!.Trim(),
            Estado = "PENDING",
            CreadoEn = DateTime.UtcNow,
            ActualizadoEn = DateTime.UtcNow,
        };
        context.EmailNotifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);

        var body = BuildBody(
            turn,
            services.Select(item => new EmailServiceLine(item.Nombre, item.Precio)));
        return await SendAsync(
            notification,
            turn.CorreoElectronico.Trim(),
            BuildSubject(turn),
            body,
            cancellationToken);
    }

    public async Task<EmailNotificationResult?> RetryAsync(int idTurno, CancellationToken cancellationToken)
    {
        var notification = await context.EmailNotifications.SingleOrDefaultAsync(
            item => item.IdTurno == idTurno, cancellationToken);
        if (notification is null)
            return await NotifyAsync(idTurno, cancellationToken);
        if (notification.Estado is "ACCEPTED" or "NOT_APPLICABLE")
            return ToResult(notification);

        var turn = await context.Turnos.SingleOrDefaultAsync(
            item => item.IdTurno == idTurno, cancellationToken);
        if (turn is null)
            return null;
        if (!IsValidEmail(turn.CorreoElectronico))
            return ToResult(notification);

        var services = await (
            from relation in context.TurnoServicios
            join service in context.Servicios on relation.idServicios equals service.idServicio
            join price in context.PrecioServicioVehiculo
                on new { relation.idServicios, turn.IdTipoVehiculo }
                equals new { idServicios = price.idServicio, price.IdTipoVehiculo }
            where relation.IdTurno == idTurno && !relation.IsDeleted
            select new { service.Nombre, price.Precio }
        ).ToListAsync(cancellationToken);
        return await SendAsync(
            notification,
            turn.CorreoElectronico.Trim(),
            BuildSubject(turn),
            BuildBody(turn, services.Select(item => new EmailServiceLine(item.Nombre, item.Precio))),
            cancellationToken);
    }

    public async Task<EmailNotificationResult?> GetAsync(int idTurno, CancellationToken cancellationToken) =>
        (await context.EmailNotifications.SingleOrDefaultAsync(
            item => item.IdTurno == idTurno, cancellationToken)) is { } notification
            ? ToResult(notification)
            : null;

    private async Task<EmailNotificationResult> SaveNotApplicableAsync(
        int idTurno, string reason, CancellationToken cancellationToken)
    {
        var notification = new EmailNotification
        {
            IdTurno = idTurno,
            Estado = "NOT_APPLICABLE",
            UltimoErrorCodigo = reason,
            CreadoEn = DateTime.UtcNow,
            ActualizadoEn = DateTime.UtcNow,
        };
        context.EmailNotifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);
        return ToResult(notification);
    }

    private async Task<EmailNotificationResult> SendAsync(
        EmailNotification notification,
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        notification.Intentos++;
        notification.Estado = "RETRYING";
        var result = await client.SendAsync(
            recipient,
            subject,
            body,
            cancellationToken);
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
            logger.LogWarning("No se pudo enviar el correo de confirmación para el turno {Turno}. Código {Codigo}.",
                notification.IdTurno, result.ErrorCode);
        return ToResult(notification);
    }

    private static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        MailAddress.TryCreate(value.Trim(), out var address) &&
        address.Address == value.Trim();

    private static string BuildBody(Turnos turn, IEnumerable<EmailServiceLine> services) =>
        $"""
        Hola {turn.NombreCliente?.Trim()},

        Recibimos su vehículo en CarWash.
        Placa: {turn.Placa}
        Marca: {turn.Marca}
        Servicios autorizados:
        {string.Join(Environment.NewLine, services.Select(service => $"- {service.Name}: {service.Price:N2}"))}
        Total a pagar: {turn.Valor:N2}

        Los servicios adicionales requieren su aprobación.
        """;

    private static string BuildSubject(Turnos turn) =>
        $"Recepción de vehículo - Placa: {turn.Placa?.Trim()} - Marca: {turn.Marca?.Trim()}";

    private sealed record EmailServiceLine(string Name, decimal Price);

    private static EmailNotificationResult ToResult(EmailNotification notification) =>
        new(notification.IdTurno, notification.Estado,
            notification.Estado is "FAILED" or "RETRYING" ? "RETRY_MANUAL" : "NONE");
}
