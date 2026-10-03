using StockHelper.Core.Entities;
using StockHelper.Core.Security;

namespace StockHelper.App.Services;

/// <summary>Desktop session: the user who signed in on this PC.</summary>
public sealed class CurrentUserService : ICurrentUserService
{
    public User? User { get; private set; }

    public event EventHandler? Changed;

    public void SignIn(User user)
    {
        User = user;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        User = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
