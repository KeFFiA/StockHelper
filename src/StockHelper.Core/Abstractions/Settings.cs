namespace StockHelper.Core.Abstractions;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Per-installation user settings (stored in settings.json).</summary>
public sealed record AppSettings
{
    /// <summary>N: items whose forecast is shorter than this many days go to the purchase list.</summary>
    public int PurchaseHorizonDays { get; init; } = 14;

    /// <summary>History window used to compute the average daily consumption.</summary>
    public int ForecastWindowDays { get; init; } = 90;

    /// <summary>How many backup copies to keep.</summary>
    public int BackupsToKeep { get; init; } = 10;

    public AppTheme Theme { get; init; } = AppTheme.System;

    public string? LastLogin { get; init; }

    /// <summary>Version whose changelog the user has already seen.</summary>
    public string? LastSeenVersion { get; init; }

    /// <summary>Navigation pane shows icons only.</summary>
    public bool IsNavigationCollapsed { get; init; }

    /// <summary>Main window placement restored on the next start (null = default size, centered).</summary>
    public WindowPlacement? MainWindow { get; init; }
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized);

public interface ISettingsService
{
    AppSettings Current { get; }

    Task SaveAsync(AppSettings settings);

    event EventHandler? Changed;
}

/// <summary>Database backup/restore. The implementation depends on the provider (file copy for SQLite).</summary>
public interface IBackupService
{
    Task<string> CreateBackupAsync(string reason, CancellationToken ct = default);

    Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken ct = default);

    /// <summary>Replaces the current database with the backup (a safety copy of the current one is made first).</summary>
    Task RestoreAsync(string backupPath, CancellationToken ct = default);

    void Prune(int keep);
}

public sealed record BackupInfo(string Path, string FileName, DateTime CreatedAtUtc, long SizeBytes);

/// <summary>Applies migrations (after a backup) and seeds reference data on first run.</summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(SeedData seed, CancellationToken ct = default);
}

/// <summary>Localized names for the initial reference data, supplied by the UI layer.</summary>
public sealed record SeedData(IReadOnlyList<string> Units, string DefaultLocation);
