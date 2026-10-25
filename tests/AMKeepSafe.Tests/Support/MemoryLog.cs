using AMKeepSafe.Core.Logging;

namespace AMKeepSafe.Tests.Support;

/// <summary>Keeps log lines in memory so tests can check what was logged.</summary>
public sealed class MemoryLog : IAppLog
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public string LogDirectory => "logs";

    public string CurrentLogFile => Path.Combine(LogDirectory, "keepsafe-2026-10-25.log");

    public void Write(LogLevel level, string message, Exception? exception = null) => Entries.Add((level, message, exception));

    public bool Has(LogLevel level, string fragment) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
