using System.Text;
using AtariHacker.Helpers;

namespace AtariHacker.Atari;

public sealed record AtrGeometry(int SectorSize, int SectorCount, string Density);

public sealed record BootHeaderInfo(
    byte Flag,
    byte SectorCount,
    ushort LoadAddress,
    ushort InitAddress,
    string? Description = null);

public sealed record AtrDirectoryEntry(
    int Index,
    string FileName,
    string Extension,
    int StartSector,
    int SectorCount,
    bool IsDeleted,
    bool IsLocked,
    bool IsBinary);

// ─── SpartaDOS types ─────────────────────────────────────────────────────

public sealed record SpartaDirectorySector(
    int SectorNumber,
    List<SpartaDirEntry> Entries,
    int NextSector
);

public sealed record SpartaDirEntry(
    byte Flags,
    ushort Time,
    byte Date,
    byte NameLength,
    string FileName,
    int StartSector,
    bool IsDeleted
);

public sealed record DiskManifest
{
    public int Sectors { get; init; }
    public string Density { get; init; } = "sd";
    public string? Filesystem { get; init; }
    public BootManifest? Boot { get; init; }
    public List<FileManifest> Files { get; init; } = [];
}

public sealed record BootManifest
{
    public string? Flag { get; init; }
    public int Sectors { get; init; } = 3;
    public string? LoadAddress { get; init; }
    public string? InitAddress { get; init; }
    public string? File { get; init; }
}

public sealed record FileManifest
{
    public string Name { get; init; } = "";
    public string File { get; init; } = "";
    public string? LoadAddress { get; init; }
}

public static class AtrParser
{
    private const int HeaderSize = 16;

    public static bool HasDosFilesystem(byte[] data)
    {
        var geometry = ParseGeometry(data);
        if (geometry.SectorCount < 368) return false;
        var vtoc = ReadSector(data, geometry, 360);
        var dirSectors = vtoc[0];
        if (dirSectors == 0 || dirSectors > 16) return false;
        var totalSectors = vtoc[1] | (vtoc[2] << 8);
        if (totalSectors == 0 || totalSectors > geometry.SectorCount) return false;
        return true;
    }

    public static bool IsAtr(byte[] data) => data.Length >= HeaderSize && data[0] == 0x96 && data[1] == 0x02;

    public static AtrGeometry ParseGeometry(byte[] data)
    {
        if (!IsAtr(data))
        {
            throw new InvalidDataException("Not a valid ATR image.");
        }

        var paragraphsLow = data[2] | (data[3] << 8);
        var paragraphsHigh = data[6] | (data[7] << 8);
        var totalParagraphs = ((uint)paragraphsHigh << 16) | (uint)paragraphsLow;
        var sectorSize = data[4] | (data[5] << 8);
        if (sectorSize is not (128 or 256))
        {
            throw new InvalidDataException($"Unsupported ATR sector size: {sectorSize}.");
        }

        var dataBytes = (int)(totalParagraphs * 16u);
        int sectorCount;
        if (sectorSize == 128)
        {
            sectorCount = dataBytes / 128;
        }
        else
        {
            sectorCount = dataBytes <= 384 ? dataBytes / 128 : 3 + ((dataBytes - 384) / 256);
        }

        var density = sectorSize switch
        {
            128 when sectorCount == 720 => "SD",
            128 when sectorCount == 1040 => "ED",
            256 when sectorCount == 720 => "DD",
            256 when sectorCount > 720 => "Extended",
            128 => "Custom-128",
            _ => "Custom-256"
        };

        return new AtrGeometry(sectorSize, sectorCount, density);
    }

    public static byte[] ReadSector(byte[] data, AtrGeometry geometry, int sectorNumber)
    {
        if (sectorNumber < 1 || sectorNumber > geometry.SectorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sectorNumber), $"Sector {sectorNumber} is outside the image.");
        }

        var length = sectorNumber <= 3 && geometry.SectorSize == 256 ? 128 : geometry.SectorSize;
        var offset = SectorFileOffset(geometry, sectorNumber);
        if (offset < HeaderSize || offset + length > data.Length)
        {
            throw new InvalidDataException($"Sector {sectorNumber} extends beyond the ATR data.");
        }

        var buffer = new byte[length];
        Buffer.BlockCopy(data, offset, buffer, 0, length);
        return buffer;
    }

    public static IReadOnlyList<AtrDirectoryEntry> ReadDirectory(byte[] data, bool atascii = false)
    {
        var geometry = ParseGeometry(data);
        var entries = new List<AtrDirectoryEntry>();

        for (var sector = 361; sector <= 368 && sector <= geometry.SectorCount; sector++)
        {
            var bytes = ReadSector(data, geometry, sector);
            for (var index = 0; index < 8; index++)
            {
                var offset = index * 16;
                var flags = bytes[offset];
                if (flags == 0)
                {
                    continue;
                }

                var sectorCount = bytes[offset + 1] | (bytes[offset + 2] << 8);
                var startSector = bytes[offset + 3] | (bytes[offset + 4] << 8);

                // Skip phantom entries with impossible sector counts
                if (sectorCount == 0 || sectorCount > geometry.SectorCount) continue;

                // Skip phantom entries with impossible sector numbers
                if (startSector == 0 || startSector > geometry.SectorCount) continue;

                var fileName = ReadPaddedString(bytes, offset + 5, 8, atascii);
                var extension = ReadPaddedString(bytes, offset + 13, 3, atascii);
                var isDeleted = (flags & 0x80) != 0;
                var isLocked = (flags & 0x20) != 0;
                var isBinary = (flags & 0x42) == 0x42;

                entries.Add(new AtrDirectoryEntry(
                    ((sector - 361) * 8) + index,
                    fileName,
                    extension,
                    startSector,
                    sectorCount,
                    isDeleted,
                    isLocked,
                    isBinary));
            }
        }

        return entries;
    }

    public static byte[] ExtractFile(byte[] data, AtrGeometry geometry, AtrDirectoryEntry entry)
    {
        var result = new List<byte>();
        var seenSectors = new HashSet<int>();
        var sector = entry.StartSector;

        while (sector != 0)
        {
            if (!seenSectors.Add(sector))
            {
                throw new InvalidDataException($"Sector chain loop detected at sector {sector}.");
            }

            var rawSector = ReadSector(data, geometry, sector);
            if (rawSector.Length < 4)
            {
                throw new InvalidDataException($"Sector {sector} is too small to contain chain metadata.");
            }

            var dataCapacity = rawSector.Length - 3;
            var usedBytes = Math.Min(rawSector[^1], dataCapacity);
            result.AddRange(rawSector.AsSpan(0, usedBytes).ToArray());

            var nextHi = rawSector[^3] & 0x03;
            var nextLo = rawSector[^2];
            sector = (nextHi << 8) | nextLo;
        }

        return result.ToArray();
    }

    public static byte[] ExtractBootSectors(byte[] data)
    {
        var geometry = ParseGeometry(data);
        using var stream = new MemoryStream();
        for (var sector = 1; sector <= 3; sector++)
        {
            stream.Write(ReadSector(data, geometry, sector));
        }

        return stream.ToArray();
    }

    public static int FreeSegmentCount(byte[] data, AtrGeometry geometry)
    {
        if (geometry.SectorCount < 360)
        {
            return 0;
        }

        var vtoc = ReadSector(data, geometry, 360);
        if (vtoc.Length >= 5)
        {
            var stored = vtoc[3] | (vtoc[4] << 8);
            if (stored > 0)
            {
                return stored;
            }
        }

        if (vtoc.Length <= 10)
        {
            return 0;
        }

        var count = 0;
        for (var i = 10; i < vtoc.Length; i++)
        {
            var value = vtoc[i];
            for (var bit = 0; bit < 8; bit++)
            {
                if ((value & (1 << bit)) != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Returns a bitmap of sector usage. true = free, false = used.
    /// Index 0 corresponds to sector 1.
    /// </summary>
    public static bool[] GetSectorBitmap(byte[] data, AtrGeometry geometry)
    {
        if (geometry.SectorCount < 360)
        {
            return Enumerable.Repeat(false, geometry.SectorCount).ToArray();
        }

        var bitmap = new bool[geometry.SectorCount];
        var vtoc = ReadSector(data, geometry, 360);

        // First 10 bytes of VTOC are header info; bitmap starts at byte 10
        if (vtoc.Length <= 10) return bitmap;

        // Sector 1 is the first bit of byte 10, but VTOC bitmap typically starts
        // at a different offset depending on the DOS version. The standard DOS 2.x
        // bitmap covers sectors 1-719 starting at VTOC byte 10.
        var bitmapOffset = 10;
        for (var sector = 0; sector < geometry.SectorCount; sector++)
        {
            var byteIndex = bitmapOffset + (sector / 8);
            if (byteIndex >= vtoc.Length) break;
            var bitIndex = sector % 8;
            bitmap[sector] = (vtoc[byteIndex] & (1 << bitIndex)) != 0;
        }

        return bitmap;
    }

    /// <summary>
    /// Find a deleted directory entry by filename (case-insensitive).
    /// Returns null if no matching deleted entry is found.
    /// </summary>
    public static AtrDirectoryEntry? FindDeletedEntry(byte[] data, string name)
    {
        var allEntries = ReadDirectory(data);
        var normalized = name.Trim().ToUpperInvariant();
        return allEntries.FirstOrDefault(entry =>
        {
            if (!entry.IsDeleted) return false;
            var fullName = string.IsNullOrWhiteSpace(entry.Extension)
                ? entry.FileName
                : $"{entry.FileName}.{entry.Extension}";
            return string.Equals(entry.FileName, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullName, normalized, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Follow the sector chain starting from startSector, returning the list
    /// of sector numbers in order. Throws on loop detection.
    /// </summary>
    public static List<int> GetSectorChain(byte[] data, AtrGeometry geometry, int startSector)
    {
        var chain = new List<int>();
        var seenSectors = new HashSet<int>();
        var sector = startSector;

        while (sector != 0)
        {
            if (!seenSectors.Add(sector))
            {
                throw new InvalidDataException($"Sector chain loop detected at sector {sector}.");
            }

            chain.Add(sector);

            var rawSector = ReadSector(data, geometry, sector);
            if (rawSector.Length < 3) break;

            var nextHi = rawSector[^3] & 0x03;
            var nextLo = rawSector[^2];
            sector = (nextHi << 8) | nextLo;
        }

        return chain;
    }

    // ─── SpartaDOS Filesystem Support ─────────────────────────────────────

    /// <summary>
    /// Detect a SpartaDOS filesystem. SpartaDOS uses a bitmap-based VTOC
    /// at sector 4 with a first-directory-sector pointer at bytes 4-5.
    /// The bitmap has specific header bytes that distinguish it from DOS 2.x.
    /// </summary>
    public static bool HasSpartaDosFilesystem(byte[] data)
    {
        var geometry = ParseGeometry(data);
        if (geometry.SectorCount < 4) return false;

        // Read the SpartaDOS VTOC at sector 4
        var vtoc = ReadSector(data, geometry, 4);
        if (vtoc.Length < 6) return false;

        // Extract first directory sector from bytes 4-5 (10-bit sector number)
        var dirSector = (vtoc[5] & 0x03) << 8 | vtoc[4];
        if (dirSector < 1 || dirSector > geometry.SectorCount) return false;

        // Directory sector must not overlap with boot sectors (1-3) or VTOC (4)
        if (dirSector <= 4) return false;

        // Check that the bitmap has at least some non-zero data
        // (a completely zero bitmap would mean no free sectors, which is unlikely
        //  for a valid filesystem, but could happen on a full disk)
        // Instead, verify the bitmap header bytes make sense
        var totalFree = vtoc[0] | (vtoc[1] << 8);
        if (totalFree > geometry.SectorCount) return false;

        return true;
    }

    /// <summary>
    /// Read the SpartaDOS directory from a disk image.
    /// SpartaDOS uses a linked list of directory sectors, each holding
    /// 16-byte entries. The first directory sector is stored in the VTOC
    /// at sector 4, bytes 4-5.
    /// </summary>
    public static List<SpartaDirEntry> ReadSpartaDirectory(byte[] data)
    {
        var geometry = ParseGeometry(data);
        var vtoc = ReadSector(data, geometry, 4);
        var firstDirSector = ((vtoc[5] & 0x03) << 8) | vtoc[4];

        var entries = new List<SpartaDirEntry>();
        var sector = firstDirSector;
        var seenSectors = new HashSet<int>();

        while (sector != 0)
        {
            if (!seenSectors.Add(sector))
                throw new InvalidDataException($"SpartaDOS directory loop detected at sector {sector}.");

            var rawSector = ReadSector(data, geometry, sector);
            if (rawSector.Length < 3) break;

            // Each directory sector has (sectorSize - 3) / 16 entries
            // The last 3 bytes are the sector chain link
            var entriesPerSector = (rawSector.Length - 3) / 16;
            for (var i = 0; i < entriesPerSector; i++)
            {
                var offset = i * 16;
                var flags = rawSector[offset];
                if (flags == 0) continue; // unused entry

                var time = (ushort)(rawSector[offset + 1] | (rawSector[offset + 2] << 8));
                var date = rawSector[offset + 3];
                var nameLength = rawSector[offset + 4];

                // Filename is at most 11 bytes (8.3 format), use NameLength
                var filenameLen = Math.Min((int)nameLength, 11);
                var fileName = Encoding.ASCII.GetString(rawSector, offset + 5, filenameLen).TrimEnd(' ', '\0');

                var startSector = (rawSector[offset + 14] & 0x03) << 8 | rawSector[offset + 13];
                var isDeleted = (flags & 0x80) != 0;

                entries.Add(new SpartaDirEntry(
                    flags,
                    time,
                    date,
                    nameLength,
                    fileName,
                    startSector,
                    isDeleted));
            }

            // Follow chain (same format as DOS 2.x)
            var nextHi = rawSector[^3] & 0x03;
            var nextLo = rawSector[^2];
            sector = (nextHi << 8) | nextLo;
        }

        return entries;
    }

    /// <summary>
    /// Returns a bitmap of sector usage for SpartaDOS filesystems.
    /// true = free, false = used.
    /// Index 0 corresponds to sector 1.
    /// SpartaDOS bitmap is stored at sector 4, starting at byte 6.
    /// </summary>
    public static bool[] GetSpartaBitmap(byte[] data, AtrGeometry geometry)
    {
        var bitmap = new bool[geometry.SectorCount];
        if (geometry.SectorCount < 4) return bitmap;

        var vtoc = ReadSector(data, geometry, 4);
        // SpartaDOS bitmap starts at byte 6
        const int bitmapOffset = 6;
        if (bitmapOffset >= vtoc.Length) return bitmap;

        for (var sector = 0; sector < geometry.SectorCount; sector++)
        {
            var byteIndex = bitmapOffset + (sector / 8);
            if (byteIndex >= vtoc.Length) break;
            var bitIndex = sector % 8;
            bitmap[sector] = (vtoc[byteIndex] & (1 << bitIndex)) != 0;
        }

        return bitmap;
    }

    /// <summary>
    /// Get the list of SpartaDOS directory sector numbers by following
    /// the chain from the first directory sector (stored in VTOC at sector 4).
    /// </summary>
    public static List<int> GetSpartaDirectorySectors(byte[] data, AtrGeometry geometry)
    {
        var vtoc = ReadSector(data, geometry, 4);
        var firstDirSector = ((vtoc[5] & 0x03) << 8) | vtoc[4];
        return GetSectorChain(data, geometry, firstDirSector);
    }

    internal static int SectorFileOffset(AtrGeometry geometry, int sectorNumber)
    {
        if (geometry.SectorSize == 256 && sectorNumber > 3)
        {
            return HeaderSize + (3 * 128) + ((sectorNumber - 4) * geometry.SectorSize);
        }

        return HeaderSize + ((sectorNumber - 1) * geometry.SectorSize);
    }

    /// <summary>
    /// Try to parse a 6-byte Atari boot header from the given data.
    /// Returns null if the data is too short or doesn't match the boot header pattern.
    /// </summary>
    public static BootHeaderInfo? TryParseBootHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            return null;

        var flag = data[0];
        if (flag != 0x00 && flag != 0xD0)
            return null;

        var sectorCount = data[1];
        var loadAddress = (ushort)(data[2] | (data[3] << 8));
        var initAddress = (ushort)(data[4] | (data[5] << 8));

        var description = flag switch
        {
            0x00 => "Continue loading",
            0xD0 => "Stop/run",
            _ => null
        };

        return new BootHeaderInfo(flag, sectorCount, loadAddress, initAddress, description);
    }

    private static string ReadPaddedString(byte[] bytes, int offset, int length, bool atascii = false)
    {
        if (atascii)
        {
            return AtasciiDecoder.Decode(bytes.AsSpan(offset, length)).TrimEnd(' ', '\0');
        }
        return Encoding.ASCII.GetString(bytes, offset, length).TrimEnd(' ', '\0');
    }
}
