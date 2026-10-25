using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Undo;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Undo;

public sealed class FileUndoJournalTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly MemoryLog _log = new();

    public void Dispose() => _temp.Dispose();

    private string JournalPath => _temp.Combine("data", "undo-history.json");

    private FileUndoJournal Create() => new(JournalPath, _log);

    private static UndoEntry RemoteDesktopTurnedOff() => new()
    {
        Id = Guid.NewGuid(),
        When = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero),
        Kind = UndoKind.RegistryValueChanged,
        DisplayName = "Turned off Remote Desktop",
        Data = new Dictionary<string, string>
        {
            ["key"] = @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server",
            ["name"] = "fDenyTSConnections",
            ["previousValue"] = "0",
        },
        CanUndoInApp = true,
    };

    private static UndoEntry ChromePolicyRemoved(string backupFile) => new()
    {
        Id = Guid.NewGuid(),
        When = new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero),
        Kind = UndoKind.RegistryKeyRemoved,
        DisplayName = "Removed the \"Managed by your organisation\" settings from Chrome",
        Data = new Dictionary<string, string> { ["key"] = @"HKLM\SOFTWARE\Policies\Google\Chrome" },
        BackupFile = backupFile,
        CanUndoInApp = true,
    };

    [Fact]
    public void Starts_empty_when_there_is_no_file()
    {
        Assert.Empty(Create().GetAll());
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void Entries_survive_closing_and_reopening_the_app()
    {
        var rdp = RemoteDesktopTurnedOff();
        var chrome = ChromePolicyRemoved(_temp.Combine("backups", "chrome-policy.json"));
        var journal = Create();
        journal.Add(rdp);
        journal.Add(chrome);

        var reopened = Create().GetAll();

        Assert.Equal(2, reopened.Count);
        Assert.Equal(rdp.Id, reopened[0].Id);
        Assert.Equal(rdp.When, reopened[0].When);
        Assert.Equal(UndoKind.RegistryValueChanged, reopened[0].Kind);
        Assert.Equal("0", reopened[0].Data["previousValue"]);
        Assert.True(reopened[0].CanUndoInApp);
        Assert.Null(reopened[0].BackupFile);
        Assert.Equal(UndoKind.RegistryKeyRemoved, reopened[1].Kind);
        Assert.Equal(chrome.BackupFile, reopened[1].BackupFile);
    }

    [Fact]
    public void Previous_values_are_kept_exactly_including_awkward_characters()
    {
        var entry = RemoteDesktopTurnedOff() with
        {
            Data = new Dictionary<string, string> { ["previousValue"] = "C:\\Path with spaces\\\"quoted\" & <angle> é" },
        };
        Create().Add(entry);

        Assert.Equal(entry.Data["previousValue"], Create().GetAll()[0].Data["previousValue"]);
    }

    [Fact]
    public void The_file_is_readable_with_named_kinds()
    {
        Create().Add(RemoteDesktopTurnedOff());

        var json = File.ReadAllText(JournalPath);

        Assert.Contains("\"registryValueChanged\"", json);
        Assert.Contains("\"displayName\": \"Turned off Remote Desktop\"", json);
        Assert.DoesNotContain("isUndone", json);
    }

    [Fact]
    public void Marking_undone_is_saved()
    {
        var entry = RemoteDesktopTurnedOff();
        var journal = Create();
        journal.Add(entry);
        var when = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

        Assert.True(journal.MarkUndone(entry.Id, when));

        var saved = Assert.Single(Create().GetAll());
        Assert.Equal(when, saved.UndoneAt);
        Assert.True(saved.IsUndone);
    }

    [Fact]
    public void New_entries_are_not_undone()
    {
        Assert.False(RemoteDesktopTurnedOff().IsUndone);
    }

    [Fact]
    public void Marking_a_change_as_not_made_is_saved()
    {
        var entry = RemoteDesktopTurnedOff();
        var journal = Create();
        journal.Add(entry);

        Assert.True(journal.MarkNotMade(entry.Id));

        Assert.True(Assert.Single(Create().GetAll()).NotMade);
        Assert.False(Create().MarkNotMade(Guid.NewGuid()));
    }

    [Fact]
    public void Marking_an_unknown_entry_undone_returns_false()
    {
        Assert.False(Create().MarkUndone(Guid.NewGuid(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void No_temporary_file_is_left_behind()
    {
        Create().Add(RemoteDesktopTurnedOff());

        Assert.False(File.Exists(JournalPath + ".tmp"));
    }

    [Fact]
    public void If_the_record_cannot_be_saved_Add_fails_so_the_change_does_not_go_ahead()
    {
        // A file where the data folder should be makes saving impossible.
        var blocked = _temp.Combine("blocked");
        File.WriteAllText(blocked, "not a folder");
        var journal = new FileUndoJournal(Path.Combine(blocked, "undo-history.json"), _log);

        Assert.ThrowsAny<IOException>(() => journal.Add(RemoteDesktopTurnedOff()));
    }

    [Fact]
    public void A_damaged_file_is_kept_aside_and_a_fresh_history_started()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        File.WriteAllText(JournalPath, "{ this is not valid json");
        var journal = Create();

        Assert.Empty(journal.GetAll());
        journal.Add(RemoteDesktopTurnedOff());

        Assert.Single(Create().GetAll());
        var backups = Directory.GetFiles(Path.GetDirectoryName(JournalPath)!, "undo-history.json.damaged-*");
        Assert.Equal("{ this is not valid json", File.ReadAllText(Assert.Single(backups)));
        Assert.True(_log.Has(LogLevel.Warning, "damaged"));
    }

    [Fact]
    public void GetAll_returns_a_copy_that_cannot_change_the_journal()
    {
        var journal = Create();
        journal.Add(RemoteDesktopTurnedOff());

        var first = journal.GetAll();
        journal.Add(RemoteDesktopTurnedOff());

        Assert.Single(first);
    }
}
