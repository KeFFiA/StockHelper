using System.Windows;
using StockHelper.Core.Abstractions;

namespace StockHelper.App.Services;

public interface IThemeService
{
    void Apply(AppTheme theme);
}

public sealed class ThemeService : IThemeService
{
    public void Apply(AppTheme theme)
    {
        Application.Current.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}
