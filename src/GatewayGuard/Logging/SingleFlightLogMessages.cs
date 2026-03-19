using Microsoft.Extensions.Logging;

namespace GatewayGuard.Logging;

internal static partial class SingleFlightLogMessages
{
    [LoggerMessage(EventId = 300, Level = LogLevel.Debug, Message = "Executing new single-flight request for key '{Key}'.")]
    public static partial void ExecutingNewFlightDebug(this ILogger logger, string key);

    [LoggerMessage(EventId = 301, Level = LogLevel.Debug, Message = "Waiting on existing concurrent single-flight request for key '{Key}'.")]
    public static partial void WaitingOnExistingFlightDebug(this ILogger logger, string key);
}
