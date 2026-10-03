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
