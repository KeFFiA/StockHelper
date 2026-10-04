using System.Windows;
using StockHelper.App.Infrastructure;
using StockHelper.App.Services;
using StockHelper.App.ViewModels;

namespace StockHelper.App.Views;

public partial class AuthWindow : Window
{
    public AuthWindow(AuthViewModel viewModel, IDialogService dialogs)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (AppInfo.IsDemo)
        {
            // Room for the demo account buttons under the form.
            Height += 150;
        }
        viewModel.Succeeded += (_, _) =>
        {
            // A new recovery code (first run, recovery, local reset) is shown once before going on.
            if (viewModel.IssuedCode is { } code)
            {
                dialogs.ShowRecoveryCode(code);
            }

            DialogResult = true;
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.Mode) && viewModel.Mode == AuthMode.Recover)
            {
                Dispatcher.BeginInvoke(() => CodeBox.Focus());
            }
        };

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
