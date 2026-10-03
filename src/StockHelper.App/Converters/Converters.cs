using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StockHelper.App.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Null → Collapsed, otherwise Visible (or the opposite when <see cref="Invert"/> is set).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null || value is string s && s.Length == 0;
        return isNull ^ Invert ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound collection or count is empty; used for empty-state placeholders.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value switch
        {
            null => true,
            int count => count == 0,
            ICollection collection => collection.Count == 0,
            IEnumerable enumerable => !enumerable.GetEnumerator().MoveNext(),
            _ => false,
        };
        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Dates are stored in UTC and displayed in local time.</summary>
public sealed class UtcToLocalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc)
        {
            return value;
        }

        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return parameter is string format ? local.ToString(format, culture) : local;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Resolves a resource key (e.g. "Icon.Home") to the resource value.</summary>
public sealed class ResourceKeyToValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Displays an enum value using the "{EnumType}_{Value}" string from Strings.resx.</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? EnumDisplay.Get(e) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class EnumDisplay
{
    public static string Get(Enum value) =>
        Resources.Strings.ResourceManager.GetString($"{value.GetType().Name}_{value}", Resources.Strings.Culture) ?? value.ToString();
}

/// <summary>Formats decimals compactly ("12,5" instead of "12,5000").</summary>
public sealed class DecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal d => d.ToString(parameter as string ?? "#,0.####", culture),
        null => string.Empty,
        _ => value,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a forecast in days: "≈ 12 дн.", "&lt; 1 дн.", "&gt; 1 года".</summary>
public sealed class DaysConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal d when d < 1 => Resources.Strings.Days_LessThanOne,
        decimal d when d > 365 => Resources.Strings.Days_MoreThanYear,
        decimal d => string.Format(culture, Resources.Strings.Days_Format, Math.Floor(d)),
        _ => Resources.Strings.Days_Unknown,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows how a movement was entered in a package unit ("2 × Банка 10 л").</summary>
public sealed class EnteredConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Core.Entities.IEnteredInUnit movement ? Infrastructure.Formatting.Entered(movement) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ZeroToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 or null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>"Вернули 2 л" for a returned issue.</summary>
public sealed class ReturnedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Core.Entities.Issue { ReturnedQuantity: { } returned } issue
            ? string.Format(Resources.Strings.Issues_StatusReturned, $"{Infrastructure.NumberInput.Format(returned)} {issue.Item?.Unit?.Name}".Trim())
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
