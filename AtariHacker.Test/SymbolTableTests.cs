using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class SymbolTableTests
{
    [Fact]
    public void IsSymbolEnabled_ReturnsTrueForEnabledUserDefinedSymbol()
    {
        var table = new SymbolTable();
        table[0x8000] = new SymbolEntry("MAIN", null, false, true, SymbolGroup.UserLabels);
        Assert.True(table.IsSymbolEnabled(0x8000));
    }

    [Fact]
    public void IsSymbolEnabled_ReturnsFalseWhenUserLabelsGroupIsDisabled()
    {
        var table = new SymbolTable();
        table[0x8000] = new SymbolEntry("MAIN", null, false, true, SymbolGroup.UserLabels);
        table.EnabledGroups = SymbolGroup.Hardware;
        Assert.False(table.IsSymbolEnabled(0x8000));
    }

    [Fact]
    public void IsSymbolEnabled_ReturnsTrueForEnabledHardwareSymbol()
    {
        var table = new SymbolTable();
        table[0xD000] = new SymbolEntry("HPOSP0", null, true, false, SymbolGroup.Hardware);
        Assert.True(table.IsSymbolEnabled(0xD000));
    }

    [Fact]
    public void IsSymbolEnabled_ReturnsFalseWhenHardwareGroupIsDisabled()
    {
        var table = new SymbolTable();
        table[0xD000] = new SymbolEntry("HPOSP0", null, true, false, SymbolGroup.Hardware);
        table.EnabledGroups = SymbolGroup.UserLabels;
        Assert.False(table.IsSymbolEnabled(0xD000));
    }

    [Fact]
    public void IsSymbolEnabled_ReturnsFalseForUnknownAddress()
    {
        var table = new SymbolTable();
        Assert.False(table.IsSymbolEnabled(0x9999));
    }

    [Fact]
    public void GetOrderedSymbols_ReturnsSortedByAddressThenLabel()
    {
        var table = new SymbolTable();
        table[0x8002] = new SymbolEntry("ZSYM", null, false, true);
        table[0x8000] = new SymbolEntry("ASYMB", null, false, true);
        table[0x8001] = new SymbolEntry("BSYMB", null, false, true);

        var ordered = table.GetOrderedSymbols().ToList();
        Assert.Equal(0x8000, ordered[0].Key);
        Assert.Equal(0x8001, ordered[1].Key);
        Assert.Equal(0x8002, ordered[2].Key);
    }

    [Fact]
    public void EnabledGroups_DefaultsToSymbolGroupAll()
    {
        var table = new SymbolTable();
        Assert.Equal(SymbolGroup.All, table.EnabledGroups);
    }
}