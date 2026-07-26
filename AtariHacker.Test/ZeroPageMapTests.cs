using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class ZeroPageMapTests
{
    [Fact]
    public void CanAddAndRetrieveSymbolEntryByByteKey()
    {
        var map = new ZeroPageMap();
        var entry = new SymbolEntry("CH", null, false, false, SymbolGroup.OsVariables);
        map[0x80] = entry;

        Assert.True(map.ContainsKey(0x80));
        Assert.Same(entry, map[0x80]);
    }

    [Fact]
    public void TryGetValue_ReturnsFalseForUnknownKey()
    {
        var map = new ZeroPageMap();
        Assert.False(map.TryGetValue(0xFF, out var entry));
        Assert.Null(entry);
    }
}