using AMKeepSafe.Core.Configuration;

namespace AMKeepSafe.Tests.Configuration;

public class AppPathsTests
{
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    [Fact]
    public void Everything_lives_under_ProgramData_AMSoftware_KeepSafe()
    {
        var root = Path.Combine(ProgramData, "AMSoftware", "KeepSafe");

        Assert.Equal(root, AppPaths.DataRoot);
        Assert.Equal(Path.Combine(root, "logs"), AppPaths.Logs);
        Assert.Equal(Path.Combine(root, "undo-history.json"), AppPaths.UndoHistory);
        Assert.Equal(Path.Combine(root, "backups"), AppPaths.Backups);
        Assert.Equal(Path.Combine(root, "licence.dat"), AppPaths.Licence);
    }

    [Fact]
    public void Keep_Safe_does_not_share_a_folder_with_AM_Speed_Up()
    {
        Assert.DoesNotContain("SpeedUp", AppPaths.DataRoot, StringComparison.OrdinalIgnoreCase);
    }
}
