using System.Windows;
using StockHelper.App.ViewModels;

namespace StockHelper.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
