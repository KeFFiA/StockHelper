using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace StockHelper.App.Infrastructure;

/// <summary>
/// Keeps the Fluent accent brushes in sync with the Windows accent color while the app runs.
/// WPF reads the accent once at start-up; here the palette is read from the registry (the same one
/// Windows Settings writes) and the existing brushes are recolored in place, so every control updates at once.
/// </summary>
public static class AccentColors
{
    private const string PaletteKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    // Palette order in the registry: Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3.
    private const int Light3 = 0, Light2 = 1, Accent = 3, Dark1 = 4, Dark2 = 5, Dark3 = 6;

    private static readonly (string Key, int Light, int Dark)[] Brushes =
    [
        ("AccentFillColorDefaultBrush", Dark1, Light2),
        ("AccentFillColorSecondaryBrush", Dark1, Light2),
        ("AccentFillColorTertiaryBrush", Dark1, Light2),
        ("AccentFillColorSelectedTextBackgroundBrush", Accent, Accent),
        ("AccentTextFillColorPrimaryBrush", Dark2, Light3),
        ("AccentTextFillColorSecondaryBrush", Dark3, Light3),
        ("AccentTextFillColorTertiaryBrush", Dark1, Light2),
        ("AccentButtonBackground", Dark1, Light2),
        ("AccentButtonBackgroundPointerOver", Dark1, Light2),
        ("AccentButtonBackgroundPressed", Dark1, Light2),
    ];

    /// <summary>Recolors the accent brushes with the current Windows accent for the current light/dark mode.</summary>
    public static void Apply()
    {
        if (ReadPalette() is not { } palette)
        {
            return;
        }

        var isDark = WindowTheme.IsDark;
        var resources = Application.Current.Resources;
        foreach (var (key, light, dark) in Brushes)
        {
            var color = palette[isDark ? dark : light];
            switch (resources[key] ?? Application.Current.TryFindResource(key))
            {
                case SolidColorBrush { IsFrozen: false } brush:
                    brush.Color = color;
                    break;
                case SolidColorBrush:
                    resources[key] = new SolidColorBrush(color);
                    break;
            }
        }
    }

    private static Color[]? ReadPalette()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PaletteKey);
            if (key?.GetValue("AccentPalette") is not byte[] { Length: >= 28 } bytes)
            {
                return null;
            }

            return [.. Enumerable.Range(0, 7).Select(i => Color.FromRgb(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2]))];
        }
        catch (Exception)
        {
            // No access to the registry: keep the colors WPF picked at start-up.
            return null;
        }
    }
}
