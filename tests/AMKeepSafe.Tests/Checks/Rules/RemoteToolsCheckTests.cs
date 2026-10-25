using System.Text;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks.Rules;

public class RemoteToolsCheckTests
{
    private static readonly RemoteToolList Shipped = RemoteToolList.LoadEmbedded();
    private static readonly RemoteToolsCheck.Settings Settings =
        CheckCatalog.LoadEmbedded()[RemoteToolsCheck.CheckId].ParametersAs<RemoteToolsCheck.Settings>();

    private static RemoteToolList Parse(string json) => RemoteToolList.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Finds_installed_tools_by_name_ignoring_capitals()
    {
        var found = Shipped.Find(["anydesk", "Microsoft Edge"], []);

        Assert.Equal([new FoundRemoteTool("AnyDesk", true, false)], found);
    }

    [Fact]
    public void Finds_portable_copies_that_are_only_running()
    {
        var found = Shipped.Find([], ["explorer.exe", "ANYDESK.EXE"]);

        Assert.Equal([new FoundRemoteTool("AnyDesk", false, true)], found);
    }

    [Fact]
    public void Installed_and_running_is_one_entry()
    {
        Assert.Equal([new FoundRemoteTool("TeamViewer", true, true)], Shipped.Find(["TeamViewer 15"], ["TeamViewer.exe"]));
    }

    [Fact]
    public void Quick_Assist_is_built_into_Windows_so_only_counts_while_open()
    {
        Assert.Empty(Shipped.Find(["Quick Assist"], []));
        Assert.Equal([new FoundRemoteTool("Quick Assist", false, true)], Shipped.Find([], ["QuickAssist.exe"]));
    }

    [Fact]
    public void Not_names_rule_out_other_products_from_the_same_company()
    {
        Assert.Empty(Shipped.Find(["LogMeIn Hamachi"], []));
        Assert.NotEmpty(Shipped.Find(["LogMeIn"], []));
    }

    [Fact]
    public void Nothing_found()
    {
        Assert.Equal(RemoteToolsCheck.NoneFound, RemoteToolsCheck.Evaluate([], Settings).Code);
    }

    [Fact]
    public void Found_tools_are_listed_in_everyday_words()
    {
        var outcome = RemoteToolsCheck.Evaluate(
            [new FoundRemoteTool("AnyDesk", true, false), new FoundRemoteTool("UltraViewer", false, true), new FoundRemoteTool("TeamViewer", true, true)],
            Settings);

        Assert.Equal(RemoteToolsCheck.Found, outcome.Code);
        Assert.Equal(["AnyDesk: installed", "UltraViewer: open right now", "TeamViewer: installed and open right now"], outcome.Items);
    }

    [Fact]
    public void Finding_tools_is_information_never_a_warning_and_never_calls_them_harmful()
    {
        var found = CheckCatalog.LoadEmbedded()[RemoteToolsCheck.CheckId].Outcomes[RemoteToolsCheck.Found];

        Assert.Equal(CheckStatus.Info, found.Status);
        Assert.DoesNotContain("malware", found.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("harmful", found.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("honest uses", found.Summary);
    }

    [Fact]
    public void Reads_installed_programs_for_all_users_and_both_32_and_64_bit()
    {
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, $@"{uninstall}\AnyDesk", "DisplayName", "AnyDesk")
            .Set(RegistryRoot.LocalMachine, $@"{uninstall}\{{1234}}", "DisplayName", "TeamViewer", use32BitView: true)
            .Set(RegistryRoot.CurrentUser, $@"{uninstall}\RustDesk", "DisplayName", "RustDesk")
            .AddKey(RegistryRoot.LocalMachine, $@"{uninstall}\NoName");

        Assert.Equal(["AnyDesk", "TeamViewer", "RustDesk"], RemoteToolsCheck.InstalledProgramNames(registry));
    }

    [Theory]
    [InlineData("""{ "tools": [ { "name": "" , "programs": ["x.exe"] } ] }""", "has no name")]
    [InlineData("""{ "tools": [ { "name": "X" } ] }""", "nothing to look for")]
    [InlineData("""{ "tools": [ { "name": "X", "installedNames": ["X"], "onlyWhenOpen": true } ] }""", "needs at least one program")]
    [InlineData("""{ "tools": [ { "name": "X", "programs": ["x"] } ] }""", "ending in .exe")]
    [InlineData("""{ "tools": [ { "name": "X", "programs": ["x.exe"] }, { "name": "x", "programs": ["y.exe"] } ] }""", "more than once")]
    public void Validation_catches_mistakes_in_the_list(string json, string expected)
    {
        Assert.Contains(Parse(json).Validate(), p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_shipped_list_includes_the_tools_named_in_the_brief()
    {
        var names = Shipped.Tools.Select(t => t.Name).ToList();

        Assert.Subset(names.ToHashSet(), new HashSet<string> { "AnyDesk", "TeamViewer", "UltraViewer", "RustDesk", "Quick Assist" });
    }
}
