using System.Xml.Linq;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Repository;

/// <summary>
/// Checks on the files that build and publish the app, so a careless edit can't break the download
/// link, drop the administrator request or ship someone else's name.
/// </summary>
public class RepositoryFileTests
{
    [Fact]
    public void The_app_asks_for_administrator_rights()
    {
        var manifest = XDocument.Parse(RepoFiles.Read("src", "AMKeepSafe", "app.manifest"));

        var level = manifest.Descendants().Single(e => e.Name.LocalName == "requestedExecutionLevel");
        Assert.Equal("requireAdministrator", level.Attribute("level")?.Value);
    }

    [Fact]
    public void The_app_is_built_as_AMKeepSafe_exe_with_the_brand_icon()
    {
        var project = XDocument.Parse(RepoFiles.Read("src", "AMKeepSafe", "AMKeepSafe.csproj"));
        string? Property(string name) => project.Descendants(name).FirstOrDefault()?.Value;

        Assert.Equal("AMKeepSafe", Property("AssemblyName"));
        Assert.Equal(@"..\..\assets\am-icon-white-edge.ico", Property("ApplicationIcon"));
        Assert.Equal("true", Property("PublishSingleFile"));
        Assert.Equal("true", Property("SelfContained"));
        Assert.Equal("win-x64", Property("RuntimeIdentifier"));
    }

    [Fact]
    public void Every_asset_the_app_project_points_at_exists()
    {
        var project = XDocument.Parse(RepoFiles.Read("src", "AMKeepSafe", "AMKeepSafe.csproj"));
        var paths = project.Descendants("Resource").Select(r => r.Attribute("Include")!.Value)
            .Append(project.Descendants("ApplicationIcon").Single().Value);

        Assert.All(paths, p => Assert.True(
            File.Exists(Path.GetFullPath(Path.Combine(RepoFiles.PathTo("src", "AMKeepSafe"), p.Replace('\\', Path.DirectorySeparatorChar)))),
            $"{p} is missing"));
    }

    [Theory]
    [InlineData("am-icon-white-edge.ico")]
    [InlineData("am-icon-512.png")]
    public void The_brand_files_named_in_the_brief_exist(string file)
    {
        Assert.True(File.Exists(RepoFiles.PathTo("assets", file)), $"assets/{file} is missing");
    }

    [Fact]
    public void Release_workflow_publishes_the_exe_under_the_fixed_name_on_version_tags()
    {
        var workflow = RepoFiles.Read(".github", "workflows", "release.yml");

        Assert.Contains("- \"v*\"", workflow);
        Assert.Contains("gh release create $env:TAG publish/AMKeepSafe.exe", workflow);
        Assert.Contains("'--title', $env:TAG", workflow);
    }

    [Fact]
    public void Build_workflow_runs_on_every_push_and_pull_request_on_Windows()
    {
        var workflow = RepoFiles.Read(".github", "workflows", "build.yml");

        Assert.Contains("\n  push:", workflow);
        Assert.Contains("\n  pull_request:", workflow);
        Assert.Contains("runs-on: windows-latest", workflow);
        Assert.Contains("dotnet test AMKeepSafe.sln", workflow);
    }

    [Fact]
    public void Licence_is_noncommercial_and_names_this_app()
    {
        var licence = RepoFiles.Read("LICENSE");

        Assert.Contains("PolyForm Noncommercial License 1.0.0", licence);
        Assert.Contains("unmodified official releases of AM Keep Safe", licence);
        Assert.Contains("https://github.com/afzalmussa/am-keep-safe/releases", licence);
    }

    [Fact]
    public void Nothing_shipped_still_mentions_AM_Speed_Up()
    {
        // Several files started as copies of AM Speed Up's. None of its name should survive.
        string[] leftovers = ["Speed Up", "SpeedUp", "speed-up", "speedup"];
        var files = RepoFiles.Find("src", "*.*")
            .Concat(RepoFiles.Find(".github", "*.*"))
            .Concat(RepoFiles.Find("data", "*.*"))
            .Where(f => !f.EndsWith(".png", StringComparison.Ordinal) && !f.EndsWith(".ico", StringComparison.Ordinal) && !f.EndsWith(".ttf", StringComparison.Ordinal))
            .Append(RepoFiles.PathTo("LICENSE"))
            .Append(RepoFiles.PathTo("Directory.Build.props"));

        var found = files
            .SelectMany(f => leftovers.Where(l => File.ReadAllText(f).Contains(l, StringComparison.OrdinalIgnoreCase)).Select(l => $"{RepoFiles.Relative(f)}: {l}"))
            .ToList();

        // AppSettings explains why a Speed Up key mustn't unlock Keep Safe, which is the one deliberate mention.
        found.RemoveAll(f => f.StartsWith("src/AMKeepSafe.Core/Configuration/AppSettings.cs", StringComparison.Ordinal));
        Assert.True(found.Count == 0, "Leftover AM Speed Up names:\n" + string.Join('\n', found));
    }

    [Fact]
    public void The_bundled_font_ships_with_its_licence_and_is_credited()
    {
        Assert.True(File.Exists(RepoFiles.PathTo("assets", "fonts", "JetBrainsMono-OFL.txt")));
        Assert.Contains("JetBrains Mono", RepoFiles.Read("THIRD-PARTY-NOTICES.md"));
    }

    [Theory]
    [InlineData("something-went-wrong.yml", "Something went wrong")]
    [InlineData("feature-request.yml", "Feature request")]
    public void Issue_templates_exist_and_warn_that_the_page_is_public(string file, string name)
    {
        var template = RepoFiles.Read(".github", "ISSUE_TEMPLATE", file);

        Assert.Contains($"name: {name}", template);
        Assert.Contains("This page is public", template);
    }
}
