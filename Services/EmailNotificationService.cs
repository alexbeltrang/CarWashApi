using System.Net.Mail;
using System.Net;
using System.Globalization;
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

        var company = await new EmpresaService(context).GetAsync(cancellationToken);
        var pickupCode = await PickupCodeAsync(idTurno, cancellationToken);
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

        var body = BuildCompanyBody(
            turn,
            services.Select(item => new EmailServiceLine(item.Nombre, item.Precio)), pickupCode, company);
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
        var pickupCode = await PickupCodeAsync(idTurno, cancellationToken);
        var company = await new EmpresaService(context).GetAsync(cancellationToken);
        return await SendAsync(
            notification,
            turn.CorreoElectronico.Trim(),
            BuildSubject(turn),
            BuildCompanyBody(turn, services.Select(item => new EmailServiceLine(item.Nombre, item.Precio)), pickupCode, company),
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
        EmailBody body,
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

    private async Task<string> PickupCodeAsync(int idTurno, CancellationToken cancellationToken)
    {
        try
        {
            return (await new RetiroVehiculosService(context, TimeProvider.System)
                .IssueAsync(new EmitirComprobanteRequest(IdTurno: idTurno), cancellationToken)).Receipt.Codigo;
        }
        catch (RetiroException) { throw new EmailNotificationConflictException(); }
    }

    private static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        MailAddress.TryCreate(value.Trim(), out var address) &&
        address.Address == value.Trim();

        private static EmailBody BuildBody(Turnos turn, IEnumerable<EmailServiceLine> services, string pickupCode) =>
            BuildCompanyBody(turn, services, pickupCode, null);

        private static EmailBody BuildCompanyBody(Turnos turn, IEnumerable<EmailServiceLine> services, string pickupCode, EmpresaDto? company)
        {
                var lines = services.ToList();
                var culture = CultureInfo.GetCultureInfo("es-CO");
                var total = turn.Valor.ToString("C2", culture);
                var companyName = company?.NombreComercial ?? "el autolavado";
                var companyLines = ReceiptPrintLayout.CompanyLines(company);
                var companyHtml = string.Join("<br>", companyLines.Select(WebUtility.HtmlEncode));
                var logoHtml = company?.LogoBase64 is null ? "" : "<img src=\"cid:company-logo\" alt=\"Logo del autolavado\" style=\"max-width:160px;max-height:100px;width:auto;height:auto;display:block;margin-bottom:12px;\">";
                var text = $"""
                {string.Join(Environment.NewLine, companyLines)}
                RECEPCION DE VEHICULO

        Hola {turn.NombreCliente?.Trim()},

        Recibimos su vehículo en {companyName}.
        Placa: {turn.Placa}
        Marca: {turn.Marca}
        Código de retiro: {pickupCode}
        Servicios autorizados:
                {string.Join(Environment.NewLine, lines.Select(service => $"- {service.Name}: {service.Price.ToString("C2", culture)}"))}
                Total a pagar: {total}

                Gracias por confiar en nosotros.
        Los servicios adicionales requieren su aprobación.
        """;
                var rows = string.Join("\n", lines.Select(service => $"""
                        <tr>
                            <td style="padding:14px 0;border-bottom:1px solid #e2e8f0;overflow-wrap:anywhere;">✅ {WebUtility.HtmlEncode(service.Name)}</td>
                            <td style="padding:14px 0 14px 12px;border-bottom:1px solid #e2e8f0;text-align:right;white-space:nowrap;">{WebUtility.HtmlEncode(service.Price.ToString("C2", culture))}</td>
                        </tr>
                        """));
                var html = $"""
                        <!doctype html>
                        <html lang="es">
                        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Recepción de vehículo</title></head>
                        <body style="margin:0;padding:0;background:#f1f5f9;color:#243347;font-family:'Trebuchet MS',Arial,sans-serif;">
                            <table role="presentation" style="width:100%;border-collapse:collapse;"><tr><td align="center" style="padding:24px 12px;">
                                <table role="presentation" style="width:100%;max-width:600px;border-collapse:collapse;background:#ffffff;">
                                    <tr><td style="padding:28px 24px;background:#e6effc;border-top:4px solid #2862a5;">
                                        {logoHtml}
                                        <p style="margin:0 0 8px;color:#2862a5;font-size:16px;font-weight:bold;overflow-wrap:anywhere;">{companyHtml}</p>
                                        <h1 style="margin:0;font-family:Georgia,serif;font-size:28px;line-height:1.3;color:#19395c;">🚘 Recepción de vehículo</h1>
                                    </td></tr>
                                    <tr><td style="padding:28px 24px 8px;">
                                        <p style="margin:0 0 12px;font-size:18px;line-height:1.5;overflow-wrap:anywhere;">Hola, <strong>{WebUtility.HtmlEncode(turn.NombreCliente?.Trim())}</strong> 👋</p>
                                        <p style="margin:0 0 24px;line-height:1.6;">Recibimos su vehículo. Estos son los datos de su ingreso:</p>
                                        <p style="margin:0 0 10px;line-height:1.5;overflow-wrap:anywhere;">🔖 <strong>Placa:</strong> {WebUtility.HtmlEncode(turn.Placa)}</p>
                                        <p style="margin:0 0 24px;line-height:1.5;overflow-wrap:anywhere;">🚗 <strong>Marca:</strong> {WebUtility.HtmlEncode(turn.Marca)}</p>
                                        <p style="margin:0 0 24px;padding:16px;background:#fff2cf;line-height:1.5;overflow-wrap:anywhere;">🔐 <strong>Código de retiro:</strong><br><span style="font-family:monospace;font-size:20px;">{WebUtility.HtmlEncode(pickupCode)}</span></p>
                                        <h2 style="margin:0 0 8px;font-size:18px;">🧽 Servicios autorizados</h2>
                                        <table style="width:100%;border-collapse:collapse;font-size:15px;line-height:1.5;">
                                            <thead><tr><th scope="col" style="text-align:left;padding:10px 0;color:#526174;">Servicio</th><th scope="col" style="text-align:right;padding:10px 0;color:#526174;">Valor</th></tr></thead>
                                            <tbody>{rows}</tbody>
                                        </table>
                                    </td></tr>
                                    <tr><td style="padding:20px 24px;">
                                        <table role="presentation" style="width:100%;border-collapse:collapse;background:#e8f5ee;"><tr><td style="padding:20px;">
                                            <p style="margin:0 0 8px;color:#286147;font-size:14px;font-weight:bold;">💰 TOTAL A PAGAR</p>
                                            <p style="margin:0;color:#164a32;font-size:28px;font-weight:bold;overflow-wrap:anywhere;">{WebUtility.HtmlEncode(total)}</p>
                                        </td></tr></table>
                                    </td></tr>
                                    <tr><td style="padding:4px 24px 28px;line-height:1.6;">
                                        <p style="margin:0 0 8px;font-weight:bold;">¡Gracias por confiar en nosotros! ✨</p>
                                        <p style="margin:0;color:#526174;font-size:13px;">Los servicios adicionales requieren su aprobación.</p>
                                    </td></tr>
                                </table>
                            </td></tr></table>
                        </body>
                        </html>
                        """;
                return new EmailBody(text, html, company?.LogoBase64, company?.NombreComercial);
        }

    private static string BuildSubject(Turnos turn) =>
        $"Recepción de vehículo - Placa: {turn.Placa?.Trim()} - Marca: {turn.Marca?.Trim()}";

    private sealed record EmailServiceLine(string Name, decimal Price);

    private static EmailNotificationResult ToResult(EmailNotification notification) =>
        new(notification.IdTurno, notification.Estado,
            notification.Estado is "FAILED" or "RETRYING" ? "RETRY_MANUAL" : "NONE");
}
