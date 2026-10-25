using System.Text;
using AMKeepSafe.Core.Configuration;

namespace AMKeepSafe.Tests.Configuration;

public class AppSettingsTests
{
    private static AppSettings Parse(string json) => AppSettings.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void The_settings_built_into_the_app_load()
    {
        var settings = AppSettings.LoadEmbedded();

        Assert.NotNull(settings.LemonSqueezy);
    }

    [Fact]
    public void Shipped_report_email_is_AM_Softwares_contact_address()
    {
        Assert.Equal("contact@amsoftware.uk", AppSettings.LoadEmbedded().ReportEmailOrDefault);
    }

    [Fact]
    public void Until_the_product_exists_the_shipped_settings_hide_Get_Pro_and_refuse_every_key()
    {
        // Phase 6 fills these in. If this fails, check the ids are AM Keep Safe's, not AM Speed Up's.
        var settings = AppSettings.LoadEmbedded();

        Assert.False(settings.HasCheckoutUrl);
        Assert.False(settings.LemonSqueezy!.IsConfigured);
    }

    [Fact]
    public void Reads_every_value()
    {
        var settings = Parse("""
            {
              "checkoutUrl": "https://example.lemonsqueezy.com/checkout/buy/abc",
              "reportEmail": "help@example.co.uk",
              "lemonSqueezy": { "storeId": 1, "productId": 2, "variantId": 3 }
            }
            """);

        Assert.True(settings.HasCheckoutUrl);
        Assert.Equal("help@example.co.uk", settings.ReportEmailOrDefault);
        Assert.Equal(new LemonSqueezySettings { StoreId = 1, ProductId = 2, VariantId = 3 }, settings.LemonSqueezy);
        Assert.True(settings.LemonSqueezy!.IsConfigured);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed_so_hand_edits_dont_break_it()
    {
        var settings = Parse("""
            {
              // the shop page
              "checkoutUrl": "https://example.com/buy",
            }
            """);

        Assert.True(settings.HasCheckoutUrl);
    }

    [Fact]
    public void A_broken_file_falls_back_to_nothing_set_up()
    {
        var settings = Parse("{ not json");

        Assert.False(settings.HasCheckoutUrl);
        Assert.Equal(AppSettings.DefaultReportEmail, settings.ReportEmailOrDefault);
        Assert.Null(settings.LemonSqueezy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("Afzal <contact@amsoftware.uk>")]
    [InlineData("someone@localhost")]
    public void A_missing_or_invalid_report_email_falls_back_to_the_default(string? email)
    {
        Assert.Equal("contact@amsoftware.uk", new AppSettings { ReportEmail = email }.ReportEmailOrDefault);
    }

    [Theory]
    [InlineData("https://example.com/buy", true)]
    [InlineData("  https://example.com/buy  ", true)]
    [InlineData("http://example.com/buy", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("example.com/buy", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_https_addresses_are_usable(string? url, bool usable)
    {
        Assert.Equal(usable, AppSettings.IsUsableUrl(url));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    public void Lemon_Squeezy_needs_both_a_store_and_a_product(long store, long product, bool configured)
    {
        Assert.Equal(configured, new LemonSqueezySettings { StoreId = store, ProductId = product }.IsConfigured);
    }
}
