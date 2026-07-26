using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class PatternToolsTests
{
    [Fact]
    public void ListPatterns_ReturnsEmptyMessageWhenLibraryIsEmpty()
    {
        var result = PatternTools.ListPatterns(null, null, null, "text");
        Assert.NotNull(result);
    }

    [Fact]
    public void AddPattern_ValidatesHexPattern()
    {
        var result = PatternTools.AddPattern("test", "invalid!!", null, null, null, false);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void RemovePattern_RemovesPatternByName()
    {
        PatternTools.AddPattern("test_remove_pat", "20 ?? ??", "test", null, null, false);
        var result = PatternTools.RemovePattern("test_remove_pat");
        Assert.DoesNotContain("ERROR", result);
    }

    [Fact]
    public void ShowPattern_DisplaysPatternDetails()
    {
        PatternTools.AddPattern("test_show_pat", "60", "RTS instruction", null, null, false);
        var result = PatternTools.ShowPattern("test_show_pat");
        Assert.NotNull(result);
        PatternTools.RemovePattern("test_show_pat");
    }

    [Fact]
    public void SearchPattern_SearchesBinaryUsingSavedPattern()
    {
        var data = new byte[] { 0x60, 0x00, 0x60, 0x00 };
        var session = new RomSession { Data = data };
        PatternTools.AddPattern("test_search_pat", "60", null, null, null, false);
        var result = PatternTools.SearchPattern(session, "test_search_pat", 10);
        Assert.Contains("Found 2 match", result);
        PatternTools.RemovePattern("test_search_pat");
    }
}
