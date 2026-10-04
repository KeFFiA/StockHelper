using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockHelper.App.Infrastructure;

namespace StockHelper.App.Views.Dialogs;

/// <summary>Read-only Markdown document in a card window: the changelog ("What's new") and the license agreement.</summary>
public partial class DocumentWindow : Window
{
    public DocumentWindow(string title, string subtitle, string iconKey, string markdown)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        IconText.Text = (string)FindResource(iconKey);
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
