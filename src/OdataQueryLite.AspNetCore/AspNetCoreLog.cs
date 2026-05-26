using Microsoft.Extensions.Logging;

namespace OdataQueryLite.AspNetCore
{
    // LoggerMessage source-generator targets. Avoids CA1873 (eager argument evaluation when
    // logging is disabled) — the generated code checks ILogger.IsEnabled before reading the
    // format args and skips the call entirely when filtered out.
    internal static partial class AspNetCoreLog
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Binding OdataQueryOptions<{EntityType}> from {Query}")]
        public static partial void BindingOptions(ILogger logger, string entityType, string? query);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Binding OdataQueryRequest<{EntityType}> from {Query}")]
        public static partial void BindingRequest(ILogger logger, string entityType, string? query);
    }
}
