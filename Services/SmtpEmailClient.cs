using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Carwash.API.Configuration;
using Microsoft.Extensions.Options;

namespace Carwash.API.Services;

public sealed class SmtpEmailClient(IOptions<EmailOptions> options) : IEmailClient
{
    public Task<EmailSendResult> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken) =>
        SendAsync(recipient, subject, new EmailBody(body), cancellationToken);

    public async Task<EmailSendResult> SendAsync(
        string recipient,
        string subject,
        EmailBody body,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, body.SenderName ?? settings.FromName),
            Subject = subject,
            Body = body.Text,
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        message.To.Add(new MailAddress(recipient));
        if (!string.IsNullOrWhiteSpace(body.Html))
        {
            var view = AlternateView.CreateAlternateViewFromString(body.Html, Encoding.UTF8, MediaTypeNames.Text.Html);
            if (body.LogoBase64 is not null)
                view.LinkedResources.Add(new LinkedResource(new MemoryStream(Convert.FromBase64String(body.LogoBase64)), "image/png")
                { ContentId = "company-logo", TransferEncoding = TransferEncoding.Base64 });
            message.AlternateViews.Add(view);
        }

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Credentials = new NetworkCredential(settings.Username, NormalizeAppPassword(settings.Password)),
        };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, cancellationToken);
            return new EmailSendResult(true, false, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SmtpException exception)
        {
            var transient = (int)exception.StatusCode is 421 or 450 or 451 or 452;
            return new EmailSendResult(false, transient, exception.StatusCode.ToString());
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return new EmailSendResult(false, false, exception.GetType().Name);
        }
    }

    private static string NormalizeAppPassword(string password) =>
        string.Concat(password.Where(character => !char.IsWhiteSpace(character)));
}
