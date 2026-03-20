using Microsoft.Extensions.Logging;
using Xunit.Abstractions; 

namespace GatewayGuard.Tests.Providers;


public class PrefixLoggerProvider : ILoggerProvider
{
    private readonly string _prefix;
    private readonly ITestOutputHelper _output;

    public PrefixLoggerProvider(string prefix, ITestOutputHelper output)
    {
        _prefix = prefix;
        _output = output;
    }

    public ILogger CreateLogger(string categoryName) => new PrefixLogger(_prefix, categoryName, _output);
    public void Dispose() { }

    private class PrefixLogger : ILogger
    {
        private readonly string _prefix;
        private readonly string _category;
        private readonly ITestOutputHelper _output;

        public PrefixLogger(string prefix, string category, ITestOutputHelper output)
        {
            _prefix = prefix;
            _category = category;
            _output = output;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // Use xUnit's output instead of Console!
            _output.WriteLine($"[{_prefix}] {_category}: {formatter(state, exception)}");
        }
    }
}