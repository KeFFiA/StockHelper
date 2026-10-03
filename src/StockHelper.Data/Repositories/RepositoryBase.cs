using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;

namespace StockHelper.Data.Repositories;

/// <summary>Each operation uses a short-lived DbContext (safe for WPF and for future multi-user access).</summary>
public abstract class RepositoryBase(IDbContextFactory<StockHelperDbContext> factory)
{
    protected Task<StockHelperDbContext> CreateAsync(CancellationToken ct) => factory.CreateDbContextAsync(ct);

    protected static async Task SaveAsync(StockHelperDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }

    /// <summary>Makes EF compare against the stamp the client loaded, not the one currently in the database.</summary>
    protected static void ExpectStamp<T>(StockHelperDbContext db, T tracked, Guid clientStamp) where T : AuditableEntity =>
        db.Entry(tracked).Property(e => e.ConcurrencyStamp).OriginalValue = clientStamp;

    protected static T Required<T>(T? entity) where T : class =>
        entity ?? throw new DomainException(DomainErrorCode.NotFound, typeof(T).Name);

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
