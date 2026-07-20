using System.Text;
using AtariHackerMCP.Analysis;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static class SegmentTools
{
    public static string DefineSegment(
        SegmentManager segmentManager,
        SessionPersistence persistence,
        string name,
        string type,
        string start,
        string end,
        string? comment = null)
    {
        try
        {
            var segmentType = type.ToLowerInvariant() switch
            {
                "code" => SegmentType.Code,
                "data" => SegmentType.Data,
                "graphics" => SegmentType.Graphics,
                "text" => SegmentType.Text,
                "zero_page" => SegmentType.ZeroPage,
                _ => throw new ArgumentException($"Invalid segment type '{type}'. Use: code, data, graphics, text, or zero_page.")
            };

            var startAddr = AddressParser.ParseAddress(start);
            var endAddr = AddressParser.ParseAddress(end);

            if (startAddr > endAddr)
            {
                return "ERROR: Start address must be <= end address.";
            }

            var segment = new SegmentDefinition(name, segmentType, startAddr, endAddr, comment);
            segmentManager.Define(segment);

            // Check for overlaps
            if (segmentManager.HasOverlaps(out var overlapDesc))
            {
                return $"WARNING: {overlapDesc}\nSegment defined, but overlaps detected.";
            }

            persistence.Save();
            return $"Defined segment '{name}' ({type}) from {Formatting.HexWord(startAddr)} to {Formatting.HexWord(endAddr)}.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string RemoveSegment(
        SegmentManager segmentManager,
        SessionPersistence persistence,
        string name)
    {
        try
        {
            segmentManager.Remove(name);
            persistence.Save();
            return $"Removed segment '{name}'.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string ListSegments(SegmentManager segmentManager)
    {
        try
        {
            var segments = segmentManager.Segments;
            if (segments.Count == 0)
            {
                return "No segments defined.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Segments ({segments.Count} defined):");
            sb.AppendLine();

            foreach (var seg in segments)
            {
                var comment = string.IsNullOrWhiteSpace(seg.Comment) ? string.Empty : $"  ; {seg.Comment}";
                sb.AppendLine($"  {seg.Name,-20} {seg.Type,-10} {Formatting.HexWord(seg.Start)}–{Formatting.HexWord(seg.End)}{comment}");
            }

            // Show gaps
            var gaps = segmentManager.FindGaps();
            if (gaps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Gaps between segments:");
                foreach (var gap in gaps)
                {
                    sb.AppendLine($"  {Formatting.HexWord(gap.Start)}–{Formatting.HexWord(gap.End)} ({(gap.End - gap.Start + 1)} bytes)");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string ClearSegments(
        SegmentManager segmentManager,
        SessionPersistence persistence)
    {
        try
        {
            segmentManager.Clear();
            persistence.Save();
            return "All segments cleared.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string GenerateLinkerConfig(
        SegmentManager segmentManager,
        string output)
    {
        try
        {
            var segments = segmentManager.Segments;
            if (segments.Count == 0)
            {
                return "ERROR: No segments defined. Use DefineSegment first.";
            }

            // Check for gaps
            var gaps = segmentManager.FindGaps();
            var warnings = new List<string>();
            if (gaps.Count > 0)
            {
                warnings.Add($"Warning: {gaps.Count} gap(s) between segments detected.");
                foreach (var gap in gaps)
                {
                    warnings.Add($"  Gap: {Formatting.HexWord(gap.Start)}–{Formatting.HexWord(gap.End)}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("FEATURES {");
            sb.AppendLine("    STARTADDRESS = default;");
            sb.AppendLine("}");
            sb.AppendLine("SYMBOLS {");
            sb.AppendLine("    __STACKSIZE__: type = weak, value = $0800;");
            sb.AppendLine("}");
            sb.AppendLine();

            // MEMORY section
            sb.AppendLine("MEMORY {");
            foreach (var seg in segments)
            {
                if (seg.Type == State.SegmentType.ZeroPage) continue; // Skip zero-page segments
                var size = seg.End - seg.Start + 1;
                var name = SanitizeName(seg.Name);
                sb.AppendLine($"    {name}: start = {Formatting.HexWord(seg.Start)}, size = ${size:X4}, type = rw;");
            }
            sb.AppendLine("}");

            sb.AppendLine();

            // SEGMENTS section
            sb.AppendLine("SEGMENTS {");
            foreach (var seg in segments)
            {
                if (seg.Type == State.SegmentType.ZeroPage) continue;
                var name = SanitizeName(seg.Name);
                sb.AppendLine($"    {name}: load = {name}, type = rw;");
            }
            sb.AppendLine("}");

            // Write output
            var parent = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            File.WriteAllText(output, sb.ToString());

            var result = $"Linker configuration written to {output}.";
            if (warnings.Count > 0)
            {
                result += "\n" + string.Join('\n', warnings);
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string SanitizeName(string name)
    {
        // Convert to uppercase, replace non-alphanumeric chars with underscore
        var sanitized = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray());
        if (sanitized.Length == 0) return "SEGMENT";
        return sanitized;
    }
}