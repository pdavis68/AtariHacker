using AtariHacker.Helpers;

namespace AtariHacker.Test;

public sealed class FormattingTests
{
    [Fact]
    public void HexByte_FormatsByteAsDollarXX()
    {
        Assert.Equal("$00", Formatting.HexByte(0));
        Assert.Equal("$FF", Formatting.HexByte(255));
        Assert.Equal("$0A", Formatting.HexByte(10));
        Assert.Equal("$A0", Formatting.HexByte(0xA0));
    }

    [Fact]
    public void HexWord_FormatsUshortAsDollarXXXX()
    {
        Assert.Equal("$0000", Formatting.HexWord(0));
        Assert.Equal("$FFFF", Formatting.HexWord(0xFFFF));
        Assert.Equal("$8000", Formatting.HexWord(0x8000));
        Assert.Equal("$D012", Formatting.HexWord(0xD012));
    }

    [Fact]
    public void HexOffset_FormatsIntAsXXXXXXXX()
    {
        Assert.Equal("00000000", Formatting.HexOffset(0));
        Assert.Equal("0000000A", Formatting.HexOffset(10));
        Assert.Equal("00010000", Formatting.HexOffset(0x10000));
    }

    [Fact]
    public void DisplayAddress_FormatsAddressOrDashDashForNull()
    {
        Assert.Equal("$8000", Formatting.DisplayAddress(0x8000));
        Assert.Equal("--------", Formatting.DisplayAddress(null));
    }

    [Fact]
    public void Printable_ReturnsCharacterForPrintableAscii()
    {
        Assert.Equal("A", Formatting.Printable(0x41));
        Assert.Equal(" ", Formatting.Printable(0x20));
        Assert.Equal("~", Formatting.Printable(0x7E));
    }

    [Fact]
    public void Printable_ReturnsDotForNonPrintable()
    {
        Assert.Equal(".", Formatting.Printable(0x00));
        Assert.Equal(".", Formatting.Printable(0x1F));
        Assert.Equal(".", Formatting.Printable(0x7F));
        Assert.Equal(".", Formatting.Printable(0xFF));
    }

    [Fact]
    public void WithSymbol_AppendsSymbolInParentheses()
    {
        Assert.Equal("$D012 (COLPM0)", Formatting.WithSymbol("$D012", "COLPM0"));
        Assert.Equal("$D012", Formatting.WithSymbol("$D012", null));
        Assert.Equal("$D012", Formatting.WithSymbol("$D012", ""));
        Assert.Equal("$D012", Formatting.WithSymbol("$D012", "  "));
    }
}
