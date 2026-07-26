using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class VerboseContextTests
{
    [Fact]
    public void GetMetadata_ReturnsEmptyStringWhenDisabled()
    {
        var ctx = new VerboseContext { Enabled = false };
        var result = ctx.GetMetadata(new RomSession(), new SymbolTable(), new SegmentManager());
        Assert.Equal("", result);
    }

    [Fact]
    public void GetMetadata_IncludesExecutionTimeWhenEnabled()
    {
        var ctx = new VerboseContext { Enabled = true };
        ctx.Timer.Start();
        Thread.Sleep(1);
        ctx.Timer.Stop();

        var result = ctx.GetMetadata(new RomSession(), new SymbolTable(), new SegmentManager());
        Assert.Contains("execution_ms", result);
    }

    [Fact]
    public void GetMetadata_IncludesBytesProcessedWhenSet()
    {
        var ctx = new VerboseContext { Enabled = true, BytesProcessed = 1024 };
        var result = ctx.GetMetadata(new RomSession(), new SymbolTable(), new SegmentManager());
        Assert.Contains("bytes_processed=1024", result);
    }

    [Fact]
    public void GetMetadata_IncludesPassesCompletedWhenGreaterThanZero()
    {
        var ctx = new VerboseContext { Enabled = true, PassesCompleted = 3 };
        var result = ctx.GetMetadata(new RomSession(), new SymbolTable(), new SegmentManager());
        Assert.Contains("passes_completed=3", result);
    }

    [Fact]
    public void GetMetadata_IncludesConfidenceWhenSet()
    {
        var ctx = new VerboseContext { Enabled = true, Confidence = "High" };
        var result = ctx.GetMetadata(new RomSession(), new SymbolTable(), new SegmentManager());
        Assert.Contains("confidence=High", result);
    }

    [Fact]
    public void GetMetadata_OutputLinesStartWithHashSpace()
    {
        var ctx = new VerboseContext { Enabled = true, BytesProcessed = 512 };
        var result = ctx.GetMetadata(new RomSession(), new SymbolTable(), new SegmentManager());
        foreach (var line in result.Trim().Split('\n'))
            Assert.StartsWith("# ", line);
    }
}
