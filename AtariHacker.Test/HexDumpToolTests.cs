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
}