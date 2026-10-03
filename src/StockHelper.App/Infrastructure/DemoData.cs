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
    IIssueRepository issues,
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

        // Package units: paint is counted in litres but bought and handed out in cans.
        var liter = unitByName.TryGetValue("л", out var l) ? l : await units.AddAsync(new Unit { Name = "л" });
        unitByName["л"] = liter;
        var can5 = await units.AddAsync(new Unit { Name = "Банка 5 л", BaseUnitId = liter.Id, Factor = 5 });
        var can10 = await units.AddAsync(new Unit { Name = "Банка 10 л", BaseUnitId = liter.Id, Factor = 10 });
        string[] painters = ["Иванов, малярный участок", "Петров, ремонтная бригада", "Сидорова, хозчасть"];

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
                var isPaint = item.Name.StartsWith("Краска");
                var received = sample.Restock && (month > 1 || isPaint) && stock[item.Id] - used < sample.MinStock * 2 ? Math.Ceiling(sample.DailyUse * 30) : 0;
                if (isPaint && received > 0)
                {
                    received = 10 * Math.Ceiling(received / 10);
                }

                if (received > 0)
                {
                    await receipts.AddAsync(new Receipt
                    {
                        ItemId = item.Id,
                        Quantity = received,
                        Price = sample.Price,
                        UnitId = isPaint ? can10.Id : null,
                        UnitQuantity = isPaint ? received / 10 : null,
                        Date = countDate.AddDays(10 + random.Next(10)),
                        StorageLocationId = warehouse.Id,
                    });
                }

                if (isPaint)
                {
                    // Cans of 5 l handed out; what is left in the can comes back.
                    var net = 0m;
                    var day = 1;
                    while (net + 2 < used && day < 27)
                    {
                        // An opened can goes first and is used up.
                        if ((await issues.GetOpenPackagesAsync(item.Id)).FirstOrDefault() is { } open)
                        {
                            await issues.AddAsync(new Issue
                            {
                                ItemId = item.Id,
                                Quantity = open.Quantity,
                                OpenPackageId = open.Id,
                                Date = countDate.AddDays(day).AddHours(1),
                                IssuedTo = painters[random.Next(painters.Length)],
                            });
                            net += open.Quantity;
                            day += 3;
                            continue;
                        }

                        var issue = await issues.AddAsync(new Issue
                        {
                            ItemId = item.Id,
                            Quantity = 5,
                            UnitId = can5.Id,
                            UnitQuantity = 1,
                            Date = countDate.AddDays(day).AddHours(1),
                            IssuedTo = painters[random.Next(painters.Length)],
                            ExpectReturn = true,
                            StorageLocationId = warehouse.Id,
                        });
                        var back = Math.Round(0.5m + (decimal)(random.NextDouble() * 2.5), 1);
                        await issues.ReturnAsync(issue.Id, back, countDate.AddDays(day + 2).AddHours(8));
                        net += 5 - back;
                        day += 5 + random.Next(4);
                    }

                    used = net + 0.5m;   // a little goes missing: shown as unaccounted
                    if (month == 1)
                    {
                        await issues.AddAsync(new Issue
                        {
                            ItemId = item.Id,
                            Quantity = 5,
                            UnitId = can5.Id,
                            UnitQuantity = 1,
                            Date = now.AddDays(-2),
                            IssuedTo = painters[0],
                            ExpectReturn = true,
                            Note = "Покраска двери склада",
                        });
                        used += 5;
                    }
                }
                else if (item.Name.StartsWith("Перчатки"))
                {
                    var pairs = Math.Round(used / 10) * 10;
                    for (var week = 0; week < 4 && pairs > 0; week++)
                    {
                        var batch = week == 3 ? pairs : Math.Min(pairs, 10 * Math.Ceiling(pairs / 40));
                        await issues.AddAsync(new Issue
                        {
                            ItemId = item.Id,
                            Quantity = batch,
                            Date = countDate.AddDays(2 + week * 7),
                            IssuedTo = painters[week % painters.Length],
                        });
                        pairs -= batch;
                    }
                }

                stock[item.Id] = Math.Max(0, stock[item.Id] - used + received);
            }
        }
    }
}
