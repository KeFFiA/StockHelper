using System.Windows;
using StockHelper.App.ViewModels;

namespace StockHelper.App.Views;

public partial class AuthWindow : Window
{
    public AuthWindow(AuthViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Succeeded += (_, _) => DialogResult = true;

        // Focus the first empty field (the login is remembered between sessions).
        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(viewModel.Login))
            {
                LoginBox.Focus();
            }
            else
            {
                PasswordBox.Focus();
            }
        };
    }
}
