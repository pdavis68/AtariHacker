using System.Text.Json;
using AtariHacker.Atari;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class AtrWriteToolsTests
{
    [Fact]
    public void ExtractAtrFile_ReturnsErrorForNonExistentFile()
    {
        var result = AtrWriteTools.ExtractAtrFile("/nonexistent/file.atr", "TEST", "/tmp/output.bin");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void InjectAtrFile_ReturnsErrorForNonExistentAtr()
    {
        var result = AtrWriteTools.InjectAtrFile("/nonexistent/file.atr", "TEST", "/tmp/input.bin");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void CreateAtr_CreatesBlankAtrWithCorrectHeader()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd");
            Assert.DoesNotContain("ERROR", result);

            var data = File.ReadAllBytes(tempFile);
            Assert.True(AtrParser.IsAtr(data));
            var geo = AtrParser.ParseGeometry(data);
            Assert.Equal(128, geo.SectorSize);
            Assert.Equal(720, geo.SectorCount);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_ValidatesDensityParameter()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "invalid");
            Assert.StartsWith("ERROR:", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── Manifest Parsing Tests ──────────────────────────────────────────

    [Fact]
    public void CreateAtr_WithManifest_CreatesDiskWithCorrectSectors()
    {
        var tempFile = Path.GetTempFileName();
        var manifestFile = Path.GetTempFileName();
        try
        {
            var manifest = new DiskManifest
            {
                Sectors = 720,
                Density = "sd",
            };
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest));

            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 0, density: "sd", manifest: manifestFile);
            Assert.DoesNotContain("ERROR", result);

            var data = File.ReadAllBytes(tempFile);
            Assert.True(AtrParser.IsAtr(data));
            var geo = AtrParser.ParseGeometry(data);
            Assert.Equal(128, geo.SectorSize);
            Assert.Equal(720, geo.SectorCount);
        }
        finally
        {
            File.Delete(tempFile);
            File.Delete(manifestFile);
        }
    }

    [Fact]
    public void CreateAtr_WithManifest_MissingFileThrowsError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", manifest: "/nonexistent/manifest.json");
            Assert.StartsWith("ERROR:", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_WithFilesystemOptionDos2_CreatesFormattedDisk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2");
            Assert.DoesNotContain("ERROR", result);

            var data = File.ReadAllBytes(tempFile);
            Assert.True(AtrParser.HasDosFilesystem(data));

            // Verify VTOC sector (sector 360) has correct format
            var geo = AtrParser.ParseGeometry(data);
            var vtoc = AtrParser.ReadSector(data, geo, 360);
            Assert.Equal(8, vtoc[0]); // 8 directory sectors
            Assert.Equal(720, vtoc[1] | (vtoc[2] << 8)); // total sectors
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_WithFilesystemOptionDos2_HasFreeSectors()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2");

            var data = File.ReadAllBytes(tempFile);
            var freeCount = AtrParser.FreeSegmentCount(data, AtrParser.ParseGeometry(data));
            Assert.True(freeCount > 0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_WithFilesystemOptionDos2_HasBootSectors()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2");

            var data = File.ReadAllBytes(tempFile);
            var boot = AtrParser.ExtractBootSectors(data);
            Assert.Equal(384, boot.Length); // 3 sectors x 128 bytes
            Assert.Equal(0, boot[0]); // boot flag = 0 (continue loading)
            Assert.Equal(3, boot[1]); // 3 sectors to load
            Assert.Equal(0x00, boot[2]); // load address low byte
            Assert.Equal(0x07, boot[3]); // load address high byte ($0700)
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── SpartaDOS Filesystem Tests ──────────────────────────────────────

    [Fact]
    public void CreateAtr_WithFilesystemOptionSpartaDos_CreatesFormattedDisk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "spartados");
            Assert.DoesNotContain("ERROR", result);

            var data = File.ReadAllBytes(tempFile);
            Assert.True(AtrParser.HasSpartaDosFilesystem(data));

            // Verify SpartaDOS VTOC sector (sector 4) has correct format
            var geo = AtrParser.ParseGeometry(data);
            var vtoc = AtrParser.ReadSector(data, geo, 4);
            var firstDirSector = ((vtoc[5] & 0x03) << 8) | vtoc[4];
            Assert.Equal(5, firstDirSector);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_WithFilesystemOptionSpartaDos_HasEmptyDirectory()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "spartados");

            var data = File.ReadAllBytes(tempFile);
            var dirEntries = AtrParser.ReadSpartaDirectory(data);
            Assert.Empty(dirEntries);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_WithFilesystemOptionSpartaDos_HasBootSectors()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "spartados");

            var data = File.ReadAllBytes(tempFile);
            var boot = AtrParser.ExtractBootSectors(data);
            Assert.Equal(384, boot.Length);
            Assert.Equal(0, boot[0]); // boot flag = 0
            Assert.Equal(3, boot[1]); // 3 sectors
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── Integration Tests ────────────────────────────────────────────────

    [Fact]
    public void CreateAtr_Dos2_VerifyWithAtrInfo()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2");

            var infoResult = AtrTools.AtrInfo(tempFile);
            Assert.DoesNotContain("ERROR", infoResult);
            Assert.Contains("720", infoResult);
            Assert.Contains("Free", infoResult);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_SpartaDos_VerifyWithAtrInfo()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "spartados");

            var infoResult = AtrTools.AtrInfo(tempFile);
            Assert.DoesNotContain("ERROR", infoResult);
            Assert.Contains("SpartaDOS", infoResult, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_Dos2_VerifyDirectoryEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2");

            var dirResult = AtrTools.ListAtrDirectory(tempFile);
            Assert.DoesNotContain("ERROR", dirResult);
            Assert.Contains("0 files", dirResult);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_SpartaDos_VerifyDirectoryEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "spartados");

            var dirResult = AtrTools.ListAtrDirectory(tempFile);
            Assert.DoesNotContain("ERROR", dirResult);
            Assert.Contains("0 files", dirResult);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_DryRun_DoesNotCreateFile()
    {
        var tempFile = Path.GetTempFileName();
        File.Delete(tempFile); // Remove the file created by GetTempFileName
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2", dryRun: true);
            Assert.Contains("DRY RUN", result);
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_DryRunWithManifest_DoesNotCreateFile()
    {
        var tempFile = Path.GetTempFileName();
        File.Delete(tempFile);
        var manifestFile = Path.GetTempFileName();
        try
        {
            var manifest = new DiskManifest
            {
                Sectors = 720,
                Density = "sd",
                Filesystem = "dos2"
            };
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest));

            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 0, density: "sd", manifest: manifestFile, dryRun: true);
            Assert.Contains("DRY RUN", result);
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            File.Delete(manifestFile);
        }
    }

    [Fact]
    public void CreateAtr_Dos2_VerifyDirectoryAndInfoCommands()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2");

            // Verify directory command
            var dirResult = AtrTools.ListAtrDirectory(tempFile);
            Assert.DoesNotContain("ERROR", dirResult);

            // Verify header command
            var headerResult = AtrTools.AtrHeader(tempFile);
            Assert.DoesNotContain("ERROR", headerResult);
            Assert.Contains("720", headerResult);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_SpartaDos_VerifyBitmapCorrect()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "spartados");

            var data = File.ReadAllBytes(tempFile);
            var geo = AtrParser.ParseGeometry(data);
            var bitmap = AtrParser.GetSpartaBitmap(data, geo);

            // Sectors 1-5 should be used (false)
            Assert.False(bitmap[0]); // sector 1 (boot)
            Assert.False(bitmap[1]); // sector 2 (boot)
            Assert.False(bitmap[2]); // sector 3 (boot)
            Assert.False(bitmap[3]); // sector 4 (VTOC)
            Assert.False(bitmap[4]); // sector 5 (directory)
            // Sector 6 should be free
            Assert.True(bitmap[5]); // sector 6
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_Dos2_VerifyBitmapCorrect()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "sd", filesystem: "dos2");

            var data = File.ReadAllBytes(tempFile);
            var geo = AtrParser.ParseGeometry(data);
            var bitmap = AtrParser.GetSectorBitmap(data, geo);

            // Sectors 1-3 should be used (false) = boot
            Assert.False(bitmap[0]); // sector 1
            Assert.False(bitmap[1]); // sector 2
            Assert.False(bitmap[2]); // sector 3
            // Sector 360 should be used = VTOC
            Assert.False(bitmap[359]); // sector 360
            // Sectors 361-368 should be used = directory
            Assert.False(bitmap[360]); // sector 361
            Assert.False(bitmap[367]); // sector 368
            // Sector 369 should be free
            Assert.True(bitmap[368]); // sector 369
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_WithManifestAndFilesystem_InitializesCorrectFs()
    {
        var tempFile = Path.GetTempFileName();
        var manifestFile = Path.GetTempFileName();
        try
        {
            var manifest = new DiskManifest
            {
                Sectors = 720,
                Density = "sd",
                Filesystem = "dos2"
            };
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest));

            AtrWriteTools.CreateAtr(tempFile, sectors: 0, density: "sd", manifest: manifestFile);

            var data = File.ReadAllBytes(tempFile);
            Assert.True(AtrParser.HasDosFilesystem(data));
            Assert.False(AtrParser.HasSpartaDosFilesystem(data));
        }
        finally
        {
            File.Delete(tempFile);
            File.Delete(manifestFile);
        }
    }

    [Fact]
    public void CreateAtr_Dos2_WithDensityDd_CreatesDoubleDensityDisk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "dd", filesystem: "dos2");
            Assert.DoesNotContain("ERROR", result);

            var data = File.ReadAllBytes(tempFile);
            var geo = AtrParser.ParseGeometry(data);
            Assert.Equal(256, geo.SectorSize);
            Assert.Equal(720, geo.SectorCount);
            Assert.True(AtrParser.HasDosFilesystem(data));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateAtr_SpartaDos_WithDensityDd_CreatesDoubleDensityDisk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = AtrWriteTools.CreateAtr(tempFile, sectors: 720, density: "dd", filesystem: "spartados");
            Assert.DoesNotContain("ERROR", result);

            var data = File.ReadAllBytes(tempFile);
            var geo = AtrParser.ParseGeometry(data);
            Assert.Equal(256, geo.SectorSize);
            Assert.Equal(720, geo.SectorCount);
            Assert.True(AtrParser.HasSpartaDosFilesystem(data));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
