using System.Globalization;

namespace StockHelper.App.Infrastructure;

public static class Compact
{
    /// <summary>Short money label for charts: "950", "12,4 тыс.", "1,2 млн".</summary>
    public static string Money(decimal value) => value switch
    {
        >= 1_000_000 => string.Format(CultureInfo.CurrentCulture, Resources.Strings.Compact_Million, value / 1_000_000),
        >= 10_000 => string.Format(CultureInfo.CurrentCulture, Resources.Strings.Compact_Thousand, value / 1_000),
        _ => value.ToString("#,0", CultureInfo.CurrentCulture),
    };
}
