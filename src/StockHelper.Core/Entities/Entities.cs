namespace StockHelper.Core.Entities;

public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>
/// Audit fields are filled automatically on save. <see cref="ConcurrencyStamp"/> is a provider-agnostic
/// optimistic concurrency token: it changes on every update, and a stale value makes the update fail.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A simple named reference entry that can be archived (category, unit, storage location).</summary>
public abstract class LookupEntity : AuditableEntity
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            NormalizedName = Normalize(value);
        }
    }

    /// <summary>Upper-cased trimmed name for case-insensitive uniqueness that works on every provider.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public bool IsArchived { get; set; }

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}

public sealed class Category : LookupEntity;

public sealed class Unit : LookupEntity;

public sealed class StorageLocation : LookupEntity
{
    public string? Description { get; set; }
}

public sealed class Item : AuditableEntity
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            NormalizedName = LookupEntity.Normalize(value);
        }
    }

    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>Optional article / external code (e.g. for a future 1C integration).</summary>
    public string? Code { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    public int UnitId { get; set; }

    public Unit? Unit { get; set; }

    public decimal MinStock { get; set; }

    /// <summary>Current unit price.</summary>
    public decimal Price { get; set; }

    public string? Note { get; set; }

    public bool IsArchived { get; set; }
}

public sealed class Receipt : AuditableEntity
{
    public int ItemId { get; set; }

    public Item? Item { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Unit price of this purchase.</summary>
    public decimal Price { get; set; }

    /// <summary>UTC.</summary>
    public DateTime Date { get; set; }

    public int? StorageLocationId { get; set; }

    public StorageLocation? StorageLocation { get; set; }

    public string? Note { get; set; }

    public decimal Amount => Quantity * Price;
}

public enum StockTakeStatus
{
    Draft = 0,
    Completed = 1,
}

public sealed class StockTake : AuditableEntity
{
    /// <summary>UTC moment the stock was counted.</summary>
    public DateTime Date { get; set; }

    public StockTakeStatus Status { get; set; } = StockTakeStatus.Draft;

    public string? Note { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? CompletedBy { get; set; }

    public List<StockTakeLine> Lines { get; set; } = [];
}

/// <summary>Counted quantity of one item in one storage location.</summary>
public sealed class StockTakeLine : AuditableEntity
{
    public int StockTakeId { get; set; }

    public StockTake? StockTake { get; set; }

    public int ItemId { get; set; }

    public Item? Item { get; set; }

    public int StorageLocationId { get; set; }

    public StorageLocation? StorageLocation { get; set; }

    public decimal CountedQuantity { get; set; }
}

public enum UserRole
{
    Storekeeper = 0,
    Manager = 1,
    Administrator = 2,
}

public sealed class User : AuditableEntity
{
    private string _login = string.Empty;

    public string Login
    {
        get => _login;
        set
        {
            _login = value;
            NormalizedLogin = LookupEntity.Normalize(value);
        }
    }

    public string NormalizedLogin { get; private set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;
}
