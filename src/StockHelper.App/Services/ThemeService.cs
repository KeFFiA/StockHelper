using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using StockHelper.App.Infrastructure;
using StockHelper.Core.Abstractions;

namespace StockHelper.App.Services;

public interface IThemeService
{
    void Apply(AppTheme theme);
}

/// <summary>Applies the Fluent theme, the matching surface palette and the title bar color.</summary>
public sealed class ThemeService : IThemeService
{
    private static readonly Uri LightPalette = new("pack://application:,,,/StockHelper;component/Themes/Palette.Light.xaml");
    private static readonly Uri DarkPalette = new("pack://application:,,,/StockHelper;component/Themes/Palette.Dark.xaml");

    private AppTheme _theme = AppTheme.System;
    private bool _listening;

    public void Apply(AppTheme theme)
    {
        _theme = theme;
        Application.Current.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        WindowTheme.SetTheme(theme);
        ApplyPalette();

        if (!_listening)
        {
            // "As in system": follow Windows light/dark mode. The event also fires for unrelated settings
            // (keyboard layout, accent color...), so react later, outside WPF's own theme update, and only on a real change.
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _theme == AppTheme.System)
                {
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        WindowTheme.SetTheme(_theme);
                        ApplyPalette();
                    });
                }
            };
            _listening = true;
        }
    }

    private static void ApplyPalette()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source == LightPalette || d.Source == DarkPalette);
        var source = WindowTheme.IsDark ? DarkPalette : LightPalette;
        if (existing?.Source == source)
        {
            return;
        }

        // Appended after the theme dictionaries so WPF's theme manager never mistakes it for its own.
        var palette = new ResourceDictionary { Source = source };
        if (existing is null)
        {
            dictionaries.Add(palette);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = palette;
        }
    }
}
