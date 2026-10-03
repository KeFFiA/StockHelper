using Microsoft.Extensions.Logging;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.App.Infrastructure;

/// <summary>
/// Fills an empty database with realistic sample data (started with <c>--demo</c>).
/// Used to try the app and to check reports; never runs when items already exist.
/// </summary>
public sealed class DemoData(
    IItemRepository items,
    ILookupRepository<Category> categories,
    ILookupRepository<Unit> units,
    ILookupRepository<StorageLocation> locations,
    IReceiptRepository receipts,
    IStockTakeRepository stockTakes,
    ILogger<DemoData> logger)
{
    private sealed record Sample(string Name, string Category, string Unit, decimal Price, decimal MinStock, decimal DailyUse, decimal Start, bool Restock = true);

    private static readonly Sample[] Samples =
    [
        new("Бумага А4, 500 л.", "Канцелярия", "пачка", 420m, 10, 1.2m, 40),
        new("Ручка шариковая синяя", "Канцелярия", "шт", 25m, 20, 0.8m, 60),
        new("Стикеры 76×76", "Канцелярия", "уп", 95m, 5, 0.15m, 18, Restock: false),
        new("Скрепки 28 мм", "Канцелярия", "уп", 60m, 3, 0.05m, 8),
        new("Тонер-картридж HP 85A", "Расходники для принтеров", "шт", 3900m, 2, 0.1m, 9, Restock: false),
        new("Барабан Brother DR-2335", "Расходники для принтеров", "шт", 4600m, 1, 0.02m, 2),
        new("Краска белая", "Хозяйственные товары", "л", 540m, 5, 0.3m, 25),
        new("Перчатки нитриловые", "Хозяйственные товары", "пара", 18m, 50, 4m, 300),
        new("Мешки для мусора 120 л", "Хозяйственные товары", "рулон", 260m, 4, 0.25m, 14),
        new("Средство для мытья пола", "Хозяйственные товары", "л", 310m, 3, 0.12m, 11, Restock: false),
    ];

    public async Task SeedAsync()
    {
        if ((await items.GetAllAsync(includeArchived: true)).Count > 0)
        {
            logger.LogInformation("Demo data skipped: the database already has items");
            return;
        }

        logger.LogInformation("Seeding demo data");
        var random = new Random(42);
        var categoryByName = (await categories.GetAllAsync(true)).ToDictionary(c => c.Name);
        var unitByName = (await units.GetAllAsync(true)).ToDictionary(u => u.Name);
        var warehouse = (await locations.GetAllAsync(false)).First();
        var shelf = await locations.AddAsync(new StorageLocation { Name = "Полка у принтера", Description = "Кабинет 204" });

        var created = new List<(Item Item, Sample Sample)>();
        foreach (var sample in Samples)
        {
            var category = categoryByName.TryGetValue(sample.Category, out var c) ? c : await categories.AddAsync(new Category { Name = sample.Category });
            categoryByName[sample.Category] = category;
            var unit = unitByName.TryGetValue(sample.Unit, out var u) ? u : await units.AddAsync(new Unit { Name = sample.Unit });
            unitByName[sample.Unit] = unit;

            var item = await items.AddAsync(new Item
            {
                Name = sample.Name,
                CategoryId = category.Id,
                UnitId = unit.Id,
                Price = sample.Price,
                MinStock = sample.MinStock,
            });
            created.Add((item, sample));
        }

        // Four monthly stock-takes; receipts in between; simulated consumption with noise.
        var now = DateTime.UtcNow;
        var stock = created.ToDictionary(c => c.Item.Id, c => c.Sample.Start);
        for (var month = 4; month >= 1; month--)
        {
            var countDate = now.AddDays(-month * 30 + 2).Date.AddHours(7);
            var draft = await stockTakes.CreateDraftAsync(countDate, month == 4 ? "Начальные остатки" : "Плановая");
            foreach (var (item, sample) in created)
            {
                var total = Math.Round(stock[item.Id], 0);
                var onShelf = item.Name.Contains("Тонер") || item.Name.Contains("Бумага") ? Math.Min(total, 2) : 0;
                await stockTakes.SetLineAsync(draft.Id, item.Id, warehouse.Id, total - onShelf);
                if (onShelf > 0)
                {
                    await stockTakes.SetLineAsync(draft.Id, item.Id, shelf.Id, onShelf);
                }

                stock[item.Id] = total;
            }

            await stockTakes.CompleteAsync(draft.Id);

            foreach (var (item, sample) in created)
            {
                var used = sample.DailyUse * 30 * (decimal)(0.7 + random.NextDouble() * 0.6);
                var received = sample.Restock && month > 1 && stock[item.Id] - used < sample.MinStock * 2 ? Math.Ceiling(sample.DailyUse * 30) : 0;
                if (received > 0)
                {
                    await receipts.AddAsync(new Receipt
                    {
                        ItemId = item.Id,
                        Quantity = received,
                        Price = sample.Price,
                        Date = countDate.AddDays(10 + random.Next(10)),
                        StorageLocationId = warehouse.Id,
                    });
                }

                stock[item.Id] = Math.Max(0, stock[item.Id] - used + received);
            }
        }
    }
}
