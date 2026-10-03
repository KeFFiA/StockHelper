using StockHelper.App.Resources;
using StockHelper.Core.Errors;

namespace StockHelper.App.Infrastructure;

public static class ErrorMessages
{
    /// <summary>Resolves <c>DomainError_{Code}</c> from Strings.resx.</summary>
    public static string For(DomainException ex) =>
        Strings.ResourceManager.GetString($"DomainError_{ex.Code}", Strings.Culture) ?? Strings.Error_Unexpected;
}
