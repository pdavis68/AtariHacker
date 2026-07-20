using System.Text;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static class HexDumpTool
{
    public static string HexDump(
        RomSession session,
        string offset,
        int numBytes,
        string? startAddress = null)
    {
        try
        {
            if (!session.IsLoaded)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var fileOffset = AddressParser.ParseOffset(offset);
            var addressOverride = string.IsNullOrWhiteSpace(startAddress) ? (ushort?)null : AddressParser.ParseAddress(startAddress);
            return GenerateHexDump(session, fileOffset, numBytes, addressOverride);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    internal static string GenerateHexDump(RomSession session, int offset, int numBytes, ushort? startAddress = null)
    {
        if (!session.IsLoaded || session.Data is null)
        {
            return "ERROR: No ROM is currently loaded. Use LoadRom first.";
        }

        if (offset < 0 || offset >= session.Length)
        {
            return $"ERROR: Offset 0x{offset:X} exceeds ROM size (0x{session.Length:X} bytes).";
        }

        if (numBytes <= 0)
        {
            return "ERROR: Number of bytes must be greater than zero.";
        }

        var count = Math.Min(numBytes, session.Length - offset);
        return GenerateHexDump(session.Data.AsSpan(offset, count), offset, count, startAddress);
    }

    internal static string GenerateHexDump(byte[] data, int offset, int numBytes, ushort? startAddress = null)
    {
        var count = Math.Min(numBytes, data.Length - offset);
        return GenerateHexDump(data.AsSpan(offset, count), offset, count, startAddress);
    }

    internal static string GenerateHexDump(ReadOnlySpan<byte> span, int fileOffset, int count, ushort? startAddress = null)
    {
        if (count <= 0)
        {
            return "ERROR: Number of bytes must be greater than zero.";
        }

        var lines = new List<string>
        {
            "Offset    Address   00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F  ASCII",
            "--------  --------  -----------------------------------------------  ----------------"
        };

        for (var rowStart = 0; rowStart < count; rowStart += 16)
        {
            var currentOffset = fileOffset + rowStart;
            var rowCount = Math.Min(16, count - rowStart);
            var address = startAddress is null
                ? (ushort?)null
                : (ushort)(startAddress.Value + rowStart);

            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            for (var i = 0; i < 16; i++)
            {
                if (i < rowCount)
                {
                    var value = span[rowStart + i];
                    hex.Append(value.ToString("X2")).Append(' ');
                    ascii.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
                }
                else
                {
                    hex.Append("   ");
                    ascii.Append(' ');
                }
            }

            lines.Add($"{Formatting.HexOffset(currentOffset)}  {Formatting.DisplayAddress(address),-8}  {hex.ToString().TrimEnd(),-47}  {ascii}");
        }

        return string.Join('\n', lines);
    }

    internal static string GenerateHexDumpWithCustomLabels(ReadOnlySpan<byte> span, int fileOffset, int count, Func<int, string> addressLabel)
    {
        if (count <= 0)
        {
            return "ERROR: Number of bytes must be greater than zero.";
        }

        var lines = new List<string>
        {
            "Offset    Address   00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F  ASCII",
            "--------  ---------  -----------------------------------------------  ----------------"
        };

        for (var rowStart = 0; rowStart < count; rowStart += 16)
        {
            var currentOffset = fileOffset + rowStart;
            var rowCount = Math.Min(16, count - rowStart);
            var label = addressLabel(currentOffset);

            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            for (var i = 0; i < 16; i++)
            {
                if (i < rowCount)
                {
                    var value = span[rowStart + i];
                    hex.Append(value.ToString("X2")).Append(' ');
                    ascii.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
                }
                else
                {
                    hex.Append("   ");
                    ascii.Append(' ');
                }
            }

            lines.Add($"{Formatting.HexOffset(currentOffset)}  {label,-9}  {hex.ToString().TrimEnd(),-47}  {ascii}");
        }

        return string.Join('\n', lines);
    }
}
