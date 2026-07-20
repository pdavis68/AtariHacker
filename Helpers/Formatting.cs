namespace AtariHackerMCP.Helpers;

internal static class Formatting
{
    public static string HexByte(byte value) => $"${value:X2}";

    public static string HexWord(ushort value) => $"${value:X4}";

    public static string HexOffset(int value) => $"{value:X8}";

    public static string DisplayAddress(ushort? address) => address is null ? "--------" : HexWord(address.Value);

    public static string Printable(byte value) => value is >= 0x20 and <= 0x7E ? ((char)value).ToString() : ".";

    public static string WithSymbol(string baseText, string? symbol) => string.IsNullOrWhiteSpace(symbol) ? baseText : $"{baseText} ({symbol})";
}
