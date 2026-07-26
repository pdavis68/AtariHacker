using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class FindPatternToolTests
{
    [Fact]
    public void FindPattern_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = FindPatternTool.FindPattern(session, "20 ?? ??", 10);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void FindPattern_ReturnsErrorForEmptyPattern()
    {
        var session = new RomSession { Data = new byte[100] };
        var result = FindPatternTool.FindPattern(session, "", 10);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void FindPattern_FindsExactBytePatternMatches()
    {
        var data = new byte[100];
        data[10] = 0x20; data[11] = 0x00; data[12] = 0x81;
        data[50] = 0x20; data[51] = 0x00; data[52] = 0x81;
        var session = new RomSession { Data = data };

        var result = FindPatternTool.FindPattern(session, "20 00 81", 10);
        Assert.Contains("Found 2 match", result);
    }

    [Fact]
    public void FindPattern_HandlesWildcardTokensCorrectly()
    {
        var data = new byte[100];
        data[10] = 0x20; data[11] = 0x34; data[12] = 0x12;
        data[30] = 0x20; data[31] = 0x78; data[32] = 0x56;
        var session = new RomSession { Data = data };

        var result = FindPatternTool.FindPattern(session, "20 ?? ??", 10);
        Assert.Contains("Found 2 match", result);
    }

    [Fact]
    public void FindPattern_RespectsMaxResultsLimit()
    {
        var data = new byte[1000];
        for (var i = 0; i < 100; i++)
            data[i * 10] = 0x20;
        var session = new RomSession { Data = data };

        var result = FindPatternTool.FindPattern(session, "20", 5);
        Assert.Contains("Found 5 match", result);
    }

    [Fact]
    public void FindPattern_ReportsZeroMatchesForNonExistentPattern()
    {
        var data = new byte[100];
        var session = new RomSession { Data = data };

        var result = FindPatternTool.FindPattern(session, "FF FF FF", 10);
        Assert.Contains("Found 0 match", result);
    }
}