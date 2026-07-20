using System.Text;
using AtariHackerMCP.Helpers;

namespace AtariHackerMCP.Atari;

public sealed record AtrGeometry(int SectorSize, int SectorCount, string Density);

public sealed record AtrDirectoryEntry(
    int Index,
    string FileName,
    string Extension,
    int StartSector,
    int SectorCount,
    bool IsDeleted,
    bool IsLocked,
    bool IsBinary);

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

    internal static int SectorFileOffset(AtrGeometry geometry, int sectorNumber)
    {
        if (geometry.SectorSize == 256 && sectorNumber > 3)
        {
            return HeaderSize + (3 * 128) + ((sectorNumber - 4) * geometry.SectorSize);
        }

        return HeaderSize + ((sectorNumber - 1) * geometry.SectorSize);
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
