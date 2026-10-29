namespace Carwash.API.Configuration;

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string GraphVersion { get; set; } = "v23.0";
    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }
    public string? TemplateName { get; set; }
    public string TemplateLanguage { get; set; } = "es";
    public string? CarWashName { get; set; }
    public int MaxAttempts { get; set; } = 3;
}
