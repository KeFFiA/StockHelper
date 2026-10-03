using StockHelper.Core.Entities;
using StockHelper.Core.Errors;

namespace StockHelper.Core.Services;

/// <summary>Entity validation shared by every data provider.</summary>
public static class Rules
{
    public static void Validate(LookupEntity entity)
    {
        RequireName(entity.Name);
    }

    public static void Validate(Item item)
    {
        RequireName(item.Name);
        if (item.MinStock < 0)
        {
            throw new DomainException(DomainErrorCode.QuantityCannotBeNegative);
        }

        if (item.Price < 0)
        {
            throw new DomainException(DomainErrorCode.PriceCannotBeNegative);
        }
    }

    public static void Validate(Receipt receipt)
    {
        if (receipt.Quantity <= 0)
        {
            throw new DomainException(DomainErrorCode.QuantityMustBePositive);
        }

        if (receipt.Price < 0)
        {
            throw new DomainException(DomainErrorCode.PriceCannotBeNegative);
        }
    }

    public static void Validate(Issue issue)
    {
        if (issue.Quantity <= 0)
        {
            throw new DomainException(DomainErrorCode.QuantityMustBePositive);
        }

        if (issue.ReturnedQuantity is { } returned && (returned < 0 || returned > issue.Quantity))
        {
            throw new DomainException(DomainErrorCode.ReturnExceedsIssued);
        }
    }

    public static void Validate(Unit unit)
    {
        RequireName(unit.Name);
        if (unit.BaseUnitId is null)
        {
            unit.Factor = 1;
            return;
        }

        if (unit.Factor <= 0)
        {
            throw new DomainException(DomainErrorCode.InvalidUnitFactor);
        }

        if (unit.BaseUnitId == unit.Id)
        {
            throw new DomainException(DomainErrorCode.PackageOfPackage);
        }
    }

    public static void ValidateCount(decimal quantity)
    {
        if (quantity < 0)
        {
            throw new DomainException(DomainErrorCode.QuantityCannotBeNegative);
        }
    }

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(DomainErrorCode.NameRequired);
        }
    }
}
