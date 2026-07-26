using AtariHacker.Analysis;

namespace AtariHacker.Test;

public sealed class DiffAnalyzerTests
{
    [Fact]
    public void DiffBytes_ReturnsNoDifferencesForIdenticalArrays()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var data2 = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);
        Assert.Empty(result.Differences);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void DiffBytes_DetectsSingleByteDifference()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var data2 = new byte[] { 0x00, 0xFF, 0x02, 0x03 };

        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);
        Assert.Single(result.Differences);
        Assert.Equal(1, result.Differences[0].Offset);
        Assert.Equal(0x01, result.Differences[0].File1Value);
        Assert.Equal(0xFF, result.Differences[0].File2Value);
    }

    [Fact]
    public void DiffBytes_DetectsMultipleByteDifferences()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var data2 = new byte[] { 0xFF, 0x01, 0xFE, 0x03, 0xFC };

        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);
        Assert.Equal(3, result.Differences.Count);
    }

    [Fact]
    public void DiffBytes_HandlesArraysOfDifferentLengths()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02 };
        var data2 = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);
        Assert.Equal(2, result.Differences.Count); // bytes 3 and 4 differ
    }

    [Fact]
    public void DiffBytes_BuildsCorrectDiffRegionList()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var data2 = new byte[] { 0x00, 0xFF, 0xFE, 0x03, 0x04 };

        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);
        Assert.Single(result.Regions);
        Assert.Equal(1, result.Regions[0].StartOffset);
        Assert.Equal(2, result.Regions[0].EndOffset);
    }

    [Fact]
    public void FormatSummary_ProducesCorrectSummaryText()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02 };
        var data2 = new byte[] { 0x00, 0xFF, 0x02 };
        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);

        var summary = DiffAnalyzer.FormatSummary(result);
        Assert.Contains("file1.bin", summary);
        Assert.Contains("file2.bin", summary);
        Assert.Contains("differences", summary);
    }

    [Fact]
    public void FormatVerbose_ListsEachByteDifference()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02 };
        var data2 = new byte[] { 0x00, 0xFF, 0x02 };
        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);

        var verbose = DiffAnalyzer.FormatVerbose(result);
        Assert.Contains("$0001", verbose);
        Assert.Contains("$01", verbose);
        Assert.Contains("$FF", verbose);
    }

    [Fact]
    public void FormatHexDiff_ProducesSideBySideHexDiff()
    {
        var data1 = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var data2 = new byte[] { 0x00, 0xFF, 0x02, 0xFE, 0x04 };
        var result = DiffAnalyzer.DiffBytes("file1.bin", "file2.bin", data1, data2);

        var hexDiff = DiffAnalyzer.FormatHexDiff(result);
        Assert.Contains("Hex diff", hexDiff);
        Assert.Contains("Region", hexDiff);
    }
}