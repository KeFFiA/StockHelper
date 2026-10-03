using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StockHelper.App.Controls;
using StockHelper.App.ViewModels;

namespace StockHelper.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Ctrl+F focuses the search box of the current page (pure view concern).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
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
