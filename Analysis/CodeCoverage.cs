using AtariHacker.Atari;
using AtariHacker.Helpers;

namespace AtariHacker.Analysis;

/// <summary>
/// Code coverage analysis — determines which bytes are code vs. data
/// in a given address range.
/// </summary>
public static class CodeCoverage
{
    /// <summary>
    /// Result of a coverage analysis for an address range.
    /// </summary>
    public sealed record CoverageResult(
        ushort Start,
        ushort End,
        int TotalBytes,
        int CodeBytes,
        int DataBytes,
        double CodePercentage,
        double DataPercentage,
        List<CoverageRegion> Regions,
        int OrphanedCodeBytes,
        int EmbeddedDataBytes)
    {
        /// <summary>
        /// Format as CSV rows (header + data).
        /// </summary>
        public string ToCsv()
        {
            var headers = new[] { "start", "end", "total_bytes", "code_bytes", "data_bytes", "code_pct", "data_pct", "orphaned_bytes", "embedded_data_bytes" };
            var rows = new[]
            {
                new[]
                {
                    Formatting.HexWord(Start),
                    Formatting.HexWord(End),
                    TotalBytes.ToString(),
                    CodeBytes.ToString(),
                    DataBytes.ToString(),
                    $"{CodePercentage:F1}",
                    $"{DataPercentage:F1}",
                    OrphanedCodeBytes.ToString(),
                    EmbeddedDataBytes.ToString()
                }
            };
            return OutputFormatter.FormatCsv(headers, rows);
        }

        /// <summary>
        /// Format as TSV rows (header + data).
        /// </summary>
        public string ToTsv()
        {
            var headers = new[] { "start", "end", "total_bytes", "code_bytes", "data_bytes", "code_pct", "data_pct", "orphaned_bytes", "embedded_data_bytes" };
            var rows = new[]
            {
                new[]
                {
                    Formatting.HexWord(Start),
                    Formatting.HexWord(End),
                    TotalBytes.ToString(),
                    CodeBytes.ToString(),
                    DataBytes.ToString(),
                    $"{CodePercentage:F1}",
                    $"{DataPercentage:F1}",
                    OrphanedCodeBytes.ToString(),
                    EmbeddedDataBytes.ToString()
                }
            };
            return OutputFormatter.FormatTsv(headers, rows);
        }

        /// <summary>
        /// Format as key=value pairs.
        /// </summary>
        public string ToKv()
        {
            var keys = new[] { "start", "end", "total_bytes", "code_bytes", "data_bytes", "code_pct", "data_pct", "orphaned_bytes", "embedded_data_bytes" };
            var rows = new[]
            {
                new[]
                {
                    Formatting.HexWord(Start),
                    Formatting.HexWord(End),
                    TotalBytes.ToString(),
                    CodeBytes.ToString(),
                    DataBytes.ToString(),
                    $"{CodePercentage:F1}",
                    $"{DataPercentage:F1}",
                    OrphanedCodeBytes.ToString(),
                    EmbeddedDataBytes.ToString()
                }
            };
            return OutputFormatter.FormatKv(keys, rows);
        }
    }

    /// <summary>
    /// A contiguous region with uniform code/data classification.
    /// </summary>
    public sealed record CoverageRegion(
        ushort Start,
        ushort End,
        int CodeBytes,
        int DataBytes,
        string Label);

    /// <summary>
    /// Analyze code coverage using the reference graph and code region tracing.
    /// </summary>
    public static CoverageResult AnalyzeCoverage(
        byte[] data,
        ReferenceGraph references,
        HashSet<ushort> codeRegions,
        HashSet<ushort> dataRegions,
        ushort start,
        ushort end)
    {
        if (data is null || data.Length == 0)
        {
            return new CoverageResult(start, end, 0, 0, 0, 0, 0, new List<CoverageRegion>(), 0, 0);
        }

        var regions = new List<CoverageRegion>();
        var totalBytes = end - start + 1;
        var codeBytes = 0;
        var dataBytes = 0;
        var orphanedCodeBytes = 0;
        var embeddedDataBytes = 0;

        // Scan through the range and build regions
        ushort? regionStart = null;
        var regionCodeBytes = 0;
        var regionDataBytes = 0;

        for (var addr = start; addr <= end; addr++)
        {
            var isCode = codeRegions.Contains(addr);
            var isData = dataRegions.Contains(addr);

            if (isCode) codeBytes++;
            if (isData) dataBytes++;

            if (isCode && !dataRegions.Contains(addr))
            {
                // Code byte not also marked as data
                if (regionStart is null) regionStart = addr;
                regionCodeBytes++;
            }
            else if (isData)
            {
                // Data byte
                if (regionStart is null) regionStart = addr;
                regionDataBytes++;
                // Check if this data is embedded in a code region
                if (codeRegions.Contains(addr))
                {
                    embeddedDataBytes++;
                }
            }
            else
            {
                // Neither — orphaned
                if (regionStart is null) regionStart = addr;
                orphanedCodeBytes++;
            }

            // Check if we need to finalize a region
            var isLast = addr == end;
            var nextIsDifferent = !isLast &&
                ((codeRegions.Contains(addr) != codeRegions.Contains((ushort)(addr + 1))) ||
                 (dataRegions.Contains(addr) != dataRegions.Contains((ushort)(addr + 1))));

            if (isLast || nextIsDifferent)
            {
                if (regionStart is not null)
                {
                    var label = codeBytes > dataBytes ? "code" : dataBytes > codeBytes ? "data" : "mixed";
                    if (orphanedCodeBytes > codeBytes && orphanedCodeBytes > dataBytes)
                    {
                        label = "orphaned";
                    }
                    regions.Add(new CoverageRegion(
                        regionStart.Value,
                        addr,
                        regionCodeBytes,
                        regionDataBytes,
                        label));
                }
                regionStart = null;
                regionCodeBytes = 0;
                regionDataBytes = 0;
            }
        }

        var codePct = totalBytes > 0 ? (double)codeBytes / totalBytes * 100 : 0;
        var dataPct = totalBytes > 0 ? (double)dataBytes / totalBytes * 100 : 0;

        orphanedCodeBytes = totalBytes - codeBytes - dataBytes;
        if (orphanedCodeBytes < 0) orphanedCodeBytes = 0;

        return new CoverageResult(
            start, end, totalBytes, codeBytes, dataBytes,
            codePct, dataPct,
            regions, orphanedCodeBytes, embeddedDataBytes);
    }

    /// <summary>
    /// Format coverage analysis as a human-readable string.
    /// </summary>
    public static string FormatCoverage(CoverageResult result)
    {
        var lines = new List<string>
        {
            $"Coverage Analysis: {Formatting.HexWord(result.Start)}–{Formatting.HexWord(result.End)}"
        };

        foreach (var region in result.Regions)
        {
            var total = region.End - region.Start + 1;
            var codePct = total > 0 ? (double)region.CodeBytes / total * 100 : 0;
            var dataPct = total > 0 ? (double)region.DataBytes / total * 100 : 0;
            lines.Add($"  {Formatting.HexWord(region.Start)}–{Formatting.HexWord(region.End)}: {codePct:F0}% code, {dataPct:F0}% data ({region.Label})");
        }

        lines.Add("  ---");
        lines.Add($"  Total: {result.CodePercentage:F0}% code, {result.DataPercentage:F0}% data");
        lines.Add($"  Orphaned code: {result.OrphanedCodeBytes} bytes ({(result.TotalBytes > 0 ? (double)result.OrphanedCodeBytes / result.TotalBytes * 100 : 0):F1}%)");
        lines.Add($"  Embedded data: {result.EmbeddedDataBytes} bytes");

        return string.Join('\n', lines);
    }
}