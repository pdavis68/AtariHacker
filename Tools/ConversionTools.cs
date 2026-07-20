using AtariHackerMCP.Helpers;

namespace AtariHackerMCP.Tools;

public static class ConversionTools
{
    public static string HexToDecimal(string hex)
    {
        try
        {
            var value = AddressParser.ParseOffset(hex);
            return $"{Formatting.HexWord((ushort)Math.Min(value, 0xFFFF))} = {value}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string DecimalToHex(int value)
    {
        try
        {
            if (value < 0)
            {
                return "ERROR: Decimal value must be non-negative.";
            }

            return $"{value} = ${value:X}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }
}
