using Microsoft.Extensions.Logging;

namespace Kite.Aspire;

/// <summary>
/// An <see cref="ILoggerProvider"/> that forwards log entries from the embedded
/// Kite host to the Aspire <c>ResourceLoggerService</c>.
/// </summary>
internal sealed class ResourceLoggerProvider(ILogger aspireResourceLogger) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new ResourceLogger(aspireResourceLogger);

    public void Dispose() { }

    private sealed class ResourceLogger(ILogger target) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => target.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            target.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
