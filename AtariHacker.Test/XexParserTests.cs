using AtariHacker.Atari;

namespace AtariHacker.Test;

public sealed class XexParserTests
{
    [Fact]
    public void IsXex_ReturnsTrueForDataStartingWithFFFF()
    {
        var data = new byte[] { 0xFF, 0xFF, 0x00, 0x80, 0xFF, 0x9F };
        Assert.True(XexParser.IsXex(data));
    }

    [Fact]
    public void IsXex_ReturnsFalseForDataNotStartingWithFFFF()
    {
        var data = new byte[] { 0x00, 0x80, 0xFF, 0x9F };
        Assert.False(XexParser.IsXex(data));
    }

    [Fact]
    public void ParseSegments_ParsesSingleSegmentCorrectly()
    {
        var data = new byte[2 + 4 + 8192];
        data[0] = 0xFF;
        data[1] = 0xFF;
        data[2] = 0x00;
        data[3] = 0x80;
        data[4] = 0xFF;
        data[5] = 0x9F;

        var segments = XexParser.ParseSegments(data);
        Assert.Single(segments);
        Assert.Equal((ushort)0x8000, segments[0].LoadAddress);
        Assert.Equal((ushort)0x9FFF, segments[0].EndAddress);
        Assert.Equal(6, segments[0].FileOffset);
        Assert.Equal(8192, segments[0].Length);
    }

    [Fact]
    public void ParseSegments_ParsesMultiSegmentCorrectly()
    {
        var data = new byte[2 + 4 + 256 + 4 + 256];
        data[0] = 0xFF;
        data[1] = 0xFF;
        data[2] = 0x00; data[3] = 0x80;
        data[4] = 0xFF; data[5] = 0x80;
        data[2 + 4 + 256] = 0x00; data[2 + 4 + 256 + 1] = 0x81;
        data[2 + 4 + 256 + 2] = 0xFF; data[2 + 4 + 256 + 3] = 0x81;

        var segments = XexParser.ParseSegments(data);
        Assert.Equal(2, segments.Count);
        Assert.Equal((ushort)0x8000, segments[0].LoadAddress);
        Assert.Equal((ushort)0x8100, segments[1].LoadAddress);
    }

    [Fact]
    public void ParseMetadata_ReturnsEmptyForNonXexData()
    {
        var data = new byte[] { 0x00, 0x80 };
        var (segments, runAddr, initAddr) = XexParser.ParseMetadata(data);
        Assert.Empty(segments);
        Assert.Null(runAddr);
        Assert.Null(initAddr);
    }

    [Fact]
    public void FileOffsetToMemoryAddress_ConvertsCorrectly()
    {
        var segments = new List<XexSegment>
        {
            new((ushort)0x8000, (ushort)0x80FF, 6, 256)
        };

        var addr = XexParser.FileOffsetToMemoryAddress(segments, 6);
        Assert.Equal((ushort)0x8000, addr);

        addr = XexParser.FileOffsetToMemoryAddress(segments, 106);
        Assert.Equal((ushort)0x8064, addr);

        addr = XexParser.FileOffsetToMemoryAddress(segments, 262);
        Assert.Null(addr);
    }

    [Fact]
    public void FileOffsetToMemoryAddress_ReturnsNullForOffsetOutsideAnySegment()
    {
        var segments = new List<XexSegment>
        {
            new((ushort)0x8000, (ushort)0x80FF, 6, 256)
        };

        var addr = XexParser.FileOffsetToMemoryAddress(segments, 0);
        Assert.Null(addr);
    }

    [Fact]
    public void MemoryAddressToFileOffset_ConvertsCorrectly()
    {
        var segments = new List<XexSegment>
        {
            new((ushort)0x8000, (ushort)0x80FF, 6, 256)
        };

        var offset = XexParser.MemoryAddressToFileOffset(segments, (ushort)0x8000);
        Assert.Equal(6, offset);

        offset = XexParser.MemoryAddressToFileOffset(segments, (ushort)0x8064);
        Assert.Equal(106, offset);

        offset = XexParser.MemoryAddressToFileOffset(segments, (ushort)0x8100);
        Assert.Null(offset);
    }

    [Fact]
    public void MemoryAddressToFileOffset_ReturnsNullForAddressOutsideAnySegment()
    {
        var segments = new List<XexSegment>
        {
            new((ushort)0x8000, (ushort)0x80FF, 6, 256)
        };

        var offset = XexParser.MemoryAddressToFileOffset(segments, (ushort)0x7000);
        Assert.Null(offset);
    }
}
