using AtariHacker.Atari;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 2: `atr directory` fails for SpartaDOS filesystems
///
/// The `ListAtrDirectory` command calls `AtrParser.HasDosFilesystem` which only
/// checks for DOS 2.x format. When a SpartaDOS disk is detected (or any non-DOS 2.x
/// disk), the command returns an error saying "No DOS 2.x filesystem detected" even
/// though the disk may have a valid, known filesystem like SpartaDOS.
///
/// Additionally, the error message is misleading — it says "custom/non-DOS layout"
/// when in fact SpartaDOS is a well-known standard.
/// </summary>
public sealed class Bug6_AtrDirectorySpartaDosTests
{
    /// <summary>
    /// Create a minimal valid ATR image with SpartaDOS-like characteristics:
    /// - 720 sectors, SD (Single Density)
    /// - Valid ATR header (16 bytes starting with $96 $02)
    /// - Boot sectors with SpartaDOS-style boot flag
    /// - NO DOS 2.x VTOC at sector 360 (which HasDosFilesystem checks for)
    /// </summary>
    private static byte[] CreateSpartaDosLikeAtr()
    {
        // ATR header: 16 bytes
        // Bytes 0-1: Magic $96 $02
        // Bytes 2-3: Image size in paragraphs (low)
        // Bytes 4-5: Sector size (128)
        // Bytes 6-7: Image size in paragraphs (high)
        // Bytes 8: Write protect flag
        // Bytes 9-15: Reserved

        // 720 sectors * 128 bytes = 92160 bytes of data
        // + 16 byte header = 92176 bytes total
        // 92176 / 16 = 5761 paragraphs = 0x1681
        var totalParagraphs = 5761; // 0x1681
        var data = new byte[92176]; // 16 + 720*128

        // ATR header
        data[0] = 0x96;
        data[1] = 0x02;
        data[2] = (byte)(totalParagraphs & 0xFF);
        data[3] = (byte)((totalParagraphs >> 8) & 0xFF);
        data[4] = 128; // sector size low
        data[5] = 0;   // sector size high
        data[6] = 0;   // paragraphs high low
        data[7] = 0;   // paragraphs high high

        // Write a boot header in sector 1 (bytes 16-21, offset 0x10)
        // Boot flag $D0 (stop/run), 3 sectors, load $0700, init $1540
        data[16] = 0xD0; // boot flag
        data[17] = 0x03; // sector count
        data[18] = 0x00; // load address low
        data[19] = 0x07; // load address high
        data[20] = 0x40; // init address low
        data[21] = 0x15; // init address high

        // Do NOT write a DOS 2.x VTOC at sector 360
        // This means HasDosFilesystem will return false

        return data;
    }

    [Fact]
    public void ListAtrDirectory_NonDosDisk_ReturnsErrorMessage()
    {
        // Create a temp ATR file
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateSpartaDosLikeAtr();
            File.WriteAllBytes(tempFile, data);

            var result = AtrTools.ListAtrDirectory(tempFile);

            // The current behavior returns an error about DOS 2.x not found
            Assert.Contains("ERROR", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No DOS 2.x", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasDosFilesystem_NonDosDisk_ReturnsFalse()
    {
        var data = CreateSpartaDosLikeAtr();
        Assert.False(AtrParser.HasDosFilesystem(data));
    }

    [Fact]
    public void AtrParser_IsValidAtr_ReturnsTrue()
    {
        var data = CreateSpartaDosLikeAtr();
        Assert.True(AtrParser.IsAtr(data));
    }

    [Fact]
    public void AtrInfo_NonDosDisk_ShowsNoDosMessage()
    {
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateSpartaDosLikeAtr();
            File.WriteAllBytes(tempFile, data);

            var result = AtrTools.AtrInfo(tempFile);

            // Should say no DOS 2.x filesystem detected
            Assert.Contains("No DOS 2.x", result);
            // Should NOT show directory listing (no DOS 2.x)
            Assert.DoesNotContain("Directory:", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}