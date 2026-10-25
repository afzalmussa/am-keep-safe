using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Services;

namespace AMKeepSafe.UI.Tests;

internal sealed class FakeLog : IAppLog
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public string LogDirectory { get; init; } = @"C:\ProgramData\AMSoftware\KeepSafe\logs";

    public string CurrentLogFile => System.IO.Path.Combine(LogDirectory, "keepsafe-2026-10-25.log");

    public void Write(LogLevel level, string message, Exception? exception = null) => Entries.Add((level, message));
}

internal sealed class FakeShell : IShellLauncher
{
    public bool UrlsWork { get; set; } = true;
    public bool FilesWork { get; set; } = true;
    public bool FoldersWork { get; set; } = true;

    public List<string> OpenedUrls { get; } = [];
    public List<string> OpenedFiles { get; } = [];
    public List<string> OpenedFolders { get; } = [];

    public bool OpenUrl(string url)
    {
        OpenedUrls.Add(url);
        return UrlsWork;
    }

    public bool OpenFile(string path)
    {
        OpenedFiles.Add(path);
        return FilesWork;
    }

    public bool OpenFolder(string path)
    {
        OpenedFolders.Add(path);
        return FoldersWork;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public List<(string Title, string Message)> Messages { get; } = [];

    public void ShowMessage(string title, string message) => Messages.Add((title, message));
}
