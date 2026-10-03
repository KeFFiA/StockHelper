using System.Globalization;

namespace StockHelper.App.Infrastructure;

/// <summary>Combines a picked local date with a "HH:mm" time text and converts to UTC.</summary>
public static class DateTimeInput
{
    public static bool TryCombine(DateTime? localDate, string? timeText, out DateTime utc)
    {
        utc = default;
        if (localDate is null)
        {
            return false;
        }

        var time = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(timeText) &&
            !TimeSpan.TryParseExact(timeText.Trim(), [@"h\:mm", @"hh\:mm", @"h\.mm", @"hh\.mm"], CultureInfo.InvariantCulture, out time))
        {
            return false;
        }

        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            return false;
        }

        var local = DateTime.SpecifyKind(localDate.Value.Date + time, DateTimeKind.Local);
        utc = local.ToUniversalTime();
        return true;
    }

    public static (DateTime Date, string Time) Split(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return (local.Date, local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }
}
