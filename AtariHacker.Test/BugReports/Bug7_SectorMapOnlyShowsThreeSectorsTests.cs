using AtariHacker.Atari;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 3: `sector-map` only shows 3 sectors used
///
/// The `SectorMap` command calls `AtrParser.HasDosFilesystem` to determine if the
/// disk has a DOS 2.x filesystem. When the disk doesn't have DOS 2.x (e.g., it's
/// SpartaDOS), the `BuildSectorInfo` method only marks sectors 1-3 as `Boot` and
/// everything else as `Free`. This is misleading because the disk may have valid
/// data in sectors beyond 3.
/// </summary>
public sealed class Bug7_SectorMapOnlyShowsThreeSectorsTests
{
    /// <summary>
    /// Create a valid ATR with SpartaDOS-like data:
    /// - 720 sectors, SD
    /// - Valid ATR header
    /// - Boot sectors with data
    /// - Data in sectors 4-367 (which would be a SpartaDOS filesystem)
    /// - NO DOS 2.x VTOC at sector 360
    /// </summary>
    private static byte[] CreateAtrWithDataBeyondBoot()
    {
        // 720 sectors * 128 bytes = 92160 bytes of data + 16 header = 92176
        var totalParagraphs = 5761; // 0x1681
        var data = new byte[92176];

        // ATR header
        data[0] = 0x96;
        data[1] = 0x02;
        data[2] = (byte)(totalParagraphs & 0xFF);
        data[3] = (byte)((totalParagraphs >> 8) & 0xFF);
        data[4] = 128; // sector size low
        data[5] = 0;   // sector size high
        data[6] = 0;
        data[7] = 0;

        // Boot sector 1 (file offset 16): boot header + some code
        data[16] = 0xD0; // boot flag
        data[17] = 0x03; // sector count
        data[18] = 0x00; // load address low
        data[19] = 0x07; // load address high
        data[20] = 0x40; // init address low
        data[21] = 0x15; // init address high

        // Fill boot sectors with some non-zero data to indicate actual content
        // Sector 1: bytes 16-143
        for (int i = 16; i < 144; i++) data[i] = 0xA9; // LDA #imm
        // Sector 2: bytes 144-271
        for (int i = 144; i < 272; i++) data[i] = 0x60; // RTS
        // Sector 3: bytes 272-399
        for (int i = 272; i < 400; i++) data[i] = 0xEA; // NOP

        // Fill sectors 4-367 (file offset 400 to 400 + 364*128 = 400 + 46592 = 46992)
        // with non-zero data to simulate a SpartaDOS filesystem with data
        for (int i = 400; i < 46992; i++) data[i] = 0x42; // non-zero data

        // Do NOT write a DOS 2.x VTOC at sector 360
        // Sector 360 would be at file offset: 16 + (360-1)*128 = 16 + 45952 = 45968
        // Leave it as zero so HasDosFilesystem returns false

        return data;
    }

    [Fact]
    public void SectorMap_NonDosDisk_ShowsOnlyThreeSectorsUsed()
    {
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateAtrWithDataBeyondBoot();
            File.WriteAllBytes(tempFile, data);

            var result = AtrForensicTools.SectorMap(tempFile);

            // Bug: Report shows only 3 sectors used even though there's data
            // in sectors 4-367
            Assert.Contains("Usage: 3", result);
            Assert.Contains("Sectors 001-003:", result);
            // 004-720 should be shown as Free (the bug)
            Assert.Contains("Sectors 004-720:", result);
            Assert.Contains("Free", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void SectorMap_NonDosDisk_DoesNotShowDataSectors()
    {
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateAtrWithDataBeyondBoot();
            File.WriteAllBytes(tempFile, data);

            var result = AtrForensicTools.SectorMap(tempFile);

            // The sector map should NOT show any file data sectors
            // because there's no DOS 2.x filesystem to parse
            Assert.DoesNotContain("File data", result);
            Assert.DoesNotContain("VTOC", result);
            Assert.DoesNotContain("Directory", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void SectorMap_NonDosDisk_UsagePercentageReflectsOnlyBoot()
    {
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateAtrWithDataBeyondBoot();
            File.WriteAllBytes(tempFile, data);

            var result = AtrForensicTools.SectorMap(tempFile);

            // The usage percentage should be 0.4% (3/720)
            // This is misleading because there's actually data in 367 sectors
            Assert.Contains("0.4%", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasDosFilesystem_ReturnsFalseForNonDosDisk()
    {
        var data = CreateAtrWithDataBeyondBoot();
        Assert.False(AtrParser.HasDosFilesystem(data));
    }

    [Fact]
    public void IsAtr_ReturnsTrueForValidAtr()
    {
        var data = CreateAtrWithDataBeyondBoot();
        Assert.True(AtrParser.IsAtr(data));
    }
}