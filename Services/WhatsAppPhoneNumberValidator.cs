using System.Text.RegularExpressions;

namespace Carwash.API.Services;

public static partial class WhatsAppPhoneNumberValidator
{
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    public static string? Normalize(string? value)
    {
        var candidate = value?.Trim();
        return candidate is not null && E164().IsMatch(candidate) ? candidate : null;
    }
}
