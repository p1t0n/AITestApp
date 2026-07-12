using Microsoft.Extensions.Logging;

namespace EmployeeManager.Agents.Tests.Fakes;

/// <summary>An <see cref="ILogger"/> that collects formatted log entries so tests can assert
/// that a code path logged (e.g. a dropped rewrite's warning) without a logging framework.</summary>
internal sealed class CollectingLogger : ILogger
{
    public sealed record Entry(LogLevel Level, string Message);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add(new Entry(logLevel, formatter(state, exception)));
}
