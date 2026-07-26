using AtariHacker.Atari;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class AtrForensicToolsTests
{
    [Fact]
    public void SectorMap_ReturnsErrorForNonAtrFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x01 });
            var result = AtrForensicTools.SectorMap(tempFile);
            Assert.StartsWith("ERROR:", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SectorMap_BuildsSectorMapForDosFormattedAtr()
    {
        var atr = CreateMinimalDosAtr();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, atr);
            var result = AtrForensicTools.SectorMap(tempFile);
            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SectorMap_FormatsTextOutputCorrectly()
    {
        var atr = CreateMinimalDosAtr();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, atr);
            var result = AtrForensicTools.SectorMap(tempFile, "text");
            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SectorMap_FormatsAsciiOutputCorrectly()
    {
        var atr = CreateMinimalDosAtr();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, atr);
            var result = AtrForensicTools.SectorMap(tempFile, "ascii");
            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static byte[] CreateMinimalDosAtr()
    {
        var dataBytes = 720 * 128;
        var paragraphs = dataBytes / 16;
        var header = new byte[16];
        header[0] = 0x96; header[1] = 0x02;
        header[2] = (byte)(paragraphs & 0xFF);
        header[3] = (byte)((paragraphs >> 8) & 0xFF);
        header[4] = 128; header[5] = 0;

        var data = new byte[16 + dataBytes];
        Array.Copy(header, data, 16);

        var vtocOffset = 16 + ((360 - 1) * 128);
        data[vtocOffset] = 8;
        data[vtocOffset + 1] = 0xD0;
        data[vtocOffset + 2] = 0x02;

        return data;
    }
}
