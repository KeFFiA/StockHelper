using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.Data.Repositories;

public sealed class IssueRepository(
    IDbContextFactory<StockHelperDbContext> factory,
    ICurrentUserService currentUser) : RepositoryBase(factory), IIssueRepository
{
    public async Task<IReadOnlyList<Issue>> GetAsync(IssueFilter filter, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var query = db.Issues.AsNoTracking()
            .Include(i => i.Item).ThenInclude(i => i!.Unit)
            .Include(i => i.Unit)
            .Include(i => i.StorageLocation)
            .AsQueryable();

        if (filter.OnlyOnHand)
        {
            query = query.Where(i => i.ExpectReturn && i.ReturnedAt == null);
        }
        else
        {
            // Items still on hand are always relevant, whatever the period.
            if (filter.FromUtc is { } from)
            {
                query = query.Where(i => i.Date >= from || (i.ExpectReturn && i.ReturnedAt == null) || i.ReturnedAt >= from);
            }

            if (filter.ToUtc is { } to)
            {
                query = query.Where(i => i.Date <= to);
            }
        }

        return await query.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetRecipientsAsync(CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var names = await db.Issues.AsNoTracking()
            .Where(i => i.IssuedTo != null)
            .Select(i => i.IssuedTo!)
            .Distinct()
            .ToListAsync(ct);
        return [.. names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<Issue> AddAsync(Issue issue, CancellationToken ct = default)
    {
        Rules.Validate(issue);
        await using var db = await CreateAsync(ct);
        await EnsureReferencesAsync(db, issue, ct);

        var entity = new Issue();
        Copy(issue, entity);
        db.Issues.Add(entity);
        await SaveAsync(db, ct);
        return entity;
    }

    public async Task<Issue> UpdateAsync(Issue issue, CancellationToken ct = default)
    {
        Rules.Validate(issue);
        await using var db = await CreateAsync(ct);
        await EnsureReferencesAsync(db, issue, ct);

        var tracked = Required(await db.Issues.FindAsync([issue.Id], ct));
        ExpectStamp(db, tracked, issue.ConcurrencyStamp);
        if (tracked.ReturnedQuantity is { } returned && issue.Quantity < returned)
        {
            throw new DomainException(DomainErrorCode.ReturnExceedsIssued);
        }

        Copy(issue, tracked);
        await SaveAsync(db, ct);
        return tracked;
    }

    public async Task ReturnAsync(int issueId, decimal returnedQuantity, DateTime returnedAtUtc, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var issue = Required(await db.Issues.FindAsync([issueId], ct));
        if (issue.IsReturned)
        {
            throw new DomainException(DomainErrorCode.AlreadyReturned);
        }

        if (returnedQuantity < 0 || returnedQuantity > issue.Quantity)
        {
            throw new DomainException(DomainErrorCode.ReturnExceedsIssued);
        }

        issue.ReturnedQuantity = returnedQuantity;
        issue.ReturnedAt = ToUtc(returnedAtUtc) < issue.Date ? issue.Date : ToUtc(returnedAtUtc);
        issue.ReturnedBy = currentUser.AuditName;
        await SaveAsync(db, ct);
    }

    public async Task CancelReturnAsync(int issueId, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var issue = Required(await db.Issues.FindAsync([issueId], ct));
        issue.ReturnedQuantity = null;
        issue.ReturnedAt = null;
        issue.ReturnedBy = null;
        await SaveAsync(db, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        db.Issues.Remove(Required(await db.Issues.FindAsync([id], ct)));
        await SaveAsync(db, ct);
    }

    private static void Copy(Issue source, Issue target)
    {
        target.ItemId = source.ItemId;
        target.Quantity = source.Quantity;
        target.UnitId = source.UnitId;
        target.UnitQuantity = source.UnitId is null ? null : source.UnitQuantity;
        target.Date = ToUtc(source.Date);
        target.IssuedTo = Clean(source.IssuedTo);
        target.StorageLocationId = source.StorageLocationId;
        target.Note = Clean(source.Note);
        target.ExpectReturn = source.ExpectReturn;
    }

    private static async Task EnsureReferencesAsync(StockHelperDbContext db, Issue issue, CancellationToken ct)
    {
        if (!await db.Items.AnyAsync(i => i.Id == issue.ItemId, ct))
        {
            throw new DomainException(DomainErrorCode.NotFound, "Item");
        }

        if (issue.StorageLocationId is { } locationId && !await db.StorageLocations.AnyAsync(l => l.Id == locationId, ct))
        {
            throw new DomainException(DomainErrorCode.NotFound, "Storage location");
        }
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
