using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Data.Repositories;

namespace StockHelper.Data;

public enum DatabaseProvider
{
    Sqlite,
    PostgreSql,
}

/// <summary>The single place where the database provider and connection string are chosen.</summary>
public sealed record DatabaseOptions
{
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.Sqlite;

    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Directory for automatic and manual backups.</summary>
    public string BackupsDirectory { get; init; } = string.Empty;
}

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddStockHelperData(this IServiceCollection services, DatabaseOptions options)
    {
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditInterceptor>();

        services.AddDbContextFactory<StockHelperDbContext>((sp, builder) =>
        {
            Configure(builder, options);
            builder.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
        });

        services.AddSingleton<ILookupRepository<Category>, CategoryRepository>();
        services.AddSingleton<ILookupRepository<Unit>, UnitRepository>();
        services.AddSingleton<ILookupRepository<StorageLocation>, StorageLocationRepository>();
        services.AddSingleton<IItemRepository, ItemRepository>();
        services.AddSingleton<IReceiptRepository, ReceiptRepository>();
        services.AddSingleton<IIssueRepository, IssueRepository>();
        services.AddSingleton<IStockTakeRepository, StockTakeRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IRecoveryKeyRepository, RecoveryKeyRepository>();
        services.AddSingleton<IStockDataReader, StockDataReader>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IBackupService, SqliteBackupService>();

        return services;
    }

    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions options)
    {
        switch (options.Provider)
        {
            case DatabaseProvider.Sqlite:
                builder.UseSqlite(options.ConnectionString);
                break;

            case DatabaseProvider.PostgreSql:
                // Multi-user mode: add Npgsql.EntityFrameworkCore.PostgreSQL, call UseNpgsql here and add
                // a PostgreSQL migrations set. Repositories and services stay unchanged.
                throw new NotSupportedException("PostgreSQL provider is not enabled in this build.");

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Provider, null);
        }
    }
}
