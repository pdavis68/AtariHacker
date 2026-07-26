using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class SegmentToolsTests
{
    [Fact]
    public void DefineSegment_CreatesSegmentWithCorrectProperties()
    {
        var mgr = new SegmentManager();
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();
        var persistence = new SessionPersistence(session, symbols, zpMap, mgr);

        var result = SegmentTools.DefineSegment(mgr, persistence, "Code", "code", "$8000", "$80FF");
        Assert.Contains("Defined segment", result);
        Assert.Single(mgr.Segments);
        Assert.Equal("Code", mgr.Segments[0].Name);
        Assert.Equal(SegmentType.Code, mgr.Segments[0].Type);
    }

    [Fact]
    public void RemoveSegment_RemovesSegmentByName()
    {
        var mgr = new SegmentManager();
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();
        var persistence = new SessionPersistence(session, symbols, zpMap, mgr);

        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, 0x8000, 0x80FF));
        var result = SegmentTools.RemoveSegment(mgr, persistence, "Code");
        Assert.Contains("Removed", result);
        Assert.Empty(mgr.Segments);
    }

    [Fact]
    public void ListSegments_ListsAllSegmentsInTextFormat()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, 0x8000, 0x80FF));
        var result = SegmentTools.ListSegments(mgr, "text");
        Assert.Contains("Code", result);
        Assert.Contains("$8000", result);
    }

    [Fact]
    public void ListSegments_ListsSegmentsInCsvFormat()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, 0x8000, 0x80FF));
        var result = SegmentTools.ListSegments(mgr, "csv");
        Assert.Contains("name", result);
        Assert.Contains("Code", result);
    }

    [Fact]
    public void ListSegments_ListsSegmentsInTsvFormat()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, 0x8000, 0x80FF));
        var result = SegmentTools.ListSegments(mgr, "tsv");
        Assert.Contains("name", result);
        Assert.Contains("Code", result);
    }

    [Fact]
    public void ListSegments_ListsSegmentsInKvFormat()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, 0x8000, 0x80FF));
        var result = SegmentTools.ListSegments(mgr, "kv");
        Assert.Contains("name=Code", result);
    }

    [Fact]
    public void ClearSegments_RemovesAllSegments()
    {
        var mgr = new SegmentManager();
        var session = new RomSession { Data = new byte[100] };
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();
        var persistence = new SessionPersistence(session, symbols, zpMap, mgr);

        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, 0x8000, 0x80FF));
        SegmentTools.ClearSegments(mgr, persistence);
        Assert.Empty(mgr.Segments);
    }
}