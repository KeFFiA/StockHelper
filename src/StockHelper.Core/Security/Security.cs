using System.Security.Cryptography;
using StockHelper.Core.Entities;

namespace StockHelper.Core.Security;

public enum Permission
{
    ViewReports,
    ManageCatalog,
    ManageReceipts,
    ManageIssues,
    EditStockTakes,
    ReopenStockTakes,
    ManageLocations,
    ManageUsers,
    ManageSettings,
    ManageBackups,
}

public static class Permissions
{
    private static readonly Dictionary<UserRole, HashSet<Permission>> Map = new()
    {
        [UserRole.Storekeeper] = [Permission.ViewReports, Permission.ManageReceipts, Permission.ManageIssues, Permission.EditStockTakes],
        [UserRole.Manager] = [Permission.ViewReports, Permission.ManageCatalog, Permission.ReopenStockTakes],
        [UserRole.Administrator] = [.. Enum.GetValues<Permission>()],
    };

    public static bool Has(UserRole role, Permission permission) => Map[role].Contains(permission);
}

/// <summary>Identity of the signed-in user. Implemented by the app (desktop session) or, later, a server.</summary>
public interface ICurrentUserService
{
    User? User { get; }

    bool IsAuthenticated => User is not null;

    /// <summary>Name written to audit fields.</summary>
    string AuditName => User?.Login ?? "system";

    bool Has(Permission permission) => User is not null && Permissions.Has(User.Role, permission);

    void SignIn(User user);

    void SignOut();

    event EventHandler? Changed;
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

/// <summary>PBKDF2-SHA256 with a random salt. Format: <c>v1.{iterations}.{salt}.{hash}</c> (Base64).</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
