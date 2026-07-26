using AtariHacker.Analysis;
using AtariHacker.Atari;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class CodeCoverageTests
{
    [Fact]
    public void AnalyzeCoverage_ReturnsZeroResultForNullData()
    {
        var result = CodeCoverage.AnalyzeCoverage(
            null!,
            ReferenceGraph.Empty,
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            0x8000, 0x80FF);

        Assert.Equal(0, result.TotalBytes);
        Assert.Equal(0, result.CodeBytes);
        Assert.Equal(0, result.DataBytes);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void AnalyzeCoverage_ReturnsZeroResultForEmptyData()
    {
        var result = CodeCoverage.AnalyzeCoverage(
            Array.Empty<byte>(),
            ReferenceGraph.Empty,
            new HashSet<ushort>(),
            new HashSet<ushort>(),
            0x8000, 0x80FF);

        Assert.Equal(0, result.TotalBytes);
    }

    [Fact]
    public void AnalyzeCoverage_CorrectlyCountsCodeVsDataBytes()
    {
        var data = new byte[256];
        var codeRegions = new HashSet<ushort>();
        var dataRegions = new HashSet<ushort>();

        // Mark first 128 bytes as code, last 128 as data
        for (var i = 0; i < 128; i++)
            codeRegions.Add((ushort)(0x8000 + i));
        for (var i = 128; i < 256; i++)
            dataRegions.Add((ushort)(0x8000 + i));

        var result = CodeCoverage.AnalyzeCoverage(
            data,
            ReferenceGraph.Empty,
            codeRegions,
            dataRegions,
            0x8000, 0x80FF);

        Assert.Equal(256, result.TotalBytes);
        Assert.Equal(128, result.CodeBytes);
        Assert.Equal(128, result.DataBytes);
        Assert.Equal(50.0, result.CodePercentage, 1);
        Assert.Equal(50.0, result.DataPercentage, 1);
    }

    [Fact]
    public void AnalyzeCoverage_BuildsCorrectCoverageRegionList()
    {
        var data = new byte[256];
        var codeRegions = new HashSet<ushort>();
        var dataRegions = new HashSet<ushort>();

        // First 64 bytes: code, Next 64 bytes: data, Next 128 bytes: code
        for (var i = 0; i < 64; i++)
            codeRegions.Add((ushort)(0x8000 + i));
        for (var i = 64; i < 128; i++)
            dataRegions.Add((ushort)(0x8000 + i));
        for (var i = 128; i < 256; i++)
            codeRegions.Add((ushort)(0x8000 + i));

        var result = CodeCoverage.AnalyzeCoverage(
            data,
            ReferenceGraph.Empty,
            codeRegions,
            dataRegions,
            0x8000, 0x80FF);

        Assert.NotEmpty(result.Regions);
    }

    [Fact]
    public void CoverageResult_ToCsv_ProducesValidCsvOutput()
    {
        var result = new CodeCoverage.CoverageResult(
            0x8000, 0x80FF, 256, 128, 128, 50.0, 50.0,
            new List<CodeCoverage.CoverageRegion>(), 0, 0);

        var csv = result.ToCsv();
        Assert.Contains("start", csv);
        Assert.Contains("$8000", csv);
        Assert.Contains("$80FF", csv);
    }

    [Fact]
    public void CoverageResult_ToTsv_ProducesValidTsvOutput()
    {
        var result = new CodeCoverage.CoverageResult(
            0x8000, 0x80FF, 256, 128, 128, 50.0, 50.0,
            new List<CodeCoverage.CoverageRegion>(), 0, 0);

        var tsv = result.ToTsv();
        Assert.Contains("start", tsv);
        Assert.Contains("$8000", tsv);
    }

    [Fact]
    public void CoverageResult_ToKv_ProducesValidKeyValueOutput()
    {
        var result = new CodeCoverage.CoverageResult(
            0x8000, 0x80FF, 256, 128, 128, 50.0, 50.0,
            new List<CodeCoverage.CoverageRegion>(), 0, 0);

        var kv = result.ToKv();
        Assert.Contains("start=$8000", kv);
        Assert.Contains("end=$80FF", kv);
    }
}