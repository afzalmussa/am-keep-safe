using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Undo;

namespace AMKeepSafe.Tests.Support;

/// <summary>Windows settings held in memory. Records every change made, in order.</summary>
public sealed class FakeChanger : ISystemChanger
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public List<SettingChange> Applied { get; } = [];

    /// <summary>Makes ApplyAsync throw, as when Windows refuses.</summary>
    public bool Refuse { get; set; }

    /// <summary>Makes ApplyAsync quietly do nothing, as when Tamper Protection ignores a change.</summary>
    public bool Ignore { get; set; }

    /// <summary>Makes BackupAsync throw, as when the backup folder can't be written.</summary>
    public bool FailBackup { get; set; }

    public List<string> Backups { get; } = [];

    /// <summary>Called just before each change is made, to look at the world at that moment.</summary>
    public Action<SettingChange>? BeforeApply { get; set; }

    public static string Identity(SettingChange c) => $"{c.Kind}|{c.Root}|{c.RegistryKey}|{c.Target}";

    public FakeChanger Set(SettingChange change, string? value)
    {
        _values[Identity(change)] = value;
        return this;
    }

    public string? Get(SettingChange change) => _values.GetValueOrDefault(Identity(change));

    public Task<string?> ReadAsync(SettingChange change, CancellationToken cancellationToken) =>
        Task.FromResult(change.Kind == ChangeKind.Task ? null : _values.GetValueOrDefault(Identity(change)));

    public Task<string> BackupAsync(SettingChange change, string folder, CancellationToken cancellationToken)
    {
        if (FailBackup)
        {
            throw new IOException("Backup folder is full.");
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"backup-{Backups.Count + 1}.json");
        File.WriteAllText(path, Identity(change));
        Backups.Add(path);
        return Task.FromResult(path);
    }

    public Task ApplyAsync(SettingChange change, CancellationToken cancellationToken)
    {
        BeforeApply?.Invoke(change);
        if (change.To == SettingChange.Present && change.Kind is ChangeKind.RegistryKey or ChangeKind.File
            && (change.BackupFile is null || !File.Exists(change.BackupFile)))
        {
            throw new FileNotFoundException("No backup to restore from.");
        }

        if (Refuse)
        {
            throw new InvalidOperationException("Windows refused.");
        }

        Applied.Add(change);
        if (!Ignore && change.Kind != ChangeKind.Task)
        {
            _values[Identity(change)] = change.To;
        }

        return Task.CompletedTask;
    }
}

/// <summary>An undo journal in memory, which can be told to fail.</summary>
public sealed class MemoryJournal : IUndoJournal
{
    public List<UndoEntry> Entries { get; } = [];

    public bool FailToSave { get; set; }

    public IReadOnlyList<UndoEntry> GetAll() => Entries.ToList();

    public void Add(UndoEntry entry)
    {
        if (FailToSave)
        {
            throw new IOException("Disk full.");
        }

        Entries.Add(entry);
    }

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
