using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StockHelper.App.Behaviors;

/// <summary>
/// One click on a data row runs a command (e.g. opens the editor). Clicks on headers, scrollbars and
/// buttons inside cells are ignored, so row actions keep working.
/// </summary>
public static class GridBehavior
{
    public static readonly DependencyProperty RowClickCommandProperty = DependencyProperty.RegisterAttached(
        "RowClickCommand", typeof(ICommand), typeof(GridBehavior), new PropertyMetadata(null, OnRowClickCommandChanged));

    public static ICommand? GetRowClickCommand(DependencyObject d) => (ICommand?)d.GetValue(RowClickCommandProperty);

    public static void SetRowClickCommand(DependencyObject d, ICommand? value) => d.SetValue(RowClickCommandProperty, value);

    private static void OnRowClickCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.PreviewMouseLeftButtonUp -= OnMouseUp;
        if (e.NewValue is not null)
        {
            grid.PreviewMouseLeftButtonUp += OnMouseUp;
        }
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        // Find the row under the pointer; stop at buttons so in-cell actions are not hijacked.
        for (var current = source; current is not null && current != grid; current = VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.DataGridColumnHeader or System.Windows.Controls.Primitives.ScrollBar)
            {
                return;
            }

            if (current is DataGridRow row)
            {
                grid.SelectedItem = row.Item;
                if (GetRowClickCommand(grid) is { } command && command.CanExecute(row.Item))
                {
                    command.Execute(row.Item);
                }

                return;
            }
        }
    }
}
