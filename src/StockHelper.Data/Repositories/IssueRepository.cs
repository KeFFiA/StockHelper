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

        // Handing out an opened package takes it off the shelf.
        if (issue.OpenPackageId is { } packageId)
        {
            var package = Required(await db.OpenPackages.FindAsync([packageId], ct));
            if (!package.IsOpen || package.ItemId != issue.ItemId)
            {
                throw new DomainException(DomainErrorCode.OpenPackageUnavailable);
            }

            package.ClosedAt = entity.Date;
            entity.OpenPackageId = packageId;
        }

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

        // Whatever came back is an opened package, offered first next time.
        if (returnedQuantity > 0)
        {
            var unitId = issue.UnitId;
            if (unitId is null && issue.OpenPackageId is { } previous)
            {
                unitId = (await db.OpenPackages.FindAsync([previous], ct))?.UnitId;
            }

            db.OpenPackages.Add(new OpenPackage
            {
                ItemId = issue.ItemId,
                UnitId = unitId,
                Quantity = returnedQuantity,
                SourceIssue = issue,
                OpenedAt = issue.ReturnedAt.Value,
            });
        }

        await SaveAsync(db, ct);
    }

    public async Task CancelReturnAsync(int issueId, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var issue = Required(await db.Issues.FindAsync([issueId], ct));
        await RemovePackageFromAsync(db, issueId, ct);
        issue.ReturnedQuantity = null;
        issue.ReturnedAt = null;
        issue.ReturnedBy = null;
        await SaveAsync(db, ct);
    }

    public async Task<IReadOnlyList<OpenPackage>> GetOpenPackagesAsync(int? itemId = null, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var query = db.OpenPackages.AsNoTracking()
            .Include(p => p.Unit)
            .Include(p => p.Item).ThenInclude(i => i!.Unit)
            .Include(p => p.SourceIssue)
            .Where(p => p.ClosedAt == null);
        if (itemId is { } id)
        {
            query = query.Where(p => p.ItemId == id);
        }

        return await query.OrderBy(p => p.OpenedAt).ToListAsync(ct);
    }

    public async Task WriteOffAsync(int openPackageId, string? note, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var package = Required(await db.OpenPackages.FindAsync([openPackageId], ct));
        if (!package.IsOpen)
        {
            throw new DomainException(DomainErrorCode.OpenPackageUnavailable);
        }

        var now = DateTime.UtcNow;
        package.ClosedAt = now;
        db.Issues.Add(new Issue
        {
            ItemId = package.ItemId,
            Quantity = package.Quantity,
            Date = now,
            Note = Clean(note),
            OpenPackageId = package.Id,
        });
        await SaveAsync(db, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var issue = Required(await db.Issues.FindAsync([id], ct));
        await RemovePackageFromAsync(db, id, ct);

        // The opened package this issue took goes back on the shelf.
        if (issue.OpenPackageId is { } packageId && await db.OpenPackages.FindAsync([packageId], ct) is { } taken)
        {
            taken.ClosedAt = null;
        }

        db.Issues.Remove(issue);
        await SaveAsync(db, ct);
    }

    /// <summary>Removes the opened package created by an issue's return, unless it was already handed out again.</summary>
    private static async Task RemovePackageFromAsync(StockHelperDbContext db, int issueId, CancellationToken ct)
    {
        var created = await db.OpenPackages.FirstOrDefaultAsync(p => p.SourceIssueId == issueId, ct);
        if (created is null)
        {
            return;
        }

        if (!created.IsOpen)
        {
            throw new DomainException(DomainErrorCode.OpenPackageAlreadyIssued);
        }

        db.OpenPackages.Remove(created);
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
