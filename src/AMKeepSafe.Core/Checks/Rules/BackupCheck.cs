using AMKeepSafe.Core.Platform;
using AMKeepSafe.Core.Text;

namespace AMKeepSafe.Core.Checks.Rules;

/// <param name="FileHistoryOn">Windows' File History is set up.</param>
/// <param name="OneDriveFolders">Which of Desktop, Documents and Pictures OneDrive is backing up.</param>
/// <param name="SystemRestoreOn">Null if it couldn't be told.</param>
public sealed record BackupFacts(bool FileHistoryOn, IReadOnlyList<string> OneDriveFolders, bool? SystemRestoreOn);

/// <summary>
/// "Is your stuff backed up?" Looks for the backups built into Windows: File History, OneDrive
/// backing up the main folders, and System Restore. Other backup programs can't be seen, so the
/// wording says so rather than claiming nothing is backed up.
/// </summary>
public sealed class BackupCheck(IRegistryReader registry) : ISafetyCheck
{
    public const string CheckId = "backups";

    public const string AllOn = "allOn";
    public const string RestoreOff = "restoreOff";
    public const string NoFileBackup = "noFileBackup";
    public const string NothingOn = "nothingOn";

    private const string FileHistoryConfigsKey = @"SYSTEM\CurrentControlSet\Services\fhsvc\Parameters\Configs";
    private const string OneDriveAccountsKey = @"Software\Microsoft\OneDrive\Accounts";
    private const string UserShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const string SystemRestoreClientsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients";
    private const string SystemRestoreClientName = "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}";
    private const string SystemRestorePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

    // The registry's names for the folders, and the names people know them by.
    private static readonly (string RegistryName, string Folder)[] MainFolders =
        [("Desktop", "Desktop"), ("Personal", "Documents"), ("My Pictures", "Pictures")];

    public sealed record Settings
    {
        public string ViaOneDrive { get; init; } = "OneDrive is backing up your {folders}.";

        public string ViaFileHistory { get; init; } = "File History is backing up your files.";

        public Dictionary<string, string> FolderNames { get; init; } = new()
        {
            ["Desktop"] = "Desktop",
            ["Documents"] = "Documents",
            ["Pictures"] = "Pictures",
        };
    }

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [AllOn, RestoreOff, NoFileBackup, NothingOn];

    public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(Read(registry), definition.ParametersAs<Settings>()));

    public static BackupFacts Read(IRegistryReader registry)
    {
        var fileHistory = registry.GetValueNames(RegistryRoot.LocalMachine, FileHistoryConfigsKey).Count > 0;

        var oneDriveRoots = registry.GetSubKeyNames(RegistryRoot.CurrentUser, OneDriveAccountsKey)
            .Select(account => registry.GetString(RegistryRoot.CurrentUser, $@"{OneDriveAccountsKey}\{account}", "UserFolder"))
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder!.TrimEnd('\\') + "\\")
            .ToList();

        var backedUp = MainFolders
            .Where(f => registry.GetString(RegistryRoot.CurrentUser, UserShellFoldersKey, f.RegistryName) is { } path
                        && oneDriveRoots.Any(root => (path.TrimEnd('\\') + "\\").StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            .Select(f => f.Folder)
            .ToList();

        bool? restore;
        if (registry.GetInt(RegistryRoot.LocalMachine, SystemRestorePolicyKey, "DisableSR") == 1)
        {
            restore = false;
        }
        else
        {
            // Windows lists the drives System Restore protects in this value. Empty or missing means none.
            restore = registry.GetValue(RegistryRoot.LocalMachine, SystemRestoreClientsKey, SystemRestoreClientName) switch
            {
                string[] drives => drives.Any(d => !string.IsNullOrWhiteSpace(d)),
                string text => !string.IsNullOrWhiteSpace(text),
                null => false,
                _ => null,
            };
        }

        return new BackupFacts(fileHistory, backedUp, restore);
    }

    public static CheckOutcome Evaluate(BackupFacts facts, Settings settings)
    {
        var detail = $"File History: {facts.FileHistoryOn}. OneDrive folders: {(facts.OneDriveFolders.Count == 0 ? "none" : string.Join(", ", facts.OneDriveFolders))}. " +
                     $"System Restore: {facts.SystemRestoreOn?.ToString() ?? "unknown"}.";

        var how = new List<string>();
        if (facts.OneDriveFolders.Count > 0)
        {
            var names = facts.OneDriveFolders.Select(f => settings.FolderNames.TryGetValue(f, out var n) ? n : f).ToList();
            how.Add(CheckCatalog.Fill(settings.ViaOneDrive, new Dictionary<string, string> { ["folders"] = FriendlyText.List(names) }));
        }

        if (facts.FileHistoryOn)
        {
            how.Add(settings.ViaFileHistory);
        }

        var values = new Dictionary<string, string> { ["backupHow"] = string.Join(" ", how) };
        var filesBackedUp = how.Count > 0;

        // If System Restore couldn't be read, don't hold it against the PC.
        var restoreOn = facts.SystemRestoreOn ?? true;

        var code = (filesBackedUp, restoreOn) switch
        {
            (true, true) => AllOn,
            (true, false) => RestoreOff,
            (false, true) => NoFileBackup,
            (false, false) => NothingOn,
        };
        return new CheckOutcome(code, values, Detail: detail);
    }
}
