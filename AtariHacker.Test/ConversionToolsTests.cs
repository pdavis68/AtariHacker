using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class ConversionToolsTests
{
    [Fact]
    public void HexToDecimal_ConvertsHexStringToDecimal()
    {
        var result = ConversionTools.HexToDecimal("FF");
        Assert.Contains("255", result);
    }

    [Fact]
    public void HexToDecimal_HandlesDollarPrefix()
    {
        var result = ConversionTools.HexToDecimal("$FF");
        Assert.Contains("255", result);
    }

    [Fact]
    public void HexToDecimal_Handles0xPrefix()
    {
        var result = ConversionTools.HexToDecimal("0xFF");
        Assert.Contains("255", result);
    }

    [Fact]
    public void DecimalToHex_ConvertsDecimalToHexString()
    {
        var result = ConversionTools.DecimalToHex(255);
        Assert.Contains("$FF", result);
    }

    [Fact]
    public void DecimalToHex_ReturnsErrorForNegativeValues()
    {
        var result = ConversionTools.DecimalToHex(-1);
        Assert.StartsWith("ERROR:", result);
    }
}