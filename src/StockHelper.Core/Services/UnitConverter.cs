using StockHelper.Core.Entities;

namespace StockHelper.Core.Services;

/// <summary>Conversions between a base unit and its package units ("Банка 5 л" = 5 × "л").</summary>
public static class UnitConverter
{
    public static int RootId(Unit unit) => unit.BaseUnitId ?? unit.Id;

    /// <summary>How many root units one <paramref name="unit"/> contains.</summary>
    public static decimal FactorToRoot(Unit unit) => unit.BaseUnitId is null ? 1 : unit.Factor;

    /// <summary>Units in which an item measured in <paramref name="itemUnit"/> can be entered (same root, not archived).</summary>
    public static IReadOnlyList<Unit> CompatibleUnits(Unit itemUnit, IEnumerable<Unit> allUnits) =>
        [.. allUnits
            .Where(u => RootId(u) == RootId(itemUnit) && (!u.IsArchived || u.Id == itemUnit.Id))
            .OrderBy(u => u.Id == itemUnit.Id ? 0 : 1)
            .ThenBy(u => FactorToRoot(u))
            .ThenBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Converts a quantity entered in <paramref name="from"/> to the item's unit.</summary>
    public static decimal ToItemUnits(decimal quantity, Unit from, Unit itemUnit)
    {
        if (from.Id == itemUnit.Id)
        {
            return quantity;
        }

        if (RootId(from) != RootId(itemUnit))
        {
            throw new InvalidOperationException($"Units '{from.Name}' and '{itemUnit.Name}' are not compatible.");
        }

        return quantity * FactorToRoot(from) / FactorToRoot(itemUnit);
    }

    /// <summary>Converts a quantity in the item's unit to <paramref name="to"/>.</summary>
    public static decimal FromItemUnits(decimal quantity, Unit to, Unit itemUnit) =>
        to.Id == itemUnit.Id ? quantity : quantity * FactorToRoot(itemUnit) / FactorToRoot(to);
}
