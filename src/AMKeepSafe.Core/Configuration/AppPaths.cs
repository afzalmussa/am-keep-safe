namespace AMKeepSafe.Core.Configuration;

/// <summary>Where the app keeps its files: %ProgramData%\AMSoftware\KeepSafe on Windows.</summary>
public static class AppPaths
{
    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AMSoftware", "KeepSafe");

    public static string Logs => Path.Combine(DataRoot, "logs");

    public static string UndoHistory => Path.Combine(DataRoot, "undo-history.json");

    /// <summary>Copies of browser policies and other settings taken before they're removed, so undo can put them back.</summary>
    public static string Backups => Path.Combine(DataRoot, "backups");

    public static string Licence => Path.Combine(DataRoot, "licence.dat");
}
