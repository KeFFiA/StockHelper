using StockHelper.Core.Entities;

namespace StockHelper.App.Infrastructure;

public static class Formatting
{
    /// <summary>"2 × Банка 10 л" for movements entered in a package unit, otherwise null.</summary>
    public static string? Entered(IEnteredInUnit movement) =>
        movement.UnitId is null || movement.Unit is null || movement.UnitQuantity is null
            ? null
            : $"{NumberInput.Format(movement.UnitQuantity.Value)} × {movement.Unit.Name}";
}
