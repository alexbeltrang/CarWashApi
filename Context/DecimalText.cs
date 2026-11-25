using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Carwash.API.Context;

public static class DecimalText
{
    public static decimal FromExactReal(double value)
    {
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        var exponent = (int)((bits >> 52) & 2047);
        if (exponent == 2047) throw new FormatException("Non-finite SQLite REAL.");
        BigInteger coefficient = bits & 0xfffffffffffffUL;
        if (exponent != 0) coefficient |= BigInteger.One << 52;
        if (coefficient.IsZero) return 0m;
        var power = exponent == 0 ? -1074 : exponent - 1023 - 52;
        if (power >= 0) coefficient <<= power;
        else
        {
            while (power < 0 && coefficient.IsEven)
            {
                coefficient >>= 1;
                power++;
            }
            if (-power > 28) throw new FormatException("SQLite REAL requires rounding.");
            coefficient *= BigInteger.Pow(5, -power);
        }
        if (coefficient > (BigInteger.One << 96) - 1)
            throw new FormatException("SQLite REAL exceeds exact decimal precision.");
        return new decimal(unchecked((int)(uint)(coefficient & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue)),
            (bits >> 63) != 0, (byte)Math.Max(0, -power));
    }

    public static string Format(decimal value) => value == 0m
        ? "0"
        : value.ToString("0.############################", CultureInfo.InvariantCulture);

    public static decimal Parse(string? text)
    {
        if (text is not { Length: > 0 and <= 60 }
            || !Regex.IsMatch(text, @"\A-?(0|[1-9][0-9]*)(\.[0-9]+)?\z", RegexOptions.CultureInvariant)
            || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Invalid exact decimal text.");

        var normalized = text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
        if (Format(value) != normalized)
            throw new FormatException("Decimal text loses precision or is not canonical.");

        return value;
    }
}