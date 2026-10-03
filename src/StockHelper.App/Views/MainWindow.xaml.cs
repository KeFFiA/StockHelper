using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StockHelper.App.Controls;
using StockHelper.App.ViewModels;
using StockHelper.Core.Abstractions;

namespace StockHelper.App.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;

    public MainWindow(ShellViewModel viewModel, ISettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        RestorePlacement();
        Closing += (_, _) => SavePlacement();
    }

    /// <summary>Restores size, position and maximized state if the saved rectangle is still on a screen.</summary>
    private void RestorePlacement()
    {
        if (_settings.Current.MainWindow is not { } p || p.Width < MinWidth || p.Height < MinHeight)
        {
            return;
        }

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(p.Left, p.Top, p.Width, p.Height)))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left;
        Top = p.Top;
        Width = p.Width;
        Height = p.Height;
        if (p.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        // RestoreBounds keeps the normal size even while maximized.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var placement = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
        // Run off the UI thread: waiting here for an async save that resumes on the UI thread would deadlock.
        var settings = _settings.Current with { MainWindow = placement };
        Task.Run(() => _settings.SaveAsync(settings)).Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Ctrl+F focuses the search box of the current page (pure view concern).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
#if DEBUG
        // Developer tool: toggles light/dark at runtime (same code path as a Windows accent/theme change).
        if (e.Key == Key.T && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            var mode = Application.Current.ThemeMode;
            Application.Current.ThemeMode = mode == ThemeMode.System ? ThemeMode.Light : mode == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.System;
            e.Handled = true;
            return;
        }
#endif

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && FindVisibleSearchBox(this) is { } search)
        {
            search.Focus();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private static SearchBox? FindVisibleSearchBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is SearchBox { IsVisible: true } box)
            {
                return box;
            }

            if (child is UIElement { IsVisible: false })
            {
                continue;
            }

            if (FindVisibleSearchBox(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
