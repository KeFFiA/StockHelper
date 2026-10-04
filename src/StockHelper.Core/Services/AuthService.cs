using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;

namespace StockHelper.Core.Services;

public interface IAuthService
{
    Task<bool> IsSetupRequiredAsync(CancellationToken ct = default);

    /// <summary>Creates the first administrator and signs them in.</summary>
    Task<User> CreateFirstAdministratorAsync(string login, string displayName, string password, CancellationToken ct = default);

    Task<User> SignInAsync(string login, string password, CancellationToken ct = default);

    void SignOut();

    Task<User> CreateUserAsync(string login, string displayName, UserRole role, string password, CancellationToken ct = default);

    Task<User> UpdateUserAsync(User user, CancellationToken ct = default);

    Task SetPasswordAsync(User user, string newPassword, CancellationToken ct = default);

    /// <summary>Changes the signed-in user's password after checking the current one.</summary>
    Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default);
}

public sealed class AuthService(IUserRepository users, IPasswordHasher hasher, ICurrentUserService currentUser) : IAuthService
{
    public const int MinPasswordLength = 4;

    public async Task<bool> IsSetupRequiredAsync(CancellationToken ct = default) => !await users.AnyAsync(ct);

    public async Task<User> CreateFirstAdministratorAsync(string login, string displayName, string password, CancellationToken ct = default)
    {
        if (await users.AnyAsync(ct))
        {
            throw new DomainException(DomainErrorCode.AccessDenied, "Users already exist.");
        }

        var user = await AddUserAsync(login, displayName, UserRole.Administrator, password, ct);
        currentUser.SignIn(user);
        return user;
    }

    public async Task<User> SignInAsync(string login, string password, CancellationToken ct = default)
    {
        var user = await users.FindByLoginAsync(login, ct);
        if (user is null || !hasher.Verify(password, user.PasswordHash))
        {
            throw new DomainException(DomainErrorCode.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            throw new DomainException(DomainErrorCode.UserInactive);
        }

        currentUser.SignIn(user);
        return user;
    }

    public void SignOut() => currentUser.SignOut();

    public Task<User> CreateUserAsync(string login, string displayName, UserRole role, string password, CancellationToken ct = default)
    {
        Demand(Permission.ManageUsers);
        return AddUserAsync(login, displayName, role, password, ct);
    }

    public async Task<User> UpdateUserAsync(User user, CancellationToken ct = default)
    {
        Demand(Permission.ManageUsers);
        ValidateNames(user.Login, user.DisplayName);

        var self = currentUser.User!;
        if (user.Id == self.Id && (!user.IsActive || user.Role != UserRole.Administrator))
        {
            // Prevents an administrator from locking themselves (and possibly everyone) out.
            throw new DomainException(DomainErrorCode.CannotDeactivateSelf);
        }

        var all = await users.GetAllAsync(ct);
        var remainingAdmins = all.Count(u => u.Id != user.Id && u.IsActive && u.Role == UserRole.Administrator);
        if (remainingAdmins == 0 && (!user.IsActive || user.Role != UserRole.Administrator))
        {
            throw new DomainException(DomainErrorCode.LastAdministrator);
        }

        return await users.UpdateAsync(user, ct);
    }

    public async Task SetPasswordAsync(User user, string newPassword, CancellationToken ct = default)
    {
        if (currentUser.User?.Id != user.Id)
        {
            Demand(Permission.ManageUsers);
        }

        ValidatePassword(newPassword);
        user.PasswordHash = hasher.Hash(newPassword);
        await users.UpdateAsync(user, ct);
    }

    public async Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var self = currentUser.User ?? throw new DomainException(DomainErrorCode.AccessDenied);
        var stored = await users.FindByLoginAsync(self.Login, ct) ?? throw new DomainException(DomainErrorCode.NotFound);
        if (!hasher.Verify(currentPassword, stored.PasswordHash))
        {
            throw new DomainException(DomainErrorCode.InvalidCredentials);
        }

        await SetPasswordAsync(stored, newPassword, ct);
    }

    private async Task<User> AddUserAsync(string login, string displayName, UserRole role, string password, CancellationToken ct)
    {
        ValidateNames(login, displayName);
        ValidatePassword(password);

        var user = new User
        {
            Login = login.Trim(),
            DisplayName = displayName.Trim(),
            Role = role,
            IsActive = true,
            PasswordHash = hasher.Hash(password),
        };
        return await users.AddAsync(user, ct);
    }

    private void Demand(Permission permission)
    {
        if (!currentUser.Has(permission))
        {
            throw new DomainException(DomainErrorCode.AccessDenied);
        }
    }

    internal static void ValidateNames(string login, string displayName)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException(DomainErrorCode.NameRequired);
        }
    }

    internal static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
        {
            throw new DomainException(DomainErrorCode.PasswordTooShort);
        }
    }
}
