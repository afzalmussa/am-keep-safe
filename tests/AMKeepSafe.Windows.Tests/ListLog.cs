using AMKeepSafe.Core.Logging;

namespace AMKeepSafe.Windows.Tests;

internal sealed class ListLog : IAppLog
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public string LogDirectory => "";

    public string CurrentLogFile => "";

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        lock (Entries)
        {
            Entries.Add((level, message));
        }
    }
}
