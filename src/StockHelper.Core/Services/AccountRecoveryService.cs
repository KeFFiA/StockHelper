using System.Security.Cryptography;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;

namespace StockHelper.Core.Services;

public sealed record RecoveryResult(User User, string NewCode);

/// <summary>
/// Regaining access without a hidden account:
/// <list type="bullet">
/// <item>a one-time recovery code, shown once to the administrator and stored only as a hash;</item>
/// <item>a local reset started on the computer with the database (<c>--reset-admin</c>).</item>
/// </list>
/// </summary>
public interface IAccountRecoveryService
{
    /// <summary>When the current recovery code was issued; null when there is none.</summary>
    Task<DateTime?> GetCodeIssuedAtAsync(CancellationToken ct = default);

    /// <summary>Issues a new recovery code (the old one stops working). Administrators only.</summary>
    Task<string> IssueCodeAsync(CancellationToken ct = default);

    /// <summary>Sets a new password for an administrator by the recovery code, signs them in and issues a new code.</summary>
    Task<RecoveryResult> RecoverAsync(string code, string login, string newPassword, CancellationToken ct = default);

    /// <summary>
    /// Local reset (<c>--reset-admin</c>, run on the computer with the database): makes the login an active administrator
    /// with the given password, creating the account if needed, signs in and issues a new recovery code.
    /// </summary>
    Task<RecoveryResult> ResetLocallyAsync(string login, string displayName, string password, CancellationToken ct = default);
}

public sealed class AccountRecoveryService(
    IUserRepository users,
    IRecoveryKeyRepository keys,
    IPasswordHasher hasher,
    ICurrentUserService currentUser) : IAccountRecoveryService
{
    public async Task<DateTime?> GetCodeIssuedAtAsync(CancellationToken ct = default) => (await keys.GetAsync(ct))?.CreatedAt;

    public Task<string> IssueCodeAsync(CancellationToken ct = default)
    {
        if (!currentUser.Has(Permission.ManageUsers))
        {
            throw new DomainException(DomainErrorCode.AccessDenied);
        }

        return IssueAsync(ct);
    }

    public async Task<RecoveryResult> RecoverAsync(string code, string login, string newPassword, CancellationToken ct = default)
    {
        var key = await keys.GetAsync(ct);
        var user = await users.FindByLoginAsync(login, ct);

        // One error for a wrong code and a wrong login: does not reveal which part was right.
        if (key is null || !hasher.Verify(RecoveryCode.Normalize(code), key.CodeHash) || user is not { Role: UserRole.Administrator })
        {
            // Slows down guessing; the code itself has ~100 bits of entropy.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            throw new DomainException(DomainErrorCode.InvalidRecoveryCode);
        }

        AuthService.ValidatePassword(newPassword);
        user.PasswordHash = hasher.Hash(newPassword);
        user.IsActive = true;
        user = await users.UpdateAsync(user, ct);
        currentUser.SignIn(user);

        // The code is single-use: the one just typed is no longer secret.
        return new RecoveryResult(user, await IssueAsync(ct));
    }

    public async Task<RecoveryResult> ResetLocallyAsync(string login, string displayName, string password, CancellationToken ct = default)
    {
        AuthService.ValidateNames(login, displayName);
        AuthService.ValidatePassword(password);

        var user = await users.FindByLoginAsync(login, ct);
        if (user is null)
        {
            user = await users.AddAsync(
                new User
                {
                    Login = login.Trim(),
                    DisplayName = displayName.Trim(),
                    Role = UserRole.Administrator,
                    IsActive = true,
                    PasswordHash = hasher.Hash(password),
                },
                ct);
        }
        else
        {
            user.DisplayName = displayName.Trim();
            user.Role = UserRole.Administrator;
            user.IsActive = true;
            user.PasswordHash = hasher.Hash(password);
            user = await users.UpdateAsync(user, ct);
        }

        currentUser.SignIn(user);
        return new RecoveryResult(user, await IssueAsync(ct));
    }

    private async Task<string> IssueAsync(CancellationToken ct)
    {
        var code = RecoveryCode.Generate();
        await keys.ReplaceAsync(hasher.Hash(RecoveryCode.Normalize(code)), ct);
        return code;
    }
}

/// <summary>Recovery code: 20 characters from an alphabet without look-alikes (0/O, 1/I), shown as XXXXX-XXXXX-XXXXX-XXXXX.</summary>
public static class RecoveryCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 20;

    public static string Generate()
    {
        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, Length);
        return string.Join('-', Enumerable.Range(0, Length / 5).Select(i => new string(chars, i * 5, 5)));
    }

    /// <summary>Upper case without separators and spaces, so the code can be typed in any form.</summary>
    public static string Normalize(string code) =>
        new([.. code.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-')]);
}
