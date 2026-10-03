using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StockHelper.App.ViewModels.Pages;

namespace StockHelper.App.Views.Pages;

/// <summary>
/// Keyboard-first counting: Enter / ↓ commit the value and move to the next row, ↑ to the previous one.
/// Only focus handling lives here; saving is done by the view model.
/// </summary>
public partial class StockTakeSessionView : UserControl
{
    public StockTakeSessionView() => InitializeComponent();

    private void OnCountKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not StockTakeRow row)
        {
            return;
        }

        var step = e.Key switch
        {
            Key.Enter or Key.Down => 1,
            Key.Up => -1,
            _ => 0,
        };

        if (step == 0)
        {
            return;
        }

        e.Handled = true;
        row.CommitCommand.Execute(null);
        MoveFocus(row, step);
    }

    private void OnCountLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: StockTakeRow row })
        {
            row.CommitCommand.Execute(null);
        }
    }

    private void OnCountGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.SelectAll();
        }
    }

    private void OnNoteLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is StockTakeSessionViewModel vm)
        {
            vm.SaveNoteCommand.Execute(null);
        }
    }

    private void MoveFocus(StockTakeRow current, int step)
    {
        var index = RowsList.Items.IndexOf(current) + step;
        if (index < 0 || index >= RowsList.Items.Count)
        {
            return;
        }

        var target = RowsList.Items[index];
        RowsList.ScrollIntoView(target);

        // The container may be realized only after layout (virtualization).
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (RowsList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem container &&
                FindChild<TextBox>(container, "CountBox") is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        });
    }

    private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            if (FindChild<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
