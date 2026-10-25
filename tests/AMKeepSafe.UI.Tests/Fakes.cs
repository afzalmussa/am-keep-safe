using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Undo;
using AMKeepSafe.ViewModels;
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

    public bool SettingsWork { get; set; } = true;

    public List<WindowsSettingsPage> OpenedSettings { get; } = [];

    public bool OpenSettings(WindowsSettingsPage page)
    {
        OpenedSettings.Add(page);
        return SettingsWork;
    }

    public List<(Browser Browser, string Address)> OpenedBrowserPages { get; } = [];

    public bool OpenBrowserPage(Browser browser, string address)
    {
        OpenedBrowserPages.Add((browser, address));
        return true;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public List<(string Title, string Message)> Messages { get; } = [];

    public List<FixPreview> Previews { get; } = [];

    public List<UndoViewModel> UndoShown { get; } = [];

    /// <summary>What the person clicks in the preview: true for the confirm button.</summary>
    public bool ConfirmAnswer { get; set; } = true;

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public bool ConfirmPreview(FixPreview preview)
    {
        Previews.Add(preview);
        if (ConfirmAnswer)
        {
            preview.Confirm();
        }

        return ConfirmAnswer;
    }

    public void ShowUndo(UndoViewModel undo) => UndoShown.Add(undo);

    public List<string> Questions { get; } = [];

    /// <summary>What the person answers to a yes/no question.</summary>
    public bool YesAnswer { get; set; }

    public bool Confirm(string heading, string body, string yesText, string noText)
    {
        Questions.Add(body);
        return YesAnswer;
    }
}

internal sealed class FakeBrowserProcesses : IBrowserProcesses
{
    public HashSet<Browser> Running { get; } = [];

    public List<Browser> CloseRequests { get; } = [];

    public bool Closes { get; set; } = true;

    public bool IsRunning(Browser browser) => Running.Contains(browser);

    public Task<bool> CloseAsync(Browser browser, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        CloseRequests.Add(browser);
        if (Closes)
        {
            Running.Remove(browser);
        }

        return Task.FromResult(Closes);
    }
}

/// <summary>Windows settings held in memory.</summary>
internal sealed class FakeChanger : ISystemChanger
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public List<SettingChange> Applied { get; } = [];

    private static string Identity(SettingChange c) => $"{c.Kind}|{c.Root}|{c.RegistryKey}|{c.Target}";

    public FakeChanger Set(ChangeKind kind, string target, string? value, AMKeepSafe.Core.Platform.RegistryRoot? root = null, string? key = null)
    {
        _values[$"{kind}|{root}|{key}|{target}"] = value;
        return this;
    }

    public string? Get(ChangeKind kind, string target, AMKeepSafe.Core.Platform.RegistryRoot? root = null, string? key = null) =>
        _values.GetValueOrDefault($"{kind}|{root}|{key}|{target}");

    public Task<string?> ReadAsync(SettingChange change, CancellationToken cancellationToken) =>
        Task.FromResult(change.Kind == ChangeKind.Task ? null : _values.GetValueOrDefault(Identity(change)));

    public List<string> Backups { get; } = [];

    public Task<string> BackupAsync(SettingChange change, string folder, CancellationToken cancellationToken)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"amkeepsafe-ui-backup-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, Identity(change));
        Backups.Add(path);
        return Task.FromResult(path);
    }

    /// <summary>Called after each change, so a test can make the rest of its fake world match.</summary>
    public Action<SettingChange>? AfterApply { get; set; }

    public Task ApplyAsync(SettingChange change, CancellationToken cancellationToken)
    {
        Applied.Add(change);
        AfterApply?.Invoke(change);
        if (change.Kind != ChangeKind.Task)
        {
            _values[Identity(change)] = change.To;
        }

        return Task.CompletedTask;
    }
}

internal sealed class MemoryJournal : IUndoJournal
{
    public List<UndoEntry> Entries { get; } = [];

    public IReadOnlyList<UndoEntry> GetAll() => Entries.ToList();

    public void Add(UndoEntry entry) => Entries.Add(entry);

    public bool MarkUndone(Guid id, DateTimeOffset when) => Update(id, e => e with { UndoneAt = when });

    public bool MarkNotMade(Guid id) => Update(id, e => e with { NotMade = true });

    private bool Update(Guid id, Func<UndoEntry, UndoEntry> change)
    {
        var index = Entries.FindIndex(e => e.Id == id);
        if (index < 0)
        {
            return false;
        }

        Entries[index] = change(Entries[index]);
        return true;
    }
}
