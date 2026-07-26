using AtariHacker.Atari;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 5: `info` command shows only boot sectors, not full disk
///
/// The `info` command (implemented via `FileTools.RomInfo`) shows information about
/// the currently loaded session data. When an ATR file is loaded via the `-t` flag,
/// `LoadTarget` in `Program.cs` calls `AtrTools.LoadAtrBoot`, which only extracts
/// the boot sectors (3 sectors = 384 bytes). The `info` command then shows this
/// boot sector data as if it were the full disk, showing only 384 bytes instead of
/// the full disk image size.
/// </summary>
public sealed class Bug9_InfoCommandShowsOnlyBootSectorsTests
{
    /// <summary>
    /// Create a minimal valid ATR image with 720 sectors
    /// </summary>
    private static byte[] CreateAtrImage()
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

        // Write a boot header in sector 1
        data[16] = 0xD0;
        data[17] = 0x03;
        data[18] = 0x00;
        data[19] = 0x07;
        data[20] = 0x40;
        data[21] = 0x15;

        // The ATR has 92160 bytes of data, but only 384 bytes (boot sectors)
        // will be loaded into the session by LoadAtrBoot

        return data;
    }

    [Fact]
    public void LoadAtrBoot_OnlyLoadsBootSectors()
    {
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateAtrImage();
            File.WriteAllBytes(tempFile, data);

            var session = new RomSession();
            var persistence = new SessionPersistence(session, new SymbolTable(), new ZeroPageMap(), new SegmentManager());

            var result = AtrTools.LoadAtrBoot(session, persistence, tempFile);

            // The boot sectors are only 384 bytes
            Assert.Equal(384, session.Length);
            Assert.Contains("Loaded ATR boot sectors", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void RomInfo_AfterLoadAtrBoot_ShowsBootSectorSize()
    {
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateAtrImage();
            File.WriteAllBytes(tempFile, data);

            var session = new RomSession();
            var symbols = new SymbolTable();
            var zeroPageMap = new ZeroPageMap();
            var persistence = new SessionPersistence(session, symbols, zeroPageMap, new SegmentManager());

            AtrTools.LoadAtrBoot(session, persistence, tempFile);

            // Now run the info command
            var result = FileTools.RomInfo(session, symbols, zeroPageMap);

            // The file size should be the boot sector size (384 bytes)
            // Bug: The info command shows only boot sector info, not the full disk
            Assert.Contains("384 bytes", result);
            // The file path should end with /BOOT (the synthetic path)
            Assert.Contains("/BOOT", result);
            // The format should be "Raw binary (base address set)"
            Assert.Contains("base address", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void RomInfo_AfterLoadAtrBoot_DoesNotShowFullDiskSize()
    {
        var tempFile = Path.GetTempFileName() + ".atr";
        try
        {
            var data = CreateAtrImage();
            File.WriteAllBytes(tempFile, data);

            var session = new RomSession();
            var symbols = new SymbolTable();
            var zeroPageMap = new ZeroPageMap();
            var persistence = new SessionPersistence(session, symbols, zeroPageMap, new SegmentManager());

            AtrTools.LoadAtrBoot(session, persistence, tempFile);

            var result = FileTools.RomInfo(session, symbols, zeroPageMap);

            // The full disk size (92160 bytes) should NOT appear in the info output
            // because the session only has the boot sectors loaded
            Assert.DoesNotContain("92160", result);
            Assert.DoesNotContain("720 sectors", result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}