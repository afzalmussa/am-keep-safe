using System.Reflection;
using System.Text.RegularExpressions;
using AMKeepSafe.Core.Configuration;

namespace AMKeepSafe.Tests.Configuration;

public class AppLinksTests
{
    public static TheoryData<string> AllLinks()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(AppLinks).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            data.Add((string)field.GetValue(null)!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllLinks))]
    public void Every_link_is_a_secure_web_address(string url)
    {
        Assert.True(AppSettings.IsUsableUrl(url), $"{url} should be an absolute https address");
    }

    [Fact]
    public void GitHub_links_point_at_the_published_repository()
    {
        Assert.Equal("https://github.com/afzalmussa/am-keep-safe", AppLinks.Repository);
        Assert.Equal("https://github.com/afzalmussa/am-keep-safe/releases", AppLinks.Releases);
        Assert.Equal("https://github.com/afzalmussa/am-keep-safe/issues", AppLinks.Issues);
    }

    [Fact]
    public void Latest_download_link_uses_the_fixed_file_name()
    {
        // The release workflow must always publish AMKeepSafe.exe, or this link breaks.
        Assert.Equal("https://github.com/afzalmussa/am-keep-safe/releases/latest/download/AMKeepSafe.exe", AppLinks.LatestDownload);
    }

    [Fact]
    public void Help_link_goes_to_the_contact_page()
    {
        Assert.Equal("https://www.amsoftware.uk/contact", AppLinks.Contact);
    }

    [Fact]
    public void Version_is_major_minor_patch()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$"), AppInfo.Version);
    }

    [Fact]
    public void Product_name_is_AM_Keep_Safe()
    {
        Assert.Equal("AM Keep Safe", AppInfo.ProductName);
    }
}
