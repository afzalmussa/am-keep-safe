using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks.Rules;

public class UacCheckTests
{
    [Theory]
    [InlineData(1, 5, UacCheck.On)]
    [InlineData(1, 2, UacCheck.On)]
    [InlineData(null, null, UacCheck.On)]   // Windows' defaults when nothing is set
    [InlineData(1, 0, UacCheck.NeverNotify)]
    [InlineData(0, 5, UacCheck.Off)]
    [InlineData(0, 0, UacCheck.Off)]
    public void Reads_the_two_settings(int? enableLua, int? consent, string expected)
    {
        Assert.Equal(expected, UacCheck.Evaluate(enableLua, consent).Code);
    }

    [Fact]
    public async Task Reads_the_real_registry_location()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0);

        var outcome = await new UacCheck(registry).RunAsync(CheckCatalog.LoadEmbedded()[UacCheck.CheckId], CancellationToken.None);

        Assert.Equal(UacCheck.Off, outcome.Code);
    }
}

public class RemoteDesktopCheckTests
{
    [Theory]
    [InlineData(0, false, RemoteDesktopCheck.On)]
    [InlineData(1, false, RemoteDesktopCheck.Off)]
    [InlineData(null, false, RemoteDesktopCheck.Off)]
    [InlineData(0, true, RemoteDesktopCheck.Off)]  // Home editions can't accept connections
    public void Rules(int? deny, bool home, string expected)
    {
        Assert.Equal(expected, RemoteDesktopCheck.Evaluate(deny, home).Code);
    }

    [Theory]
    [InlineData("Core", true)]
    [InlineData("CoreSingleLanguage", true)]
    [InlineData("Professional", false)]
    [InlineData("Enterprise", false)]
    public void Knows_a_Home_edition_by_its_edition_id(string edition, bool home)
    {
        Assert.Equal(home, new WindowsVersionFacts("Windows 10 Home", edition, null, 26200).IsHomeEdition);
    }

    [Fact]
    public async Task Remote_Desktop_on_a_Pro_PC_is_flagged()
    {
        var registry = HealthyPc.Registry()
            .Set(RegistryRoot.LocalMachine, WindowsVersionFacts.CurrentVersionKey, "EditionID", "Professional")
            .Set(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 0);

        var outcome = await new RemoteDesktopCheck(registry).RunAsync(CheckCatalog.LoadEmbedded()[RemoteDesktopCheck.CheckId], CancellationToken.None);

        Assert.Equal(RemoteDesktopCheck.On, outcome.Code);
    }
}

public class BackupCheckTests
{
    private const string Accounts = @"Software\Microsoft\OneDrive\Accounts";
    private const string ShellFolders = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const string RestoreClients = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients";
    private const string RestoreClient = "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}";

    private static readonly BackupCheck.Settings Settings =
        CheckCatalog.LoadEmbedded()[BackupCheck.CheckId].ParametersAs<BackupCheck.Settings>();

    [Fact]
    public void Sees_which_main_folders_OneDrive_is_backing_up()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.CurrentUser, $@"{Accounts}\Personal", "UserFolder", @"C:\Users\Pat\OneDrive")
            .Set(RegistryRoot.CurrentUser, ShellFolders, "Desktop", @"C:\Users\Pat\OneDrive\Desktop")
            .Set(RegistryRoot.CurrentUser, ShellFolders, "Personal", @"C:\Users\Pat\OneDrive\Documents")
            .Set(RegistryRoot.CurrentUser, ShellFolders, "My Pictures", @"C:\Users\Pat\Pictures");

        Assert.Equal(["Desktop", "Documents"], BackupCheck.Read(registry).OneDriveFolders);
    }

    [Fact]
    public void A_folder_whose_name_merely_starts_like_OneDrive_doesnt_count()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.CurrentUser, $@"{Accounts}\Personal", "UserFolder", @"C:\Users\Pat\OneDrive")
            .Set(RegistryRoot.CurrentUser, ShellFolders, "Personal", @"C:\Users\Pat\OneDriveOld\Documents");

        Assert.Empty(BackupCheck.Read(registry).OneDriveFolders);
    }

    [Fact]
    public void Works_with_a_work_OneDrive_account_too()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.CurrentUser, $@"{Accounts}\Business1", "UserFolder", @"C:\Users\Pat\OneDrive - Pat Ltd")
            .Set(RegistryRoot.CurrentUser, ShellFolders, "Desktop", @"C:\Users\Pat\OneDrive - Pat Ltd\Desktop");

        Assert.Equal(["Desktop"], BackupCheck.Read(registry).OneDriveFolders);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Reads_whether_System_Restore_is_on(bool listed, bool disabledByPolicy)
    {
        var registry = new FakeRegistry();
        if (listed)
        {
            registry.Set(RegistryRoot.LocalMachine, RestoreClients, RestoreClient, new[] { @"\\?\Volume{abc}\:" });
        }

        if (disabledByPolicy)
        {
            registry.Set(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR", 1)
                .Set(RegistryRoot.LocalMachine, RestoreClients, RestoreClient, new[] { @"\\?\Volume{abc}\:" });
        }

        Assert.Equal(listed && !disabledByPolicy, BackupCheck.Read(registry).SystemRestoreOn);
    }

    [Fact]
    public void Sees_File_History()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\fhsvc\Parameters\Configs", @"C:\Users\Pat\AppData\Local\Microsoft\Windows\FileHistory\Configuration\Config", 1);

        Assert.True(BackupCheck.Read(registry).FileHistoryOn);
    }

    [Theory]
    [InlineData(true, true, BackupCheck.AllOn)]
    [InlineData(true, false, BackupCheck.RestoreOff)]
    [InlineData(false, true, BackupCheck.NoFileBackup)]
    [InlineData(false, false, BackupCheck.NothingOn)]
    public void Rules(bool filesBackedUp, bool restoreOn, string expected)
    {
        var facts = new BackupFacts(false, filesBackedUp ? ["Documents"] : [], restoreOn);

        Assert.Equal(expected, BackupCheck.Evaluate(facts, Settings).Code);
    }

    [Fact]
    public void Unknown_System_Restore_isnt_held_against_the_PC()
    {
        Assert.Equal(BackupCheck.AllOn, BackupCheck.Evaluate(new BackupFacts(true, [], null), Settings).Code);
    }

    [Fact]
    public void Says_how_files_are_backed_up_in_everyday_words()
    {
        var outcome = BackupCheck.Evaluate(new BackupFacts(true, ["Desktop", "Documents"], true), Settings);

        Assert.Equal("OneDrive is backing up your Desktop and Documents folders. File History is backing up your files.", outcome.Values["backupHow"]);
    }
}
