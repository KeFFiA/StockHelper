using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.Data.Repositories;

public sealed class RecoveryKeyRepository(IDbContextFactory<StockHelperDbContext> factory) : RepositoryBase(factory), IRecoveryKeyRepository
{
    public async Task<RecoveryKey?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await db.RecoveryKeys.AsNoTracking().OrderByDescending(k => k.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<RecoveryKey> ReplaceAsync(string codeHash, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        db.RecoveryKeys.RemoveRange(await db.RecoveryKeys.ToListAsync(ct));
        var key = new RecoveryKey { CodeHash = codeHash };
        db.RecoveryKeys.Add(key);
        await SaveAsync(db, ct);
        return key;
    }
}
