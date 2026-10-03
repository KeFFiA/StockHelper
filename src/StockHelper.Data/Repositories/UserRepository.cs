using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;

namespace StockHelper.Data.Repositories;

public sealed class UserRepository(IDbContextFactory<StockHelperDbContext> factory) : RepositoryBase(factory), IUserRepository
{
    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var list = await db.Users.AsNoTracking().ToListAsync(ct);
        return [.. list.OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<User?> FindByLoginAsync(string login, CancellationToken ct = default)
    {
        var normalized = LookupEntity.Normalize(login);
        await using var db = await CreateAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedLogin == normalized, ct);
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await db.Users.AnyAsync(ct);
    }

    public async Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        await EnsureUniqueAsync(db, user, ct);
        db.Users.Add(user);
        await SaveAsync(db, ct);
        return user;
    }

    public async Task<User> UpdateAsync(User user, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        await EnsureUniqueAsync(db, user, ct);
        var tracked = Required(await db.Users.FindAsync([user.Id], ct));
        ExpectStamp(db, tracked, user.ConcurrencyStamp);
        tracked.Login = user.Login.Trim();
        tracked.DisplayName = user.DisplayName.Trim();
        tracked.Role = user.Role;
        tracked.IsActive = user.IsActive;
        tracked.PasswordHash = user.PasswordHash;
        await SaveAsync(db, ct);
        return tracked;
    }

    private static async Task EnsureUniqueAsync(StockHelperDbContext db, User user, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.NormalizedLogin == user.NormalizedLogin && u.Id != user.Id, ct))
        {
            throw new DomainException(DomainErrorCode.LoginNotUnique, user.Login);
        }
    }
}
