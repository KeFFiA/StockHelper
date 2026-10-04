using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.Core.Tests;

public sealed class AccountRecoveryTests
{
    private readonly AuthServiceTests.InMemoryUsers _users = new();
    private readonly AuthServiceTests.TestCurrentUser _current = new();
    private readonly InMemoryKeys _keys = new();
    private readonly AuthService _auth;
    private readonly AccountRecoveryService _recovery;

    public AccountRecoveryTests()
    {
        var hasher = new Pbkdf2PasswordHasher();
        _auth = new AuthService(_users, hasher, _current);
        _recovery = new AccountRecoveryService(_users, _keys, hasher, _current);
    }

    [Fact]
    public void Code_Has20CharactersInGroups_AndNormalizes()
    {
        var code = RecoveryCode.Generate();

        Assert.Matches("^[A-Z2-9]{5}(-[A-Z2-9]{5}){3}$", code);
        Assert.DoesNotContain('O', code);
        Assert.Equal(code.Replace("-", string.Empty), RecoveryCode.Normalize(" " + code.ToLowerInvariant().Replace('-', ' ') + " "));
    }

    [Fact]
    public async Task Recover_WithCode_SetsPassword_ReactivatesAndIssuesNewCode()
    {
        await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");
        var code = await _recovery.IssueCodeAsync();
        _auth.SignOut();

        var result = await _recovery.RecoverAsync(code.ToLowerInvariant(), "ADMIN", "new-pass");

        Assert.Same(result.User, _current.User);
        Assert.True(result.User.IsActive);
        Assert.NotEqual(code, result.NewCode);
        _auth.SignOut();
        await _auth.SignInAsync("admin", "new-pass");

        // Single use: the old code no longer works, the new one does.
        await Assert.ThrowsAsync<DomainException>(() => _recovery.RecoverAsync(code, "admin", "other1"));
        await _recovery.RecoverAsync(result.NewCode, "admin", "other1");
    }

    [Fact]
    public async Task Recover_WrongCode_OrNonAdministrator_Fails()
    {
        await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");
        await _auth.CreateUserAsync("clerk", "Кладовщик", UserRole.Storekeeper, "1234");
        var code = await _recovery.IssueCodeAsync();
        _auth.SignOut();

        var wrong = await Assert.ThrowsAsync<DomainException>(() => _recovery.RecoverAsync("AAAAA-AAAAA-AAAAA-AAAAA", "admin", "new-pass"));
        Assert.Equal(DomainErrorCode.InvalidRecoveryCode, wrong.Code);
        var clerk = await Assert.ThrowsAsync<DomainException>(() => _recovery.RecoverAsync(code, "clerk", "new-pass"));
        Assert.Equal(DomainErrorCode.InvalidRecoveryCode, clerk.Code);
        Assert.Null(_current.User);
    }

    [Fact]
    public async Task IssueCode_RequiresAdministrator()
    {
        await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");
        await _auth.CreateUserAsync("boss", "Руководитель", UserRole.Manager, "1234");
        await _auth.SignInAsync("boss", "1234");

        var ex = await Assert.ThrowsAsync<DomainException>(() => _recovery.IssueCodeAsync());
        Assert.Equal(DomainErrorCode.AccessDenied, ex.Code);
    }

    [Fact]
    public async Task LocalReset_PromotesExistingUser_OrCreatesAdministrator()
    {
        await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");
        var clerk = await _auth.CreateUserAsync("clerk", "Кладовщик", UserRole.Storekeeper, "1234");
        clerk.IsActive = false;
        await _auth.UpdateUserAsync(clerk);
        _auth.SignOut();

        var promoted = await _recovery.ResetLocallyAsync("clerk", "Новый админ", "secret");
        Assert.Equal(UserRole.Administrator, promoted.User.Role);
        Assert.True(promoted.User.IsActive);
        Assert.Equal(2, _users.All.Count);

        var created = await _recovery.ResetLocallyAsync("rescue", "Резерв", "secret");
        Assert.Equal(UserRole.Administrator, created.User.Role);
        Assert.Equal(3, _users.All.Count);
        await _recovery.RecoverAsync(created.NewCode, "rescue", "secret2");
    }

    private sealed class InMemoryKeys : IRecoveryKeyRepository
    {
        private RecoveryKey? _key;

        public Task<RecoveryKey?> GetAsync(CancellationToken ct = default) => Task.FromResult(_key);

        public Task<RecoveryKey> ReplaceAsync(string codeHash, CancellationToken ct = default)
        {
            _key = new RecoveryKey { CodeHash = codeHash, CreatedAt = DateTime.UtcNow };
            return Task.FromResult(_key);
        }
    }
}
