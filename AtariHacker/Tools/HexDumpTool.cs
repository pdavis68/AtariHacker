using System.Text;
using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class HexDumpTool
{
    public static string HexDump(
        RomSession session,
        string offset,
        int numBytes,
        string? startAddress = null,
        bool sectorAware = false)
    {
        try
        {
            if (!session.IsLoaded)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var fileOffset = AddressParser.ParseOffset(offset);
            var addressOverride = string.IsNullOrWhiteSpace(startAddress) ? (ushort?)null : AddressParser.ParseAddress(startAddress);
            return GenerateHexDump(session, fileOffset, numBytes, addressOverride, sectorAware);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    internal static string GenerateHexDump(RomSession session, int offset, int numBytes, ushort? startAddress = null, bool sectorAware = false)
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

        // If sector-aware and the session was loaded from an ATR, try to read geometry
        AtrGeometry? geometry = null;
        if (sectorAware && !string.IsNullOrWhiteSpace(session.SourceAtrPath) && File.Exists(session.SourceAtrPath))
        {
            try
            {
                var atrBytes = File.ReadAllBytes(session.SourceAtrPath);
                if (AtrParser.IsAtr(atrBytes))
                {
                    geometry = AtrParser.ParseGeometry(atrBytes);
                }
            }
            catch
            {
                // Silently fall back to non-sector-aware dump
            }
        }

        return GenerateHexDump(session.Data.AsSpan(offset, count), offset, count, startAddress, geometry);
    }

    internal static string GenerateHexDump(byte[] data, int offset, int numBytes, ushort? startAddress = null)
    {
        var count = Math.Min(numBytes, data.Length - offset);
        return GenerateHexDump(data.AsSpan(offset, count), offset, count, startAddress);
    }

    internal static string GenerateHexDump(ReadOnlySpan<byte> span, int fileOffset, int count, ushort? startAddress = null)
    {
        return GenerateHexDump(span, fileOffset, count, startAddress, geometry: null);
    }

    internal static string GenerateHexDump(ReadOnlySpan<byte> span, int fileOffset, int count, ushort? startAddress = null, AtrGeometry? geometry = null)
    {
        if (count <= 0)
        {
            return "ERROR: Number of bytes must be greater than zero.";
        }

        var sectorAware = geometry is not null;
        List<string> lines;

        if (sectorAware)
        {
            lines = new List<string>
            {
                "Offset    Sector    Address   00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F  ASCII",
                "--------  --------  --------  -----------------------------------------------  ----------------"
            };
        }
        else
        {
            lines = new List<string>
            {
                "Offset    Address   00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F  ASCII",
                "--------  --------  -----------------------------------------------  ----------------"
            };
        }

        // Pre-calculate sector boundaries for annotation
        var sectorNotes = new Dictionary<int, string>();
        if (geometry is not null)
        {
            // Annotate ATR header boundary (first 16 bytes are the ATR header)
            if (fileOffset < 16 && fileOffset + count > 16)
            {
                // The annotation will be added at the first row after offset 16
                var noteRow = ((16 - fileOffset + 15) / 16) * 16;
                // Align to row boundary
                var alignedNoteRow = ((16 - fileOffset) + 15) / 16 * 16;
                var noteOffset = fileOffset + alignedNoteRow;
                if (noteOffset >= 16 && noteOffset < fileOffset + count)
                {
                    sectorNotes[alignedNoteRow / 16] = "(ATR header ends, sector data starts)";
                }
            }
        }

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

            var rowIndex = rowStart / 16;
            var note = sectorNotes.TryGetValue(rowIndex, out var n) ? $"  {n}" : "";

            if (sectorAware)
            {
                var sectorNumber = CalculateSectorFromOffset(geometry!, currentOffset);
                var sectorLabel = sectorNumber is > 0
                    ? $"Sctr {sectorNumber.Value,3:D3}"
                    : "       ";
                lines.Add($"{Formatting.HexOffset(currentOffset)}  {sectorLabel,-9}  {Formatting.DisplayAddress(address),-8}  {hex.ToString().TrimEnd(),-47}  {ascii}{note}");
            }
            else
            {
                lines.Add($"{Formatting.HexOffset(currentOffset)}  {Formatting.DisplayAddress(address),-8}  {hex.ToString().TrimEnd(),-47}  {ascii}{note}");
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Calculate the sector number for a given file offset into an ATR image.
    /// Returns null for offsets within the ATR header (first 16 bytes).
    /// </summary>
    private static int? CalculateSectorFromOffset(AtrGeometry geometry, int fileOffset)
    {
        if (fileOffset < 16)
            return null;

        var dataOffset = fileOffset - 16;
        var sectorSize = geometry.SectorSize;

        // First 3 sectors are always 128 bytes (even if sector size is 256)
        const int firstThreeSectorsSize = 3 * 128;

        if (dataOffset < firstThreeSectorsSize)
        {
            return 1 + (dataOffset / 128);
        }

        if (sectorSize == 128)
        {
            return 4 + ((dataOffset - firstThreeSectorsSize) / 128);
        }

        // For 256-byte sectors, sectors 4+ are 256 bytes each
        return 4 + ((dataOffset - firstThreeSectorsSize) / 256);
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
