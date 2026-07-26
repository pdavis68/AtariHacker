using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class ZeroPageToolTests
{
    [Fact]
    public void AnnotateZeroPage_AddsAnnotationToZeroPageMap()
    {
        var session = new RomSession { Data = new byte[100] };
        var zpMap = new ZeroPageMap();
        var symbols = new SymbolTable();
        var persistence = new SessionPersistence(session, symbols, zpMap, new SegmentManager());

        var result = ZeroPageTool.AnnotateZeroPage(session, zpMap, persistence, "$80", "CH", "Cursor column");
        Assert.Contains("Annotated", result);
        Assert.True(zpMap.ContainsKey(0x80));
        Assert.Equal("CH", zpMap[0x80].Label);
    }

    [Fact]
    public void ShowZeroPageMap_ListsAnnotations()
    {
        var session = new RomSession { Data = new byte[100] };
        var zpMap = new ZeroPageMap();
        zpMap[0x80] = new SymbolEntry("CH", "Cursor column");

        var result = ZeroPageTool.ShowZeroPageMap(session, zpMap, false);
        Assert.Contains("CH", result);
    }
}