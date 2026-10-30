using Microsoft.Extensions.Logging;

namespace Malabak.UnitTests;

/// <summary>Records every log entry (rendered message, structured values and exception) so tests can check what reaches the logs.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    /// <summary>All entries joined, for asserting that a value appears in none of them.</summary>
    public string AllText => string.Join('\n', _entries);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
            : string.Empty;

        _entries.Add($"{logLevel}: {formatter(state, exception)} | {values} | {exception}");
    }
}
