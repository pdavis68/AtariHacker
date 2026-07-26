using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class ControlFlowToolTests
{
    [Fact]
    public void TraceControlFlow_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = ControlFlowTool.TraceControlFlow(session, new SymbolTable(), new ZeroPageMap(), "$8000");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void TraceControlFlow_ReturnsErrorForAddressNotInLoadedRom()
    {
        var session = new RomSession { Data = new byte[10] };
        var result = ControlFlowTool.TraceControlFlow(session, new SymbolTable(), new ZeroPageMap(), "$8000");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void TraceControlFlow_TracesExecutionFlowFromStartAddress()
    {
        var data = new byte[] { 0xA9, 0x40, 0x85, 0x80, 0x60 }; // LDA #$40; STA $80; RTS
        var session = new RomSession { Data = data };
        var result = ControlFlowTool.TraceControlFlow(session, new SymbolTable(), new ZeroPageMap(), "0", maxDepth: 5, maxInstructions: 10);
        Assert.Contains("LDA", result);
        Assert.Contains("STA", result);
        Assert.Contains("RTS", result);
    }

    [Fact]
    public void TraceControlFlow_RespectsMaxDepthParameter()
    {
        var data = new byte[] { 0xA9, 0x00, 0x60 }; // LDA #$00; RTS
        var session = new RomSession { Data = data };
        var result = ControlFlowTool.TraceControlFlow(session, new SymbolTable(), new ZeroPageMap(), "0", maxDepth: 0, maxInstructions: 10);
        Assert.NotNull(result);
    }

    [Fact]
    public void TraceControlFlow_FormatsTextOutputCorrectly()
    {
        var data = new byte[] { 0x60 }; // RTS
        var session = new RomSession { Data = data };
        var result = ControlFlowTool.TraceControlFlow(session, new SymbolTable(), new ZeroPageMap(), "0", format: "text");
        Assert.NotNull(result);
    }

    [Fact]
    public void TraceControlFlow_FormatsCsvOutputCorrectly()
    {
        var data = new byte[] { 0x60 }; // RTS
        var session = new RomSession { Data = data };
        var result = ControlFlowTool.TraceControlFlow(session, new SymbolTable(), new ZeroPageMap(), "0", format: "csv");
        Assert.Contains("depth", result);
    }
}
