using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class StringSearchToolTests
{
    [Fact]
    public void FindStrings_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = StringSearchTool.FindStrings(session);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void FindStrings_FindsAsciiStringsOfMinimumLength()
    {
        var data = new byte[100];
        var text = "HELLO WORLD"u8.ToArray();
        Array.Copy(text, 0, data, 10, text.Length);
        var session = new RomSession { Data = data };

        var result = StringSearchTool.FindStrings(session, minLength: 4, encoding: "ascii");
        Assert.Contains("HELLO", result);
    }

    [Fact]
    public void FindStrings_RespectsMinLengthParameter()
    {
        var data = new byte[100];
        data[10] = (byte)'A'; data[11] = (byte)'B';
        var session = new RomSession { Data = data };

        var result = StringSearchTool.FindStrings(session, minLength: 5, encoding: "ascii");
        Assert.DoesNotContain("AB", result);
    }

    [Fact]
    public void FindStrings_RespectsMaxResultsLimit()
    {
        var data = new byte[1000];
        for (var i = 0; i < 50; i++)
        {
            var s = $"STRING{i:D2}";
            var text = System.Text.Encoding.ASCII.GetBytes(s);
            Array.Copy(text, 0, data, i * 20, text.Length);
        }
        var session = new RomSession { Data = data };

        var result = StringSearchTool.FindStrings(session, minLength: 4, maxResults: 5, encoding: "ascii");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= 8);
    }

    [Fact]
    public void FindStrings_FiltersBySubstring()
    {
        var data = new byte[200];
        var text1 = "PLAYER DATA"u8.ToArray();
        var text2 = "ENEMY DATA"u8.ToArray();
        Array.Copy(text1, 0, data, 10, text1.Length);
        Array.Copy(text2, 0, data, 50, text2.Length);
        var session = new RomSession { Data = data };

        var result = StringSearchTool.FindStrings(session, minLength: 4, filter: "PLAYER", encoding: "ascii");
        Assert.Contains("PLAYER", result);
        Assert.DoesNotContain("ENEMY", result);
    }

    [Fact]
    public void FindStrings_ReturnsNoneWhenNoStringsFound()
    {
        var data = new byte[100];
        var session = new RomSession { Data = data };

        var result = StringSearchTool.FindStrings(session, minLength: 4, encoding: "ascii");
        Assert.Contains("none", result, StringComparison.OrdinalIgnoreCase);
    }
}