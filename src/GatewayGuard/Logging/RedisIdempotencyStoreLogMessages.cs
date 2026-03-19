using Microsoft.Extensions.Logging;

namespace GatewayGuard.Logging;

internal static partial class RedisIdempotencyStoreLogMessages
{
    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "Timed out waiting for completion of idempotency key '{Key}' after {Timeout}ms.")]
    public static partial void WaitForCompletionTimeoutWarning(this ILogger logger, string key, double timeout);

    [LoggerMessage(EventId = 201, Level = LogLevel.Debug, Message = "Successfully acquired Redis lock for idempotency key '{Key}'.")]
    public static partial void LockAcquiredDebug(this ILogger logger, string key);

    [LoggerMessage(EventId = 202, Level = LogLevel.Debug, Message = "Did not acquire Redis lock for idempotency key '{Key}' (already held by another process).")]
    public static partial void LockAcquisitionFailedDebug(this ILogger logger, string key);

    [LoggerMessage(EventId = 203, Level = LogLevel.Debug, Message = "Successfully saved captured response for idempotency key '{Key}'.")]
    public static partial void ResponseSavedDebug(this ILogger logger, string key);

    [LoggerMessage(EventId = 204, Level = LogLevel.Debug, Message = "Published completion notification for idempotency key '{Key}'.")]
    public static partial void PublishedCompletionDebug(this ILogger logger, string key);
}
