using AtariHacker.Atari;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class AtrToolsTests
{
    [Fact]
    public void AtrInfo_ReturnsErrorForNonAtrFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x01, 0x02 });
            var result = AtrTools.AtrInfo(tempFile);
            Assert.StartsWith("ERROR:", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AtrInfo_DisplaysAtrGeometryAndDirectory()
    {
        // Build a minimal ATR with DOS filesystem
        var atr = CreateMinimalDosAtr();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, atr);
            var result = AtrTools.AtrInfo(tempFile);
            Assert.Contains("ATR", result);
            Assert.Contains("SD", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ListAtrDirectory_ListsDirectoryEntries()
    {
        var atr = CreateMinimalDosAtr();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, atr);
            var result = AtrTools.ListAtrDirectory(tempFile);
            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static byte[] CreateMinimalDosAtr()
    {
        // Create a minimal 720-sector SD ATR with DOS filesystem
        var dataBytes = 720 * 128;
        var paragraphs = dataBytes / 16;
        var header = new byte[16];
        header[0] = 0x96; header[1] = 0x02;
        header[2] = (byte)(paragraphs & 0xFF);
        header[3] = (byte)((paragraphs >> 8) & 0xFF);
        header[4] = 128; header[5] = 0;

        var data = new byte[16 + dataBytes];
        Array.Copy(header, data, 16);

        // Write VTOC at sector 360
        var vtocOffset = 16 + ((360 - 1) * 128);
        data[vtocOffset] = 8;     // dir sectors
        data[vtocOffset + 1] = 0xD0; // total sectors low
        data[vtocOffset + 2] = 0x02; // total sectors high

        return data;
    }
}
