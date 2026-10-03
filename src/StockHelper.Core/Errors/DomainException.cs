namespace StockHelper.Core.Errors;

/// <summary>
/// Business rule violations. Core carries only a code; the UI maps it to a localized message.
/// </summary>
public enum DomainErrorCode
{
    NameRequired,
    NameNotUnique,
    LoginNotUnique,
    InvalidCredentials,
    UserInactive,
    PasswordTooShort,
    QuantityMustBePositive,
    QuantityCannotBeNegative,
    PriceCannotBeNegative,
    NotFound,
    InUse,
    StockTakeCompleted,
    StockTakeNotCompleted,
    DraftStockTakeExists,
    StockTakeDateConflict,
    AccessDenied,
    CannotDeactivateSelf,
    LastAdministrator,
    ArchivedReference,
    ReturnExceedsIssued,
    AlreadyReturned,
    IncompatibleUnit,
    InvalidUnitFactor,
    PackageOfPackage,
    OpenPackageUnavailable,
    OpenPackageAlreadyIssued,
}

public class DomainException(DomainErrorCode code, string? details = null)
    : Exception(details is null ? code.ToString() : $"{code}: {details}")
{
    public DomainErrorCode Code { get; } = code;

    public string? Details { get; } = details;
}

/// <summary>Raised when the record was changed by someone else since it was loaded.</summary>
public sealed class ConcurrencyConflictException(Exception? inner = null)
    : Exception("The record was modified by another user.", inner);
