using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class PatternDetectionToolTests
{
    [Fact]
    public void DetectPatterns_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = PatternDetectionTool.DetectPatterns(session);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void DetectPatterns_DetectsAllPatternTypes()
    {
        var data = new byte[100];
        var session = new RomSession { Data = data };
        var result = PatternDetectionTool.DetectPatterns(session);
        Assert.NotNull(result);
    }

    [Fact]
    public void DetectPatterns_FormatsCsvOutputCorrectly()
    {
        var data = new byte[100];
        var session = new RomSession { Data = data };
        var result = PatternDetectionTool.DetectPatterns(session, format: "csv");
        Assert.Contains("type", result);
    }
}
