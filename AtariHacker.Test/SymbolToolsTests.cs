using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class SymbolToolsTests
{
    [Fact]
    public void DefineSymbol_AddsSymbolToTable()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        var persistence = new SessionPersistence(session, symbols, new ZeroPageMap(), new SegmentManager());
        var result = SymbolTools.DefineSymbol(session, symbols, persistence, "$8000", "MAIN");
        Assert.Contains("Defined symbol", result);
        Assert.True(symbols.ContainsKey((ushort)0x8000));
        Assert.Equal("MAIN", symbols[(ushort)0x8000].Label);
    }

    [Fact]
    public void RemoveSymbol_RemovesUserDefinedSymbol()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        symbols[(ushort)0x8000] = new SymbolEntry("MAIN", null, false, true);
        var persistence = new SessionPersistence(session, symbols, new ZeroPageMap(), new SegmentManager());
        var result = SymbolTools.RemoveSymbol(session, symbols, persistence, "$8000");
        Assert.Contains("Removed", result);
        Assert.False(symbols.ContainsKey((ushort)0x8000));
    }

    [Fact]
    public void RemoveSymbol_ReturnsErrorWhenTryingToRemoveHardwareSymbol()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        symbols[(ushort)0xD000] = new SymbolEntry("HPOSP0", null, true, false);
        var persistence = new SessionPersistence(session, symbols, new ZeroPageMap(), new SegmentManager());
        var result = SymbolTools.RemoveSymbol(session, symbols, persistence, "$D000");
        Assert.Contains("ERROR", result);
    }

    [Fact]
    public void LookupSymbol_ReturnsSymbolDetailsForKnownAddress()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        symbols[(ushort)0x8000] = new SymbolEntry("MAIN", "Main routine", false, true);
        var result = SymbolTools.LookupSymbol(session, symbols, "$8000");
        Assert.Contains("MAIN", result);
    }

    [Fact]
    public void LookupSymbol_ReturnsNoSymbolDefinedForUnknownAddress()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        var result = SymbolTools.LookupSymbol(session, symbols, "$9999");
        Assert.Contains("No symbol", result);
    }

    [Fact]
    public void ListSymbols_ListsUserDefinedSymbols()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        symbols[(ushort)0x8000] = new SymbolEntry("MAIN", null, false, true);
        symbols[(ushort)0x8100] = new SymbolEntry("SUB", null, false, true);

        var result = SymbolTools.ListSymbols(session, symbols, false, null);
        Assert.Contains("MAIN", result);
        Assert.Contains("SUB", result);
    }

    [Fact]
    public void ListSymbols_IncludesHardwareSymbolsWhenRequested()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        symbols[(ushort)0xD000] = new SymbolEntry("HPOSP0", null, true, false);

        var result = SymbolTools.ListSymbols(session, symbols, true, null);
        Assert.Contains("HPOSP0", result);
    }

    [Fact]
    public void ListSymbols_FiltersBySubstring()
    {
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        symbols[(ushort)0x8000] = new SymbolEntry("MAIN", null, false, true);
        symbols[(ushort)0x8100] = new SymbolEntry("SUBROUTINE", null, false, true);

        var result = SymbolTools.ListSymbols(session, symbols, false, "SUB");
        Assert.Contains("SUBROUTINE", result);
        Assert.DoesNotContain("MAIN", result);
    }

    [Fact]
    public void SetSymbols_EnablesDisablesSymbolGroups()
    {
        var symbols = new SymbolTable();
        var persistence = new SessionPersistence(new RomSession { Data = new byte[100] }, symbols, new ZeroPageMap(), new SegmentManager());
        var result = SymbolTools.SetSymbols(symbols, new ZeroPageMap(), persistence, hardware: false);
        Assert.False(symbols.EnabledGroups.HasFlag(SymbolGroup.Hardware));
    }
}