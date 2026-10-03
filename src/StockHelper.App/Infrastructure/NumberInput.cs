using System.Globalization;

namespace StockHelper.App.Infrastructure;

/// <summary>Lenient decimal parsing for text inputs: accepts both "," and "." as the decimal separator.</summary>
public static class NumberInput
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var culture = CultureInfo.CurrentCulture;
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        var normalized = text.Trim()
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(",", separator)
            .Replace(".", separator);

        return decimal.TryParse(normalized, NumberStyles.Number, culture, out value);
    }

    public static string Format(decimal value) => value.ToString("0.####", CultureInfo.CurrentCulture);

    public static string Format(decimal? value) => value is { } v ? Format(v) : string.Empty;
}
