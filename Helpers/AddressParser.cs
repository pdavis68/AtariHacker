using System.Globalization;
using System.Text.RegularExpressions;

namespace AtariHackerMCP.Helpers;

internal static partial class AddressParser
{
    public static ushort ParseAddress(string input)
    {
        var value = ParseInteger(input, allowNegative: false, 0xFFFF, "address");
        return checked((ushort)value);
    }

    public static int ParseOffset(string input)
    {
        return ParseInteger(input, allowNegative: false, int.MaxValue, "offset");
    }

    public static byte ParseZeroPageAddress(string input)
    {
        var value = ParseInteger(input, allowNegative: false, 0xFF, "zero page address");
        return checked((byte)value);
    }

    private static int ParseInteger(string input, bool allowNegative, int maxValue, string kind)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new FormatException($"Invalid {kind}: value is empty.");
        }

        var trimmed = input.Trim();
        var isHex = trimmed.StartsWith('$') || trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || HexDigitsRegex().IsMatch(trimmed);
        var normalized = trimmed.TrimStart('$');
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        NumberStyles style = isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer;
        if (!int.TryParse(normalized, style, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"Invalid {kind}: '{input}'.");
        }

        if (!allowNegative && value < 0)
        {
            throw new FormatException($"Invalid {kind}: '{input}'. Negative values are not allowed.");
        }

        if (value > maxValue)
        {
            throw new FormatException($"Invalid {kind}: '{input}'. Maximum allowed is {maxValue}.");
        }

        return value;
    }

    [GeneratedRegex("[A-Fa-f]")]
    private static partial Regex HexDigitsRegex();
}
