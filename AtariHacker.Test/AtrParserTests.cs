using AtariHacker.Atari;
using System.IO;

namespace AtariHacker.Test;

public sealed class AtrParserTests
{
    private static byte[] CreateAtrHeader(ushort paragraphs, ushort sectorSize, ushort? paragraphsHigh = null)
    {
        var header = new byte[16];
        header[0] = 0x96;
        header[1] = 0x02;
        header[2] = (byte)(paragraphs & 0xFF);
        header[3] = (byte)((paragraphs >> 8) & 0xFF);
        header[4] = (byte)(sectorSize & 0xFF);
        header[5] = (byte)((sectorSize >> 8) & 0xFF);
        if (paragraphsHigh.HasValue)
        {
            header[6] = (byte)(paragraphsHigh.Value & 0xFF);
            header[7] = (byte)((paragraphsHigh.Value >> 8) & 0xFF);
        }
        return header;
    }

    private static byte[] CreateSdAtr(int sectorCount = 720)
    {
        var dataBytes = sectorCount * 128;
        var paragraphs = dataBytes / 16;
        var header = CreateAtrHeader((ushort)paragraphs, 128);
        var data = new byte[16 + dataBytes];
        Array.Copy(header, data, 16);
        return data;
    }

    private static byte[] CreateDdAtr(int sectorCount = 720)
    {
        var dataBytes = (3 * 128) + ((sectorCount - 3) * 256);
        var paragraphs = dataBytes / 16;
        var header = CreateAtrHeader((ushort)paragraphs, 256);
        var data = new byte[16 + dataBytes];
        Array.Copy(header, data, 16);
        return data;
    }

    [Fact]
    public void IsAtr_ReturnsTrueForValidAtrHeader()
    {
        var header = CreateAtrHeader((ushort)(720 * 128 / 16), 128);
        Assert.True(AtrParser.IsAtr(header));
    }

    [Fact]
    public void IsAtr_ReturnsFalseForDataShorterThan16Bytes()
    {
        var data = new byte[] { 0x96, 0x02 };
        Assert.False(AtrParser.IsAtr(data));
    }

    [Fact]
    public void IsAtr_ReturnsFalseForInvalidMagicBytes()
    {
        var data = new byte[16];
        Assert.False(AtrParser.IsAtr(data));
    }

    [Fact]
    public void ParseGeometry_ParsesSdCorrectly()
    {
        var atr = CreateSdAtr(720);
        var geometry = AtrParser.ParseGeometry(atr);
        Assert.Equal(128, geometry.SectorSize);
        Assert.Equal(720, geometry.SectorCount);
        Assert.Equal("SD", geometry.Density);
    }

    [Fact]
    public void ParseGeometry_ParsesDdCorrectly()
    {
        var atr = CreateDdAtr(720);
        var geometry = AtrParser.ParseGeometry(atr);
        Assert.Equal(256, geometry.SectorSize);
        Assert.Equal(720, geometry.SectorCount);
        Assert.Equal("DD", geometry.Density);
    }

    [Fact]
    public void ParseGeometry_ParsesEdCorrectly()
    {
        var atr = CreateSdAtr(1040);
        var geometry = AtrParser.ParseGeometry(atr);
        Assert.Equal(128, geometry.SectorSize);
        Assert.Equal(1040, geometry.SectorCount);
        Assert.Equal("ED", geometry.Density);
    }

    [Fact]
    public void ParseGeometry_ThrowsForUnsupportedSectorSize()
    {
        var header = CreateAtrHeader(1000, 64);
        Assert.Throws<InvalidDataException>(() => AtrParser.ParseGeometry(header));
    }

    [Fact]
    public void ParseGeometry_ThrowsForNonAtrData()
    {
        var data = new byte[16];
        Assert.Throws<InvalidDataException>(() => AtrParser.ParseGeometry(data));
    }

    [Fact]
    public void ReadSector_ReturnsCorrectBytes()
    {
        var atr = CreateSdAtr(720);
        var sectorOffset = 16 + ((10 - 1) * 128);
        atr[sectorOffset] = 0xAB;

        var sector = AtrParser.ReadSector(atr, new AtrGeometry(128, 720, "SD"), 10);
        Assert.Equal(128, sector.Length);
        Assert.Equal(0xAB, sector[0]);
    }

    [Fact]
    public void ReadSector_ThrowsForSectorZero()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");
        Assert.Throws<ArgumentOutOfRangeException>(() => AtrParser.ReadSector(atr, geometry, 0));
    }

    [Fact]
    public void ReadSector_ThrowsForSectorBeyondCount()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");
        Assert.Throws<ArgumentOutOfRangeException>(() => AtrParser.ReadSector(atr, geometry, 721));
    }

    [Fact]
    public void ReadSector_HandlesSector1To3With128ByteLengthOnDdImages()
    {
        var atr = CreateDdAtr(720);
        var geometry = new AtrGeometry(256, 720, "DD");

        var sector1 = AtrParser.ReadSector(atr, geometry, 1);
        Assert.Equal(128, sector1.Length);

        var sector4 = AtrParser.ReadSector(atr, geometry, 4);
        Assert.Equal(256, sector4.Length);
    }

    [Fact]
    public void HasDosFilesystem_DetectsDos2xCorrectly()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        var vtocOffset = 16 + ((360 - 1) * 128);
        atr[vtocOffset] = 8;
        atr[vtocOffset + 1] = 0xD0;
        atr[vtocOffset + 2] = 0x02;

        Assert.True(AtrParser.HasDosFilesystem(atr));
    }

    [Fact]
    public void HasDosFilesystem_ReturnsFalseForNonDosImages()
    {
        var atr = CreateSdAtr(100);
        Assert.False(AtrParser.HasDosFilesystem(atr));
    }

    [Fact]
    public void GetSectorChain_FollowsSectorLinksCorrectly()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        var s5Offset = 16 + ((5 - 1) * 128);
        atr[s5Offset + 125] = 0;
        atr[s5Offset + 126] = 10;

        var s10Offset = 16 + ((10 - 1) * 128);
        atr[s10Offset + 125] = 0;
        atr[s10Offset + 126] = 20;

        var s20Offset = 16 + ((20 - 1) * 128);
        atr[s20Offset + 125] = 0;
        atr[s20Offset + 126] = 0;

        var chain = AtrParser.GetSectorChain(atr, geometry, 5);
        Assert.Equal(3, chain.Count);
        Assert.Equal([5, 10, 20], chain);
    }

    [Fact]
    public void GetSectorChain_ThrowsOnLoop()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        var s5Offset = 16 + ((5 - 1) * 128);
        atr[s5Offset + 125] = 0;
        atr[s5Offset + 126] = 10;

        var s10Offset = 16 + ((10 - 1) * 128);
        atr[s10Offset + 125] = 0;
        atr[s10Offset + 126] = 5;

        Assert.Throws<InvalidDataException>(() => AtrParser.GetSectorChain(atr, geometry, 5));
    }

    // ─── TryParseBootHeader tests ───────────────────────────────────────

    [Fact]
    public void TryParseBootHeader_ReturnsNullForEmptyData()
    {
        Assert.Null(AtrParser.TryParseBootHeader(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void TryParseBootHeader_ReturnsNullForShortData()
    {
        Assert.Null(AtrParser.TryParseBootHeader(new byte[] { 0xD0, 0x03, 0x00, 0x07 }));
    }

    [Fact]
    public void TryParseBootHeader_ReturnsNullForInvalidFlag()
    {
        Assert.Null(AtrParser.TryParseBootHeader(new byte[] { 0xFF, 0x03, 0x00, 0x07, 0x40, 0x15 }));
    }

    [Fact]
    public void TryParseBootHeader_ParsesStandardBootHeader()
    {
        var data = new byte[] { 0xD0, 0x03, 0x00, 0x07, 0x40, 0x15 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0xD0, result!.Flag);
        Assert.Equal(3, result.SectorCount);
        Assert.Equal(0x0700, result.LoadAddress);
        Assert.Equal(0x1540, result.InitAddress);
    }

    [Fact]
    public void TryParseBootHeader_ParsesContinueFlag()
    {
        var data = new byte[] { 0x00, 0x03, 0x00, 0x07, 0x00, 0x07 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0x00, result!.Flag);
        Assert.Equal("Continue loading", result.Description);
    }

    [Fact]
    public void TryParseBootHeader_ParsesStopRunFlag()
    {
        var data = new byte[] { 0xD0, 0x03, 0x00, 0x07, 0x40, 0x15 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0xD0, result!.Flag);
        Assert.Equal("Stop/run", result.Description);
    }

    [Fact]
    public void TryParseBootHeader_ParsesLoadAndInitAddresses()
    {
        var data = new byte[] { 0xD0, 0x01, 0x00, 0x20, 0x00, 0x30 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0x2000, result!.LoadAddress);
        Assert.Equal(0x3000, result.InitAddress);
    }
}
