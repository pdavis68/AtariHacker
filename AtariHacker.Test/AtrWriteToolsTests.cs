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
}
