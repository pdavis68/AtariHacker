using AtariHacker.Helpers;

namespace AtariHacker.Test;

public sealed class AddressParserTests
{
    [Fact]
    public void ParseAddress_ParsesDecimalStringCorrectly()
    {
        var result = AddressParser.ParseAddress("32768");
        Assert.Equal(0x8000, result);
    }

    [Fact]
    public void ParseAddress_ParsesHexWithDollarPrefix()
    {
        var result = AddressParser.ParseAddress("$8000");
        Assert.Equal(0x8000, result);
    }

    [Fact]
    public void ParseAddress_ParsesHexWith0xPrefix()
    {
        var result = AddressParser.ParseAddress("0x8000");
        Assert.Equal(0x8000, result);
    }

    [Fact]
    public void ParseAddress_ThrowsFormatExceptionForEmptyString()
    {
        Assert.Throws<FormatException>(() => AddressParser.ParseAddress(""));
    }

    [Fact]
    public void ParseAddress_ThrowsFormatExceptionForInvalidFormat()
    {
        Assert.Throws<FormatException>(() => AddressParser.ParseAddress("not-a-number"));
    }

    [Fact]
    public void ParseAddress_ThrowsFormatExceptionForValueGreaterThan0xFFFF()
    {
        Assert.Throws<FormatException>(() => AddressParser.ParseAddress("$10000"));
    }

    [Fact]
    public void ParseOffset_ParsesDecimalValueCorrectly()
    {
        var result = AddressParser.ParseOffset("100");
        Assert.Equal(100, result);
    }

    [Fact]
    public void ParseOffset_ParsesHexValueCorrectly()
    {
        var result = AddressParser.ParseOffset("$100");
        Assert.Equal(256, result);
    }

    [Fact]
    public void ParseOffset_ThrowsFormatExceptionForNegativeValues()
    {
        Assert.Throws<FormatException>(() => AddressParser.ParseOffset("-1"));
    }

    [Fact]
    public void ParseZeroPageAddress_ParsesValuesCorrectly()
    {
        var result = AddressParser.ParseZeroPageAddress("$FF");
        Assert.Equal(0xFF, result);

        result = AddressParser.ParseZeroPageAddress("0");
        Assert.Equal(0, result);
    }

    [Fact]
    public void ParseZeroPageAddress_ThrowsFormatExceptionForValueGreaterThan0xFF()
    {
        Assert.Throws<FormatException>(() => AddressParser.ParseZeroPageAddress("$100"));
    }
}
