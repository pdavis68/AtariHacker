using AtariHacker.Atari;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class HexDumpToolTests
{
    [Fact]
    public void HexDump_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = HexDumpTool.HexDump(session, "0", 16);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void HexDump_ReturnsErrorForOffsetBeyondRomSize()
    {
        var session = new RomSession { Data = new byte[10] };
        var result = HexDumpTool.HexDump(session, "20", 16);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void HexDump_ReturnsErrorForZeroByteCount()
    {
        var session = new RomSession { Data = new byte[100] };
        var result = HexDumpTool.HexDump(session, "0", 0);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void HexDump_ProducesCorrectHexDumpOutput()
    {
        var data = new byte[256];
        for (var i = 0; i < 256; i++)
            data[i] = (byte)i;

        var session = new RomSession { Data = data };
        var result = HexDumpTool.HexDump(session, "0", 256);

        Assert.Contains("Offset", result);
        Assert.Contains("Address", result);
    }

    [Fact]
    public void HexDump_UsesAddressOverrideWhenProvided()
    {
        var data = new byte[256];
        for (var i = 0; i < 256; i++)
            data[i] = (byte)i;

        var session = new RomSession { Data = data };
        var result = HexDumpTool.HexDump(session, "0", 16, startAddress: "$8000");

        Assert.Contains("$8000", result);
    }

    // ─── GenerateHexDump with AtrGeometry tests ──────────────────────────

    [Fact]
    public void GenerateHexDump_WithGeometry_ShowsSectorColumn()
    {
        var data = new byte[512];
        for (var i = 0; i < 512; i++)
            data[i] = (byte)(i & 0xFF);

        var geometry = new AtrGeometry(128, 720, "SD");
        var result = HexDumpTool.GenerateHexDump(
            data.AsSpan(), 0, data.Length, startAddress: null, geometry: geometry);

        // Should contain the Sector column header
        Assert.Contains("Sector", result);
        // Should show sector numbers for data after the 16-byte ATR header
        Assert.Contains("Sctr", result);
    }

    [Fact]
    public void GenerateHexDump_WithGeometry_ShowsSectorBoundaries()
    {
        // Create data that simulates ATR sectors: 16-byte header + 3 sectors of 128 bytes
        var data = new byte[16 + (3 * 128)];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i & 0xFF);

        var geometry = new AtrGeometry(128, 3, "SD");
        var result = HexDumpTool.GenerateHexDump(
            data.AsSpan(), 0, data.Length, startAddress: null, geometry: geometry);

        // Should contain sector column
        Assert.Contains("Sector", result);
    }

    [Fact]
    public void GenerateHexDump_WithoutGeometry_UsesStandardFormat()
    {
        var data = new byte[256];
        for (var i = 0; i < 256; i++)
            data[i] = (byte)i;

        var result = HexDumpTool.GenerateHexDump(
            data.AsSpan(), 0, data.Length, startAddress: null, geometry: null);

        // Should NOT contain the Sector column
        Assert.DoesNotContain("Sector", result);
        // Should use standard header
        Assert.Contains("Offset    Address", result);
    }

    [Fact]
    public void HexDump_WithSectorAwareFlag_WhenNoSourceAtr_UsesStandardFormat()
    {
        var data = new byte[256];
        for (var i = 0; i < 256; i++)
            data[i] = (byte)i;

        var session = new RomSession { Data = data };
        // sectorAware=true but no SourceAtrPath → should fall back to standard format
        var result = HexDumpTool.HexDump(session, "0", 256, sectorAware: true);

        // Should fall back to standard format without sector column
        Assert.DoesNotContain("Sector", result);
    }
}