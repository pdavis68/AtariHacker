using AtariHacker.Analysis;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class CallGraphTests
{
    [Fact]
    public void BuildCallGraph_ReturnsEmptyGraphForEmptyReferences()
    {
        var graph = CallGraph.BuildCallGraph(ReferenceGraph.Empty);
        Assert.Empty(graph);
    }

    [Fact]
    public void BuildCallGraph_BuildsGraphFromSubroutineEntries()
    {
        var refs = new ReferenceGraph(
            new HashSet<ushort> { (ushort)0x8000 },
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<byte>(),
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<ushort>());

        var graph = CallGraph.BuildCallGraph(refs, startAddress: (ushort)0x8000);
        Assert.NotEmpty(graph);
        Assert.True(graph.ContainsKey((ushort)0x8000));
    }

    [Fact]
    public void BuildCallGraph_RespectsMaxDepthParameter()
    {
        var refs = new ReferenceGraph(
            new HashSet<ushort> { (ushort)0x8000 },
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<byte>(),
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            new HashSet<ushort>());

        var graph = CallGraph.BuildCallGraph(refs, startAddress: (ushort)0x8000, maxDepth: 0);
        Assert.NotEmpty(graph);
    }

    [Fact]
    public void BuildCallGraphFromData_HandlesEmptyDataGracefully()
    {
        var refs = ReferenceGraph.Empty;
        var graph = CallGraph.BuildCallGraphFromData(Array.Empty<byte>(), refs, null, null);
        Assert.Empty(graph);
    }

    [Fact]
    public void FormatMermaid_ProducesValidMermaidSyntax()
    {
        var graph = new Dictionary<ushort, HashSet<ushort>>
        {
            { (ushort)0x8000, new HashSet<ushort> { (ushort)0x8100 } }
        };
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();

        var result = CallGraph.FormatMermaid(graph, symbols, zpMap);
        Assert.StartsWith("graph TD", result.Trim());
        Assert.Contains("-->", result);
    }

    [Fact]
    public void FormatText_ProducesIndentedTextTree()
    {
        var graph = new Dictionary<ushort, HashSet<ushort>>
        {
            { (ushort)0x8000, new HashSet<ushort> { (ushort)0x8100 } }
        };
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();

        var result = CallGraph.FormatText(graph, symbols, zpMap);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void FormatMermaid_HandlesEmptyGraph()
    {
        var graph = new Dictionary<ushort, HashSet<ushort>>();
        var symbols = new SymbolTable();
        var zpMap = new ZeroPageMap();

        var result = CallGraph.FormatMermaid(graph, symbols, zpMap);
        Assert.Equal("graph TD\n", result);
    }
}