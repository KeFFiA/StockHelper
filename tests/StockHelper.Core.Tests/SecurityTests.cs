using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.Core.Tests;

public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_AcceptsCorrectPassword_RejectsWrong()
    {
        var hash = _hasher.Hash("секрет-123");

        Assert.True(_hasher.Verify("секрет-123", hash));
        Assert.False(_hasher.Verify("секрет-124", hash));
    }

    [Fact]
    public void Hash_IsSalted()
    {
        Assert.NotEqual(_hasher.Hash("same"), _hasher.Hash("same"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("v1.x.y.z")]
    [InlineData("v1.1000.!!!.???")]
    public void Verify_MalformedHash_ReturnsFalse(string hash)
    {
        Assert.False(_hasher.Verify("anything", hash));
    }
}

public sealed class PermissionsTests
{
    [Fact]
    public void Administrator_HasEverything()
    {
        Assert.All(Enum.GetValues<Permission>(), p => Assert.True(Permissions.Has(UserRole.Administrator, p)));
    }

    [Theory]
    [InlineData(UserRole.Manager, Permission.ManageCatalog, true)]
    [InlineData(UserRole.Manager, Permission.ReopenStockTakes, true)]
    [InlineData(UserRole.Manager, Permission.ManageUsers, false)]
    [InlineData(UserRole.Manager, Permission.ManageLocations, false)]
    [InlineData(UserRole.Storekeeper, Permission.ManageReceipts, true)]
    [InlineData(UserRole.Storekeeper, Permission.EditStockTakes, true)]
    [InlineData(UserRole.Storekeeper, Permission.ViewReports, true)]
    [InlineData(UserRole.Storekeeper, Permission.ManageCatalog, false)]
    [InlineData(UserRole.Storekeeper, Permission.ReopenStockTakes, false)]
    public void Roles_HaveExpectedPermissions(UserRole role, Permission permission, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, permission));
    }
}

public sealed class AuthServiceTests
{
    private readonly InMemoryUsers _users = new();
    private readonly TestCurrentUser _current = new();
    private readonly AuthService _auth;

    public AuthServiceTests() => _auth = new AuthService(_users, new Pbkdf2PasswordHasher(), _current);

    [Fact]
    public async Task FirstRun_CreatesAdministrator_AndSignsIn()
    {
        Assert.True(await _auth.IsSetupRequiredAsync());

        var admin = await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");

        Assert.Equal(UserRole.Administrator, admin.Role);
        Assert.Same(admin, _current.User);
        Assert.False(await _auth.IsSetupRequiredAsync());
        await Assert.ThrowsAsync<DomainException>(() => _auth.CreateFirstAdministratorAsync("other", "X", "1234"));
    }

    [Fact]
    public async Task SignIn_WrongPassword_Fails_InactiveUser_Fails()
    {
        await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");
        var clerk = await _auth.CreateUserAsync("clerk", "Кладовщик", UserRole.Storekeeper, "5678");
        _auth.SignOut();

        var wrong = await Assert.ThrowsAsync<DomainException>(() => _auth.SignInAsync("admin", "0000"));
        Assert.Equal(DomainErrorCode.InvalidCredentials, wrong.Code);

        _current.SignIn(_users.All[0]);
        clerk.IsActive = false;
        await _auth.UpdateUserAsync(clerk);
        _auth.SignOut();

        var inactive = await Assert.ThrowsAsync<DomainException>(() => _auth.SignInAsync("CLERK", "5678"));
        Assert.Equal(DomainErrorCode.UserInactive, inactive.Code);
    }

    [Fact]
    public async Task NonAdministrator_CannotManageUsers()
    {
        await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");
        await _auth.CreateUserAsync("boss", "Руководитель", UserRole.Manager, "1234");
        await _auth.SignInAsync("boss", "1234");

        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.CreateUserAsync("x", "X", UserRole.Storekeeper, "1234"));
        Assert.Equal(DomainErrorCode.AccessDenied, ex.Code);
    }

    [Fact]
    public async Task Administrator_CannotDemoteOrDeactivateSelf()
    {
        var admin = await _auth.CreateFirstAdministratorAsync("admin", "Админ", "1234");
        var edited = new User { Id = admin.Id, Login = admin.Login, DisplayName = admin.DisplayName, Role = UserRole.Manager, IsActive = true };

        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.UpdateUserAsync(edited));
        Assert.Equal(DomainErrorCode.CannotDeactivateSelf, ex.Code);
    }

    [Fact]
    public async Task ShortPassword_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.CreateFirstAdministratorAsync("admin", "Админ", "12"));
        Assert.Equal(DomainErrorCode.PasswordTooShort, ex.Code);
    }

    private sealed class InMemoryUsers : IUserRepository
    {
        public List<User> All { get; } = [];

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>([.. All]);

        public Task<User?> FindByLoginAsync(string login, CancellationToken ct = default) =>
            Task.FromResult(All.FirstOrDefault(u => u.NormalizedLogin == LookupEntity.Normalize(login)));

        public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(All.Count > 0);

        public Task<User> AddAsync(User user, CancellationToken ct = default)
        {
            user.Id = All.Count + 1;
            All.Add(user);
            return Task.FromResult(user);
        }

        public Task<User> UpdateAsync(User user, CancellationToken ct = default)
        {
            var index = All.FindIndex(u => u.Id == user.Id);
            All[index] = user;
            return Task.FromResult(user);
        }
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public User? User { get; private set; }

        public event EventHandler? Changed;

        public void SignIn(User user)
        {
            User = user;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SignOut() => User = null;
    }
}
