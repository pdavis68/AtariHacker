using System.Text;

namespace AtariHacker.Helpers;

/// <summary>
/// Static utility for formatting structured output in CSV, TSV, and KV formats.
/// Used by tools that support the --format option (text, csv, tsv, kv).
/// </summary>
public static class OutputFormatter
{
    /// <summary>
    /// Format rows as CSV with a header row.
    /// </summary>
    public static string FormatCsv(string[] headers, string[][] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Format rows as TSV with a header row.
    /// </summary>
    public static string FormatTsv(string[] headers, string[][] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", headers.Select(EscapeTsv)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join("\t", row.Select(EscapeTsv)));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Format rows as key=value pairs, one per cell.
    /// Each row is a sequence of key=value lines, separated by blank lines between rows.
    /// </summary>
    public static string FormatKv(string[] keys, string[][] rows)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var row in rows)
        {
            if (!first)
                sb.AppendLine();
            first = false;

            for (var i = 0; i < keys.Length && i < row.Length; i++)
            {
                sb.AppendLine($"{keys[i]}={EscapeKvValue(row[i])}");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escape a single value for CSV: wrap in quotes if it contains comma, quote, or newline.
    /// </summary>
    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// Escape a single value for TSV: replace tabs with spaces.
    /// </summary>
    private static string EscapeTsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Replace tabs with spaces to preserve column alignment
        return value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }

    /// <summary>
    /// Escape a single value for KV output: replace newlines with spaces.
    /// </summary>
    private static string EscapeKvValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace('\n', ' ').Replace('\r', ' ');
    }
}