using System.Text;
using AtariHacker.Helpers;

namespace AtariHacker.Analysis;

/// <summary>
/// Binary comparison of ROM or ATR files.
/// </summary>
public static class DiffAnalyzer
{
    /// <summary>
    /// Represents a single byte difference between two files.
    /// </summary>
    public sealed record ByteDiff(int Offset, byte File1Value, byte File2Value);

    /// <summary>
    /// Represents a contiguous region of differences.
    /// </summary>
    public sealed record DiffRegion(int StartOffset, int EndOffset, int Length, string Description);

    /// <summary>
    /// Result of a diff comparison.
    /// </summary>
    public sealed record DiffResult(
        string File1,
        string File2,
        int File1Size,
        int File2Size,
        List<ByteDiff> Differences,
        List<DiffRegion> Regions);

    /// <summary>
    /// Compare two binary files byte-by-byte.
    /// </summary>
    public static DiffResult DiffRoms(string file1, string file2)
    {
        var data1 = File.ReadAllBytes(file1);
        var data2 = File.ReadAllBytes(file2);
        return DiffBytes(file1, file2, data1, data2);
    }

    /// <summary>
    /// Compare two byte arrays.
    /// </summary>
    public static DiffResult DiffBytes(string file1, string file2, byte[] data1, byte[] data2)
    {
        var maxLen = Math.Max(data1.Length, data2.Length);
        var differences = new List<ByteDiff>();
        var regions = new List<DiffRegion>();

        int? regionStart = null;

        for (var i = 0; i < maxLen; i++)
        {
            var b1 = i < data1.Length ? data1[i] : (byte)0;
            var b2 = i < data2.Length ? data2[i] : (byte)0;

            if (b1 != b2)
            {
                differences.Add(new ByteDiff(i, b1, b2));
                if (regionStart is null) regionStart = i;
            }
            else
            {
                if (regionStart is not null)
                {
                    var end = i - 1;
                    regions.Add(new DiffRegion(
                        regionStart.Value,
                        end,
                        end - regionStart.Value + 1,
                        DescribeRegion(regionStart.Value, end, data1, data2)));
                    regionStart = null;
                }
            }
        }

        // Finalize last region
        if (regionStart is not null)
        {
            var end = maxLen - 1;
            regions.Add(new DiffRegion(
                regionStart.Value,
                end,
                end - regionStart.Value + 1,
                DescribeRegion(regionStart.Value, end, data1, data2)));
        }

        return new DiffResult(
            Path.GetFileName(file1),
            Path.GetFileName(file2),
            data1.Length,
            data2.Length,
            differences,
            regions);
    }

    /// <summary>
    /// Format diff result as a summary (default format).
    /// </summary>
    public static string FormatSummary(DiffResult result)
    {
        var lines = new List<string>
        {
            $"Diff: {result.File1} vs {result.File2}",
            $"  Size: {result.File1Size} vs {result.File2Size} {(result.File1Size == result.File2Size ? "(identical)" : "")}",
            $"  Total differences: {result.Differences.Count} bytes",
            "  ---"
        };

        if (result.Regions.Count > 0)
        {
            lines.Add("  Changed regions:");
            foreach (var region in result.Regions)
            {
                lines.Add($"    ${region.StartOffset:X4}-${region.EndOffset:X4} ({region.Length} bytes) — {region.Description}");
            }
            lines.Add("  ---");
        }

        var identicalBytes = Math.Max(result.File1Size, result.File2Size) - result.Differences.Count;
        var totalBytes = Math.Max(result.File1Size, result.File2Size);
        var pct = totalBytes > 0 ? (double)identicalBytes / totalBytes * 100 : 100;
        lines.Add($"  Identical: {identicalBytes} / {totalBytes} bytes ({pct:F2}%)");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Format diff result in verbose mode — list each difference.
    /// </summary>
    public static string FormatVerbose(DiffResult result)
    {
        var lines = new List<string>
        {
            $"Diff: {result.File1} vs {result.File2}",
            $"Total differences: {result.Differences.Count}",
            ""
        };

        foreach (var diff in result.Differences)
        {
            lines.Add($"  ${diff.Offset:X4}: {Formatting.HexByte(diff.File1Value)} → {Formatting.HexByte(diff.File2Value)}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Format diff result as a hex diff — side-by-side hex dump of differing regions.
    /// </summary>
    public static string FormatHexDiff(DiffResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hex diff: {result.File1} vs {result.File2}");
        sb.AppendLine();

        foreach (var region in result.Regions)
        {
            var startRow = region.StartOffset & ~0x0F;
            var endRow = (region.EndOffset + 15) & ~0x0F;

            sb.AppendLine($"--- Region ${region.StartOffset:X4}-${region.EndOffset:X4} ({region.Description}) ---");

            for (var row = startRow; row <= endRow; row += 16)
            {
                var hasDiff = false;
                for (var col = 0; col < 16; col++)
                {
                    var offset = row + col;
                    if (offset >= region.StartOffset && offset <= region.EndOffset)
                    {
                        hasDiff = true;
                        break;
                    }
                    // Also check if value differs at this offset
                    if (offset < Math.Max(result.File1Size, result.File2Size))
                    {
                        var b1 = offset < result.File1Size ? result.Differences.FirstOrDefault(d => d.Offset == offset)?.File1Value ?? 0 : (byte)0;
                        var b2 = offset < result.File2Size ? result.Differences.FirstOrDefault(d => d.Offset == offset)?.File2Value ?? 0 : (byte)0;
                        if (b1 != b2) { hasDiff = true; break; }
                    }
                }

                if (!hasDiff) continue;

                sb.Append($"{Formatting.HexWord((ushort)row)}: ");

                // File 1 hex
                for (var col = 0; col < 16; col++)
                {
                    var offset = row + col;
                    if (offset < result.File1Size)
                    {
                        var diff = result.Differences.FirstOrDefault(d => d.Offset == offset);
                        if (diff is not null)
                        {
                            sb.Append($"{diff.File1Value:X2} ");
                        }
                        else
                        {
                            sb.Append("-- ");
                        }
                    }
                    else
                    {
                        sb.Append("   ");
                    }
                }

                sb.Append(" | ");

                // File 2 hex
                for (var col = 0; col < 16; col++)
                {
                    var offset = row + col;
                    if (offset < result.File2Size)
                    {
                        var diff = result.Differences.FirstOrDefault(d => d.Offset == offset);
                        if (diff is not null)
                        {
                            sb.Append($"{diff.File2Value:X2} ");
                        }
                        else
                        {
                            sb.Append("-- ");
                        }
                    }
                    else
                    {
                        sb.Append("   ");
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string DescribeRegion(int start, int end, byte[] data1, byte[] data2)
    {
        var length = end - start + 1;
        if (length <= 8) return $"small patch ({length} bytes)";

        // Check if it looks like a code patch
        if (length <= 64) return $"code/data modification ({length} bytes)";

        return $"large modification ({length} bytes)";
    }
}