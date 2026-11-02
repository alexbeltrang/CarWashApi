using System.Net;
using System.Net.Mail;
using Carwash.API.Configuration;
using Microsoft.Extensions.Options;

namespace Carwash.API.Services;

public sealed class SmtpEmailClient(IOptions<EmailOptions> options) : IEmailClient
{
    public async Task<EmailSendResult> SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        message.To.Add(new MailAddress(recipient));

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
