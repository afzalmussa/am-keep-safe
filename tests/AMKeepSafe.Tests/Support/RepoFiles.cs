namespace AMKeepSafe.Tests.Support;

/// <summary>Finds files in the repository, so tests can check the files that actually ship.</summary>
public static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static string PathTo(params string[] parts) => Path.Combine([Root, .. parts]);

    public static string Read(params string[] parts) => File.ReadAllText(PathTo(parts));

    /// <summary>Files under a folder, leaving out build output.</summary>
    public static IEnumerable<string> Find(string folder, string pattern)
    {
        var start = PathTo(folder);
        if (!Directory.Exists(start))
        {
            return [];
        }

        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(start, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"))
            .Order(StringComparer.Ordinal);
    }

    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AMKeepSafe.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Couldn't find AMKeepSafe.sln above the test folder.");
    }
}
