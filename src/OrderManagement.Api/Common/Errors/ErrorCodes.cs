namespace OrderManagement.Api.Common.Errors;

/// <summary>Stable machine-readable codes returned in the <c>errorCode</c> field of every error response.</summary>
public static class ErrorCodes
{
    // 400
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string IdempotencyKeyMissing = "IDEMPOTENCY_KEY_MISSING";
    public const string IdempotencyKeyInvalid = "IDEMPOTENCY_KEY_INVALID";
    public const string BadRequest = "BAD_REQUEST";

    // 404
    public const string NotFound = "NOT_FOUND";
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";

    // 409
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string ConcurrentModification = "CONCURRENT_MODIFICATION";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string IdempotencyRequestInProgress = "IDEMPOTENCY_REQUEST_IN_PROGRESS";
    public const string Conflict = "CONFLICT";

    // 422
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string OrderNotCancellable = "ORDER_NOT_CANCELLABLE";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string UnknownProduct = "UNKNOWN_PRODUCT";
    public const string ProductInactive = "PRODUCT_INACTIVE";

    // 5xx
    public const string InternalError = "INTERNAL_ERROR";

    public static string FromStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ValidationFailed,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status409Conflict => Conflict,
        >= 500 => InternalError,
        _ => $"HTTP_{statusCode}"
    };
}
