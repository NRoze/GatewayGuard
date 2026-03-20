using Microsoft.Extensions.Logging;

namespace GatewayGuard.Logging;

internal static partial class IdempotencyMiddlewareLogMessages
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Warning, Message = "Idempotency key is missing from the request and fingerprinting failed or is disabled.")]
    public static partial void MissingIdempotencyKeyWarning(this ILogger logger);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Conflict: Request fingerprint for idempotency key '{Key}' does not match the cached response.")]
    public static partial void ConflictingIdempotencyKeyWarning(this ILogger logger, string key);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Idempotency store is unavailable. Circuit breaker or connection error occurred.")]
    public static partial void IdempotencyStoreUnavailableWarning(this ILogger logger, Exception ex);

    [LoggerMessage(EventId = 103, Level = LogLevel.Debug, Message = "Replaying cached response for idempotency key '{Key}'.")]
    public static partial void ReplayingCachedResponseDebug(this ILogger logger, string key);

    [LoggerMessage(EventId = 104, Level = LogLevel.Debug, Message = "Cache miss. Executing request for idempotency key '{Key}'.")]
    public static partial void ExecutingRequestDebug(this ILogger logger, string key);

    [LoggerMessage(EventId = 105, Level = LogLevel.Debug, Message = "Failed writing to response for idempotency key '{Key}'.")]
    public static partial void ResponseStreamAlreadyStarted(this ILogger logger, string key);
}