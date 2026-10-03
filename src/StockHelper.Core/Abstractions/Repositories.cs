using StockHelper.Core.Entities;

namespace StockHelper.Core.Abstractions;

/// <summary>
/// Data access contracts. Implementations live in StockHelper.Data (EF Core). Returned entities are detached:
/// pass them back to Update* with their original <see cref="AuditableEntity.ConcurrencyStamp"/>.
/// Violations throw <see cref="Errors.DomainException"/>; stale updates throw <see cref="Errors.ConcurrencyConflictException"/>.
/// </summary>
public interface ILookupRepository<T> where T : LookupEntity
{
    Task<IReadOnlyList<T>> GetAllAsync(bool includeArchived, CancellationToken ct = default);

    Task<T> AddAsync(T entity, CancellationToken ct = default);

    Task<T> UpdateAsync(T entity, CancellationToken ct = default);

    Task SetArchivedAsync(int id, bool archived, CancellationToken ct = default);

    /// <summary>Physically deletes an entry that is not referenced anywhere; otherwise throws InUse.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    Task<bool> IsInUseAsync(int id, CancellationToken ct = default);
}

public interface IItemRepository
{
    /// <summary>Items with Category and Unit loaded.</summary>
    Task<IReadOnlyList<Item>> GetAllAsync(bool includeArchived, CancellationToken ct = default);

    Task<Item?> GetAsync(int id, CancellationToken ct = default);

    Task<Item> AddAsync(Item item, CancellationToken ct = default);

    Task<Item> UpdateAsync(Item item, CancellationToken ct = default);

    Task SetArchivedAsync(int id, bool archived, CancellationToken ct = default);

    /// <summary>Items with history (receipts or stock-take lines) cannot be deleted, only archived.</summary>
    Task<bool> HasHistoryAsync(int id, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed record ReceiptFilter(DateTime? FromUtc = null, DateTime? ToUtc = null, int? ItemId = null);

public interface IReceiptRepository
{
    /// <summary>Receipts with Item (+Unit) and StorageLocation loaded, newest first.</summary>
    Task<IReadOnlyList<Receipt>> GetAsync(ReceiptFilter filter, CancellationToken ct = default);

    Task<Receipt> AddAsync(Receipt receipt, CancellationToken ct = default);

    Task<Receipt> UpdateAsync(Receipt receipt, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed record IssueFilter(DateTime? FromUtc = null, DateTime? ToUtc = null, bool OnlyOnHand = false);

public interface IIssueRepository
{
    /// <summary>Issues with Item (+Unit), entered Unit and StorageLocation loaded, newest first. On-hand issues are always included.</summary>
    Task<IReadOnlyList<Issue>> GetAsync(IssueFilter filter, CancellationToken ct = default);

    /// <summary>Distinct recipients used before, for autocompletion.</summary>
    Task<IReadOnlyList<string>> GetRecipientsAsync(CancellationToken ct = default);

    Task<Issue> AddAsync(Issue issue, CancellationToken ct = default);

    Task<Issue> UpdateAsync(Issue issue, CancellationToken ct = default);

    /// <summary>Registers the return of the remainder (in item units, 0..issued).</summary>
    Task ReturnAsync(int issueId, decimal returnedQuantity, DateTime returnedAtUtc, CancellationToken ct = default);

    /// <summary>Cancels a registered return.</summary>
    Task CancelReturnAsync(int issueId, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed record StockTakeSummary(
    int Id,
    DateTime Date,
    StockTakeStatus Status,
    string? Note,
    int CountedItems,
    DateTime? CompletedAt,
    string? CompletedBy,
    string CreatedBy,
    Guid ConcurrencyStamp);

public interface IStockTakeRepository
{
    Task<IReadOnlyList<StockTakeSummary>> GetSummariesAsync(CancellationToken ct = default);

    /// <summary>Stock-take with all lines (Item, StorageLocation not loaded).</summary>
    Task<StockTake?> GetWithLinesAsync(int id, CancellationToken ct = default);

    Task<StockTake?> GetDraftAsync(CancellationToken ct = default);

    /// <summary>Only one draft may exist at a time.</summary>
    Task<StockTake> CreateDraftAsync(DateTime dateUtc, string? note, CancellationToken ct = default);

    Task UpdateHeaderAsync(int id, DateTime dateUtc, string? note, CancellationToken ct = default);

    /// <summary>Sets the counted quantity for (item, location). Null removes the line. Draft only.</summary>
    Task SetLineAsync(int stockTakeId, int itemId, int storageLocationId, decimal? quantity, CancellationToken ct = default);

    Task CompleteAsync(int id, CancellationToken ct = default);

    Task ReopenAsync(int id, CancellationToken ct = default);

    /// <summary>Deletes a draft stock-take with its lines.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);

    Task<User?> FindByLoginAsync(string login, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);

    Task<User> AddAsync(User user, CancellationToken ct = default);

    Task<User> UpdateAsync(User user, CancellationToken ct = default);
}

/// <summary>Read-only snapshot used by analytics (all calculations happen in memory in Core).</summary>
public interface IStockDataReader
{
    Task<StockSnapshot> LoadAsync(CancellationToken ct = default);
}

public sealed record StockSnapshot(
    IReadOnlyList<Item> Items,
    IReadOnlyList<CompletedStockTake> StockTakes,
    IReadOnlyList<Receipt> Receipts,
    IReadOnlyList<Issue> Issues)
{
    public StockSnapshot(IReadOnlyList<Item> items, IReadOnlyList<CompletedStockTake> stockTakes, IReadOnlyList<Receipt> receipts)
        : this(items, stockTakes, receipts, [])
    {
    }
}

/// <summary>A completed stock-take with counted totals per item (summed over storage locations).</summary>
public sealed record CompletedStockTake(int Id, DateTime Date, IReadOnlyDictionary<int, decimal> CountedByItem);
