using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockHelper.App.Infrastructure;

namespace StockHelper.App.Views.Dialogs;

/// <summary>"What's new" window rendering the changelog Markdown.</summary>
public partial class ChangelogWindow : Window
{
    public ChangelogWindow(string title, string markdown)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        Viewer.Document = MarkdownDocument.Build(markdown, this);
        Viewer.PreviewMouseWheel += OnMouseWheel;
    }

    /// <summary>The default FlowDocument wheel step is tiny; scroll ~3x faster.</summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer(Viewer) is { } scroll)
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 1.5);
            e.Handled = true;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if ((child as ScrollViewer ?? FindScrollViewer(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
