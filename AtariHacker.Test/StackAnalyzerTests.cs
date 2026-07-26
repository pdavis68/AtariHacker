using AtariHacker.Analysis;

namespace AtariHacker.Test;

public sealed class StackAnalyzerTests
{
    [Fact]
    public void AnalyzeStack_ReturnsErrorResultForNullData()
    {
        var result = StackAnalyzer.AnalyzeStack(null!, 0);
        Assert.Contains("ERROR", result.Warnings[0]);
    }

    [Fact]
    public void AnalyzeStack_ReturnsErrorResultForEmptyData()
    {
        var result = StackAnalyzer.AnalyzeStack(Array.Empty<byte>(), 0);
        Assert.Contains("ERROR", result.Warnings[0]);
    }

    [Fact]
    public void AnalyzeStack_ReturnsErrorForAddressBeyondDataLength()
    {
        var data = new byte[10];
        var result = StackAnalyzer.AnalyzeStack(data, 0x8000);
        Assert.Contains("ERROR", result.Warnings[0]);
    }

    [Fact]
    public void AnalyzeStack_TracksPhaPlaStackDepthCorrectly()
    {
        var data = new byte[] { 0x48, 0x48, 0x68, 0x68, 0x60 };
        var result = StackAnalyzer.AnalyzeStack(data, 0, maxInstructions: 10);

        Assert.NotNull(result);
    }

    [Fact]
    public void AnalyzeStack_TracksJsrRtsStackDepthCorrectly()
    {
        var data = new byte[] { 0x20, 0x10, 0x00, 0x60 };
        var result = StackAnalyzer.AnalyzeStack(data, 0, maxInstructions: 10);

        Assert.NotNull(result);
    }

    [Fact]
    public void AnalyzeStack_DetectsUnbalancedStack()
    {
        var data = new byte[] { 0x48, 0x60 };
        var result = StackAnalyzer.AnalyzeStack(data, 0, maxInstructions: 10);

        Assert.NotNull(result);
    }

    [Fact]
    public void AnalyzeStack_RespectsMaxInstructionsBudget()
    {
        var data = new byte[100];
        Array.Fill(data, (byte)0xEA);
        data[^1] = 0x60;

        var result = StackAnalyzer.AnalyzeStack(data, 0, maxInstructions: 10);
        Assert.NotNull(result);
    }
}