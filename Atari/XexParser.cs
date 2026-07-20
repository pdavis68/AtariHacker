namespace AtariHackerMCP.Atari;

public sealed record XexSegment(
    ushort LoadAddress,
    ushort EndAddress,
    int FileOffset,
    int Length);

public static class XexParser
{
    public static bool IsXex(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFF;

    public static IReadOnlyList<XexSegment> ParseSegments(byte[] data)
    {
        return ParseMetadata(data).Segments;
    }

    public static (IReadOnlyList<XexSegment> Segments, ushort? RunAddress, ushort? InitAddress) ParseMetadata(byte[] data)
    {
        if (!IsXex(data))
        {
            return (Array.Empty<XexSegment>(), null, null);
        }

        var segments = new List<XexSegment>();
        ushort? runAddress = null;
        ushort? initAddress = null;
        var position = 2;

        while (position < data.Length)
        {
            if (position + 1 < data.Length && data[position] == 0xFF && data[position + 1] == 0xFF)
            {
                position += 2;
                continue;
            }

            if (position + 3 >= data.Length)
            {
                break;
            }

            var loadAddress = (ushort)(data[position] | (data[position + 1] << 8));
            var endAddress = (ushort)(data[position + 2] | (data[position + 3] << 8));
            position += 4;

            if (endAddress < loadAddress)
            {
                break;
            }

            var length = endAddress - loadAddress + 1;
            if (position + length > data.Length)
            {
                length = data.Length - position;
                if (length <= 0)
                {
                    break;
                }
            }

            if (loadAddress == 0x02E0 && length >= 2)
            {
                runAddress = (ushort)(data[position] | (data[position + 1] << 8));
            }
            else if (loadAddress == 0x02E2 && length >= 2)
            {
                initAddress = (ushort)(data[position] | (data[position + 1] << 8));
            }
            else
            {
                segments.Add(new XexSegment(loadAddress, (ushort)(loadAddress + length - 1), position, length));
            }

            position += length;
        }

        return (segments, runAddress, initAddress);
    }

    public static ushort? FileOffsetToMemoryAddress(IReadOnlyList<XexSegment> segments, int fileOffset)
    {
        foreach (var segment in segments)
        {
            if (fileOffset >= segment.FileOffset && fileOffset < segment.FileOffset + segment.Length)
            {
                return (ushort)(segment.LoadAddress + (fileOffset - segment.FileOffset));
            }
        }

        return null;
    }

    public static int? MemoryAddressToFileOffset(IReadOnlyList<XexSegment> segments, ushort memoryAddress)
    {
        foreach (var segment in segments)
        {
            if (memoryAddress >= segment.LoadAddress && memoryAddress <= segment.EndAddress)
            {
                return segment.FileOffset + (memoryAddress - segment.LoadAddress);
            }
        }

        return null;
    }
}
