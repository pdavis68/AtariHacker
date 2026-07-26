using AtariHacker.Analysis;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class PatternDetectorTests
{
    [Fact]
    public void DetectStateMachines_ReturnsEmptyListForNullData()
    {
        var results = PatternDetector.DetectStateMachines(null!, ReferenceGraph.Empty);
        Assert.Empty(results);
    }

    [Fact]
    public void DetectStateMachines_ReturnsEmptyListForEmptyData()
    {
        var results = PatternDetector.DetectStateMachines(Array.Empty<byte>(), ReferenceGraph.Empty);
        Assert.Empty(results);
    }

    [Fact]
    public void DetectJumpTables_ReturnsEmptyListForNullData()
    {
        var results = PatternDetector.DetectJumpTables(null!, ReferenceGraph.Empty);
        Assert.Empty(results);
    }

    [Fact]
    public void DetectJumpTables_ReturnsEmptyListForEmptyData()
    {
        var results = PatternDetector.DetectJumpTables(Array.Empty<byte>(), ReferenceGraph.Empty);
        Assert.Empty(results);
    }

    [Fact]
    public void DetectCoroutines_ReturnsEmptyListForNullGraph()
    {
        var results = PatternDetector.DetectCoroutines(null!);
        Assert.Empty(results);
    }

    [Fact]
    public void DetectCoroutines_ReturnsEmptyListForEmptyGraph()
    {
        var results = PatternDetector.DetectCoroutines(ReferenceGraph.Empty);
        Assert.Empty(results);
    }

    [Fact]
    public void DetectInterruptHandlers_ReturnsEmptyListForNullSession()
    {
        var results = PatternDetector.DetectInterruptHandlers(null!);
        Assert.Empty(results);
    }

    [Fact]
    public void DetectInterruptHandlers_ReturnsEmptyListForEmptyData()
    {
        var session = new RomSession();
        var results = PatternDetector.DetectInterruptHandlers(session);
        Assert.Empty(results);
    }
}