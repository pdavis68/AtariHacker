using System.CommandLine;

namespace AtariHacker.Helpers;

/// <summary>
/// Shared --format option definition used by multiple commands.
/// Accepts: text, csv, tsv, kv
/// </summary>
public static class FormatOption
{
    /// <summary>
    /// The shared --format option with default value "text".
    /// </summary>
    public static readonly Option<string> Option = new(
        "--format",
        () => "text",
        "Output format: text, csv, tsv, or kv");
}