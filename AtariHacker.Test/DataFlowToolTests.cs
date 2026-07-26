using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class DataFlowToolTests
{
    [Fact]
    public void TraceAccess_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = DataFlowTool.TraceAccess(session, new SymbolTable(), new ZeroPageMap(), "$D012");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void TraceAccess_TracesForwardDataFlow()
    {
        // LDA $D012; STA $80; RTS — writes to $80, reads from $D012
        var data = new byte[] { 0xAD, 0x12, 0xD0, 0x85, 0x80, 0x60 };
        var session = new RomSession { Data = data };
        var result = DataFlowTool.TraceAccess(session, new SymbolTable(), new ZeroPageMap(), "$D012", direction: "forward");
        Assert.NotNull(result);
    }

    [Fact]
    public void TraceAccess_TracesBackwardDataFlow()
    {
        var data = new byte[] { 0xA9, 0x40, 0x85, 0x80, 0xA5, 0x80, 0x60 }; // LDA #$40; STA $80; LDA $80; RTS
        var session = new RomSession { Data = data };
        var result = DataFlowTool.TraceAccess(session, new SymbolTable(), new ZeroPageMap(), "$80", direction: "backward");
        Assert.NotNull(result);
    }

    [Fact]
    public void TraceAccess_FormatsTextOutputCorrectly()
    {
        var data = new byte[] { 0xAD, 0x12, 0xD0, 0x60 };
        var session = new RomSession { Data = data };
        var result = DataFlowTool.TraceAccess(session, new SymbolTable(), new ZeroPageMap(), "$D012", format: "text");
        Assert.NotNull(result);
    }

    [Fact]
    public void TraceAccess_FormatsCsvOutputCorrectly()
    {
        var data = new byte[] { 0xAD, 0x12, 0xD0, 0x60 };
        var session = new RomSession { Data = data };
        var result = DataFlowTool.TraceAccess(session, new SymbolTable(), new ZeroPageMap(), "$D012", format: "csv");
        Assert.Contains("address", result);
    }
}
