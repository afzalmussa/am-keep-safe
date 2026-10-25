using AMKeepSafe.Core.Configuration;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.UI.Tests;

public class MainViewModelTests
{
    private readonly FakeLog _log = new();
    private readonly FakeShell _shell = new();
    private readonly FakeDialogs _dialogs = new();

    private MainViewModel Create(AppSettings? settings = null, AppOptions? options = null) =>
        new(_log, _shell, _dialogs, settings ?? new AppSettings(), options ?? AppOptions.Default);

    [Fact]
    public void Shows_the_free_version_without_a_licence()
    {
        var vm = Create();

        Assert.Equal("Free version", vm.LicenceStatusText);
        Assert.False(vm.IsPro);
    }

    [Fact]
    public void Version_text_includes_the_version_number()
    {
        Assert.Equal($"Version {AppInfo.Version}", Create().VersionText);
    }

    [Fact]
    public void Dry_run_flag_reaches_the_view_model()
    {
        Assert.True(Create(options: AppOptions.Parse(["--dry-run"])).IsDryRun);
        Assert.False(Create().IsDryRun);
    }

    [Fact]
    public void View_log_opens_todays_log_file_and_logs_that_it_did()
    {
        Create().ViewLogCommand.Execute(null);

        Assert.Equal([_log.CurrentLogFile], _shell.OpenedFiles);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Info && e.Message.Contains("opened the log"));
        Assert.Empty(_dialogs.Messages);
    }

    [Fact]
    public void View_log_falls_back_to_the_folder_when_the_file_wont_open()
    {
        _shell.FilesWork = false;

        Create().ViewLogCommand.Execute(null);

        Assert.Equal([_log.LogDirectory], _shell.OpenedFolders);
        Assert.Empty(_dialogs.Messages);
    }

    [Fact]
    public void View_log_says_where_the_log_is_when_nothing_opens()
    {
        _shell.FilesWork = false;
        _shell.FoldersWork = false;

        Create().ViewLogCommand.Execute(null);

        var message = Assert.Single(_dialogs.Messages);
        Assert.Contains(_log.LogDirectory, message.Message);
    }

    [Fact]
    public void Footer_links_go_to_the_right_pages()
    {
        var vm = Create();

        vm.CheckForUpdatesCommand.Execute(null);
        vm.ReportProblemCommand.Execute(null);
        vm.GetHelpCommand.Execute(null);
        vm.OpenWebsiteCommand.Execute(null);

        Assert.Equal(
            [
                "https://github.com/afzalmussa/am-keep-safe/releases",
                "https://github.com/afzalmussa/am-keep-safe/issues",
                "https://www.amsoftware.uk/contact",
                "https://www.amsoftware.uk",
            ],
            _shell.OpenedUrls);
    }

    [Fact]
    public void If_the_browser_wont_open_the_address_is_shown_instead()
    {
        _shell.UrlsWork = false;

        Create().GetHelpCommand.Execute(null);

        Assert.Contains("https://www.amsoftware.uk/contact", Assert.Single(_dialogs.Messages).Message);
    }

    [Fact]
    public void Get_Pro_is_hidden_until_a_checkout_link_is_set_up()
    {
        Assert.False(Create().CanGetPro);
        Assert.True(Create(new AppSettings { CheckoutUrl = "https://example.com/buy" }).CanGetPro);
    }

    [Fact]
    public void Get_Pro_opens_the_configured_checkout_page()
    {
        Create(new AppSettings { CheckoutUrl = "https://example.com/buy" }).GetProCommand.Execute(null);

        Assert.Equal(["https://example.com/buy"], _shell.OpenedUrls);
    }

    [Fact]
    public void Enter_licence_key_explains_safety_features_stay_free()
    {
        Create().EnterLicenceKeyCommand.Execute(null);

        Assert.Contains("Every safety feature", Assert.Single(_dialogs.Messages).Message);
    }
}
