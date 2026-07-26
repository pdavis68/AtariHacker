using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class AnalysisToolsTests
{
    [Fact]
    public void AnalyzeAndDisassemble_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = AnalysisTools.AnalyzeAndDisassemble(session, new SymbolTable(), new ZeroPageMap(), new SegmentManager(), "$8000", 10);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void ProbeAndSegment_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var persistence = new SessionPersistence(session, new SymbolTable(), new ZeroPageMap(), new SegmentManager());
        var result = AnalysisTools.ProbeAndSegment(session, new SymbolTable(), new ZeroPageMap(), new SegmentManager(), persistence, "$8000", "$80FF");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void ProbeData_ProbesMemoryRange()
    {
        var data = new byte[256];
        var session = new RomSession { Data = data };
        var result = AnalysisTools.ProbeData(session, "$8000", "$80FF");
        Assert.NotNull(result);
    }
}
