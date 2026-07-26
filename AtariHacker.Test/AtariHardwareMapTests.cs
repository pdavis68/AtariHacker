using AtariHacker.Atari;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class AtariHardwareMapTests
{
    [Fact]
    public void Populate_VerifiesAllHardwareSymbolsAdded()
    {
        var table = new SymbolTable();
        AtariHardwareMap.Populate(table);

        Assert.True(table.ContainsKey((ushort)0xD000));
        Assert.Equal("HPOSP0", table[(ushort)0xD000].Label);
        Assert.True(table[(ushort)0xD000].IsHardware);
        Assert.Equal(SymbolGroup.Hardware, table[(ushort)0xD000].Group);

        Assert.True(table.ContainsKey((ushort)0xC000));
        Assert.Equal("SYSVBL", table[(ushort)0xC000].Label);
        Assert.Equal(SymbolGroup.OsRom, table[(ushort)0xC000].Group);

        Assert.True(table.ContainsKey((ushort)0xFFFA));
        Assert.Equal("NMIVEC", table[(ushort)0xFFFA].Label);
    }

    [Fact]
    public void PopulateZeroPage_VerifiesAllZeroPageSymbolsAdded()
    {
        var map = new ZeroPageMap();
        AtariHardwareMap.PopulateZeroPage(map);

        Assert.True(map.ContainsKey(0x00));
        Assert.StartsWith("LINZBS", map[0x00].Label);
        Assert.Equal(SymbolGroup.OsVariables, map[0x00].Group);

        Assert.True(map.ContainsKey(0xFF));
        Assert.Equal("DVSTAT", map[0xFF].Label);
    }

    [Fact]
    public void TryGetHardwareSymbol_ReturnsCorrectEntryForKnownAddress()
    {
        var found = AtariHardwareMap.TryGetHardwareSymbol((ushort)0xD012, out var entry);
        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("COLPM0", entry.Label);
    }

    [Fact]
    public void TryGetHardwareSymbol_ReturnsFalseForNonHardwareAddress()
    {
        var found = AtariHardwareMap.TryGetHardwareSymbol((ushort)0x2000, out var entry);
        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void HardwareSymbolDictionary_IsReadOnly()
    {
        var symbols = AtariHardwareMap.HardwareSymbols;
        Assert.NotNull(symbols);
        Assert.True(symbols.ContainsKey((ushort)0xD000));
    }
}
