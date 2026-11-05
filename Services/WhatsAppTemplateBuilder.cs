using Carwash.API.Configuration;
using Carwash.API.Modelos;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Carwash.API.Services;

public sealed class WhatsAppTemplateBuilder(IOptions<WhatsAppOptions> options)
{
    public bool TryBuild(
        Turnos turn,
        IReadOnlyList<(string Name, decimal Price)> services,
        out string recipient,
        out string templateName,
        out string language,
        out IReadOnlyList<string> parameters,
        out string reason)
    {
        var settings = options.Value;
        recipient = WhatsAppPhoneNumberValidator.Normalize(turn.NumeroCelular) ?? string.Empty;
        templateName = settings.TemplateName?.Trim() ?? string.Empty;
        language = settings.TemplateLanguage.Trim();
        parameters = Array.Empty<string>();
        reason = string.Empty;

        if (recipient.Length == 0) { reason = "PHONE_INVALID"; return false; }
        if (string.IsNullOrWhiteSpace(templateName)) { reason = "TEMPLATE_MISSING"; return false; }
        if (string.IsNullOrWhiteSpace(settings.CarWashName)) { reason = "CARWASH_NAME_MISSING"; return false; }
        if (string.IsNullOrWhiteSpace(turn.Placa) || string.IsNullOrWhiteSpace(turn.Marca))
        {
            reason = "VEHICLE_DATA_MISSING";
            return false;
        }
        if (services.Count == 0) { reason = "SERVICES_MISSING"; return false; }

        var culture = CultureInfo.GetCultureInfo("es-CO");
        parameters = new[]
        {
            turn.NombreCliente?.Trim() ?? string.Empty,
            turn.Placa.Trim(),
            turn.Marca.Trim(),
            string.Join(", ", services.Select(service => $"{service.Name}: {service.Price.ToString("N2", culture)}")),
            turn.Valor.ToString("N2", culture),
            settings.CarWashName.Trim(),
        };
        return true;
    }
}
