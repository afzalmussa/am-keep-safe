using System.Text;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Browsers;

public class Lz4BlockTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(300)]
    [InlineData(70000)]
    public void Decodes_runs_of_literals_of_any_length(int length)
    {
        var data = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();

        Assert.Equal(data, Lz4Block.Decode(BrowserFolders.Lz4LiteralsOnly(data), length));
    }

    [Fact]
    public void Decodes_a_match_that_overlaps_itself()
    {
        // "a", then copy 9 bytes from 1 back: the classic run-length case.
        byte[] block = [0x15, (byte)'a', 0x01, 0x00];

        Assert.Equal("aaaaaaaaaa", Encoding.ASCII.GetString(Lz4Block.Decode(block, 10)));
    }

    [Fact]
    public void Decodes_literals_then_a_match_then_more_literals()
    {
        // "abc", copy 6 from 3 back ("abcabc"), then "xyz".
        byte[] block = [0x32, (byte)'a', (byte)'b', (byte)'c', 0x03, 0x00, 0x30, (byte)'x', (byte)'y', (byte)'z'];

        Assert.Equal("abcabcabcxyz", Encoding.ASCII.GetString(Lz4Block.Decode(block, 12)));
    }

    [Fact]
    public void Reads_Firefox_mozLz4_files()
    {
        const string json = """{"version":6,"engines":[{"_name":"DuckDuckGo"}]}""";

        Assert.Equal(json, Encoding.UTF8.GetString(Lz4Block.DecodeMozLz4(BrowserFolders.MozLz4(json))));
    }

    [Theory]
    [InlineData(new byte[] { 0x15, (byte)'a', 0x05, 0x00 })]   // offset reaches back before the start
    [InlineData(new byte[] { 0x50, (byte)'a' })]               // says 5 literals, has 1
    [InlineData(new byte[] { 0x15, (byte)'a', 0x00, 0x00 })]   // offset of zero
    public void Damaged_data_is_refused_rather_than_misread(byte[] block)
    {
        Assert.Throws<InvalidDataException>(() => Lz4Block.Decode(block, 10));
    }

    [Fact]
    public void A_file_without_the_mozLz4_header_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => Lz4Block.DecodeMozLz4(Encoding.ASCII.GetBytes("not a firefox file at all")));
    }
}
