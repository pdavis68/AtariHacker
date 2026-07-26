using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class SymbolResolverTests
{
    [Fact]
    public void Resolve_ReturnsLabelForKnownSymbolTableEntry()
    {
        var symbols = new SymbolTable();
        symbols[0x8000] = new SymbolEntry("MAIN", null, false, true, SymbolGroup.UserLabels);
        var zpMap = new ZeroPageMap();

        var label = SymbolResolver.Resolve(0x8000, symbols, zpMap);
        Assert.Equal("MAIN", label);
    }

    [Fact]
    public void Resolve_ReturnsZeroPageLabelForAddressUpTo0xFF()
    {
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();
        zpMap[0x80] = new SymbolEntry("CH", null, false, false, SymbolGroup.OsVariables);

        var label = SymbolResolver.Resolve(0x80, symbols, zpMap);
        Assert.Equal("CH", label);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenOsVariablesGroupIsDisabled()
    {
        var symbols = new SymbolTable();
        symbols.EnabledGroups = SymbolGroup.Hardware; // disable OsVariables
        var zpMap = new ZeroPageMap();
        zpMap[0x80] = new SymbolEntry("CH", null, false, false, SymbolGroup.OsVariables);

        var label = SymbolResolver.Resolve(0x80, symbols, zpMap);
        Assert.Null(label);
    }

    [Fact]
    public void Resolve_ReturnsNullForUnknownAddress()
    {
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();

        var label = SymbolResolver.Resolve(0x1234, symbols, zpMap);
        Assert.Null(label);
    }

    [Fact]
    public void ResolveEntry_ReturnsSymbolEntryForKnownAddress()
    {
        var symbols = new SymbolTable();
        symbols[0x8000] = new SymbolEntry("MAIN", null, false, true, SymbolGroup.UserLabels);
        var zpMap = new ZeroPageMap();

        var entry = SymbolResolver.ResolveEntry(0x8000, symbols, zpMap);
        Assert.NotNull(entry);
        Assert.Equal("MAIN", entry.Label);
    }

    [Fact]
    public void ResolveEntry_ReturnsNullForUnknownAddress()
    {
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();

        var entry = SymbolResolver.ResolveEntry(0x1234, symbols, zpMap);
        Assert.Null(entry);
    }
}
