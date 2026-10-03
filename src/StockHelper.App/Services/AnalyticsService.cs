using StockHelper.Core.Abstractions;
using StockHelper.Core.Services;

namespace StockHelper.App.Services;

/// <summary>Loads a data snapshot and runs the pure Core calculations with the user's settings.</summary>
public interface IAnalyticsService
{
    Task<AnalyticsResult> LoadAsync(CancellationToken ct = default);
}

public sealed record AnalyticsResult(
    StockSnapshot Snapshot,
    DateTime NowUtc,
    ForecastOptions Options,
    IReadOnlyList<ItemStockStatus> Statuses)
{
    public IReadOnlyList<ItemStockStatus> PurchaseList =>
        [.. Statuses.Where(s => s.NeedsPurchase)
            .OrderBy(s => s.DaysLeft ?? (s.IsBelowMinimum ? 0 : decimal.MaxValue))
            .ThenBy(s => s.Item.Name, StringComparer.CurrentCultureIgnoreCase)];

    public CompletedStockTake? LastStockTake => Snapshot.StockTakes.OrderBy(s => s.Date).LastOrDefault();

    public IReadOnlyList<ItemConsumption> Consumption(DateTime fromUtc, DateTime toUtc) =>
        StockCalculator.GetConsumption(Snapshot, fromUtc, toUtc);
}

public sealed class AnalyticsService(IStockDataReader reader, ISettingsService settings, TimeProvider clock) : IAnalyticsService
{
    public async Task<AnalyticsResult> LoadAsync(CancellationToken ct = default)
    {
        var snapshot = await reader.LoadAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var options = new ForecastOptions(settings.Current.PurchaseHorizonDays, settings.Current.ForecastWindowDays);
        var statuses = StockCalculator.GetStatuses(snapshot, now, options);
        return new AnalyticsResult(snapshot, now, options, statuses);
    }
}
