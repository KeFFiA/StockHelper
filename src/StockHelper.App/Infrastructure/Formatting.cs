using StockHelper.Core.Entities;

namespace StockHelper.App.Infrastructure;

public static class Formatting
{
    /// <summary>"2 × Банка 10 л" for movements entered in a package unit, otherwise null.</summary>
    public static string? Entered(IEnteredInUnit movement) =>
        movement is Issue { OpenPackageId: not null } ? Resources.Strings.Issues_FromOpenPackage
        : movement.UnitId is null || movement.Unit is null || movement.UnitQuantity is null
            ? null
            : $"{NumberInput.Format(movement.UnitQuantity.Value)} × {movement.Unit.Name}";

    /// <summary>"открытая Банка 5 л, ≈ 2 л" / "открытая упаковка, ≈ 2 л".</summary>
    public static string Package(OpenPackage package) =>
        string.Format(Resources.Strings.Issues_PackageFormat,
            package.Unit?.Name ?? Resources.Strings.Issues_PackageGeneric,
            NumberInput.Format(package.Quantity),
            package.Item?.Unit?.Name);
}
