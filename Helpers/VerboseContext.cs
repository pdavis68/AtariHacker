using System.Diagnostics;
using AtariHacker.State;

namespace AtariHacker.Helpers;

/// <summary>
/// Collects and formats execution metadata for verbose mode output.
/// Emitted as "# key=value" lines (shell-compatible comments) before command output.
/// </summary>
public sealed class VerboseContext
{
    /// <summary>
    /// Whether verbose mode is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Wall-clock timer for the command.
    /// </summary>
    public Stopwatch Timer { get; } = new();

    /// <summary>
    /// Number of bytes scanned or processed by the command.
    /// </summary>
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Number of analysis passes completed (analyze command only).
    /// </summary>
    public int PassesCompleted { get; set; }

    /// <summary>
    /// Confidence score from data probing (probe command only).
    /// </summary>
    public string? Confidence { get; set; }

    /// <summary>
    /// Returns metadata lines formatted as "# key=value\n" when verbose is enabled,
    /// or an empty string when verbose is disabled.
    /// </summary>
    public string GetMetadata(RomSession session, SymbolTable symbols, SegmentManager segments)
    {
        if (!Enabled)
        {
            return string.Empty;
        }

        Timer.Stop();

        var lines = new List<string>
        {
            $"# execution_ms={Timer.ElapsedMilliseconds}",
            $"# bytes_processed={BytesProcessed}",
            $"# session_target={Path.GetFileName(session.FilePath ?? "unknown")}",
            $"# session_size={session.Length}",
            $"# symbol_count={symbols.Count}",
            $"# segment_count={segments.Segments.Count}"
        };

        if (PassesCompleted > 0)
        {
            lines.Add($"# passes_completed={PassesCompleted}");
        }

        if (!string.IsNullOrWhiteSpace(Confidence))
        {
            lines.Add($"# confidence={Confidence}");
        }

        return string.Join('\n', lines) + '\n';
    }
}