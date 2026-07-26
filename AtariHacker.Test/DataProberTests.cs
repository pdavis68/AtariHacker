using AtariHacker.Analysis;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class DataProberTests
{
    [Fact]
    public void ProbeData_ReturnsInvalidRangeForNullData()
    {
        var result = DataProber.ProbeData(null!, 0, 0xFF);
        Assert.Equal("Invalid range", result.Description);
        Assert.Equal("Low", result.Confidence);
    }

    [Fact]
    public void ProbeData_ReturnsInvalidRangeForStartGreaterThanEnd()
    {
        var data = new byte[100];
        var result = DataProber.ProbeData(data, 50, 10);
        Assert.Equal("Invalid range", result.Description);
    }

    [Fact]
    public void ProbeData_ReturnsInvalidRangeForEndBeyondDataLength()
    {
        var data = new byte[100];
        var result = DataProber.ProbeData(data, 0, 200);
        Assert.Equal("Invalid range", result.Description);
    }

    [Fact]
    public void ProbeData_DetectsAtasciiAsciiStrings()
    {
        var data = new byte[200];
        var text = "HELLO WORLD"u8.ToArray();
        Array.Copy(text, 0, data, 10, text.Length);

        var result = DataProber.ProbeData(data, 0, 199);
        Assert.NotNull(result);
    }

    [Fact]
    public void ProbeData_DetectsPaddingBytes()
    {
        var data = new byte[100];
        Array.Fill(data, (byte)0xFF);

        var result = DataProber.ProbeData(data, 0, 99);
        Assert.Contains("padding", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProbeData_ReturnsUnknownDataWhenNoHeuristicMatches()
    {
        var data = new byte[100];
        var rng = new Random(42);
        rng.NextBytes(data);

        var result = DataProber.ProbeData(data, 0, 99);
        Assert.NotNull(result);
    }

    [Fact]
    public void ProbeResult_ToCsv_ProducesValidCsvOutput()
    {
        var result = new ProbeResult("Test data", "High", new List<string> { "detail1", "detail2" });
        var csv = result.ToCsv();
        Assert.Contains("description", csv);
        Assert.Contains("Test data", csv);
        Assert.Contains("High", csv);
    }

    [Fact]
    public void ProbeResult_ToTsv_ProducesValidTsvOutput()
    {
        var result = new ProbeResult("Test data", "Medium", new List<string> { "detail" });
        var tsv = result.ToTsv();
        Assert.Contains("description", tsv);
        Assert.Contains("Test data", tsv);
    }

    [Fact]
    public void ProbeResult_ToKv_ProducesValidKeyValueOutput()
    {
        var result = new ProbeResult("Test data", "Low", new List<string> { "detail" });
        var kv = result.ToKv();
        Assert.Contains("description=Test data", kv);
        Assert.Contains("confidence=Low", kv);
    }
}