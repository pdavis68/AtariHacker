using System.Text.Json;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class PatternTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ─── List ───────────────────────────────────────────────────────────────

    /// <summary>
    /// List all saved patterns with optional filtering.
    /// </summary>
    public static string ListPatterns(string? tag, string? category, string? query, string format)
    {
        var library = PatternLibrary.Load();
        var results = library.Query(tag, category, query);

        if (results.Count == 0)
        {
            if (tag is not null || category is not null || query is not null)
                return "No patterns match the specified filters.";
            return "Pattern library is empty. Use 'patterns add' to create a new pattern.";
        }

        return format.ToLowerInvariant() switch
        {
            "csv" => FormatPatternsCsv(results),
            "tsv" => FormatPatternsTsv(results),
            "kv" => FormatPatternsKeyValue(results),
            _ => FormatPatternsText(results)
        };
    }

    // ─── Add ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Save a new pattern from inline hex, with optional --force to overwrite.
    /// </summary>
    public static string AddPattern(string name, string hex, string? description, string[]? tags, string? category, bool force)
    {
        // Validate hex pattern
        var validationError = ValidateHexPattern(hex);
        if (validationError is not null)
            return $"ERROR: {validationError}";

        var library = PatternLibrary.Load();

        // Check for duplicates
        if (!force && library.Find(name) is not null)
            return $"ERROR: A pattern named '{name}' already exists. Use --force to overwrite.";

        if (force && library.Find(name) is PatternEntry existing)
        {
            // Update existing
            existing.Description = description ?? existing.Description;
            existing.Hex = hex;
            existing.Tags = tags?.ToList() ?? existing.Tags;
            existing.Category = category ?? existing.Category;
            existing.Modified = DateTime.UtcNow;
            library.Save();
            return $"Updated pattern: {name}";
        }

        var entry = new PatternEntry
        {
            Name = name,
            Description = description ?? "",
            Hex = hex,
            Tags = tags?.ToList() ?? new List<string>(),
            Category = category ?? "uncategorized",
            Created = DateTime.UtcNow
        };

        library.Add(entry);
        library.Save();
        return $"Added pattern: {name}";
    }

    // ─── Remove ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Delete a pattern by name.
    /// </summary>
    public static string RemovePattern(string name)
    {
        var library = PatternLibrary.Load();
        if (!library.Remove(name))
            return $"ERROR: Pattern '{name}' not found.";

        library.Save();
        return $"Removed pattern: {name}";
    }

    // ─── Show ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Display full details of a named pattern.
    /// </summary>
    public static string ShowPattern(string name)
    {
        var library = PatternLibrary.Load();
        var entry = library.Find(name);
        if (entry is null)
            return $"ERROR: Pattern '{name}' not found.";

        var tags = entry.Tags.Count > 0 ? string.Join(", ", entry.Tags) : "(none)";
        var modified = entry.Modified?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "(never)";
        var matchCount = entry.MatchCount.HasValue ? entry.MatchCount.Value.ToString() : "(not searched)";

        return $"""
Pattern: {entry.Name}
  Description: {entry.Description}
  Hex:         {entry.Hex}
  Category:    {entry.Category}
  Tags:        {tags}
  Created:     {entry.Created:yyyy-MM-dd HH:mm:ss UTC}
  Modified:    {modified}
  Last Match:  {matchCount}
""";
    }

    // ─── Search ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Search the loaded binary using a saved pattern.
    /// </summary>
    public static string SearchPattern(RomSession session, string name, int maxResults, VerboseContext? verbose = null)
    {
        var library = PatternLibrary.Load();
        var entry = library.Find(name);
        if (entry is null)
            return $"ERROR: Pattern '{name}' not found in library.";

        // Delegate to FindPatternTool which already handles session validation
        var result = FindPatternTool.FindPattern(session, entry.Hex, maxResults, verbose);

        // Update match count on the pattern entry
        // Parse the result to extract match count
        var matchCount = ParseMatchCount(result);
        entry.MatchCount = matchCount;

        // Save updated match count (persist the library)
        library.Save();

        return result;
    }

    // ─── Import ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Import patterns from an external JSON file, merging into the library.
    /// </summary>
    public static string ImportPatterns(string path, bool force)
    {
        if (!File.Exists(path))
            return $"ERROR: File not found: {path}";

        List<PatternEntry> imported;
        try
        {
            var text = File.ReadAllText(path);
            // Try as a PatternLibrary wrapper first, then as a raw list
            if (JsonSerializer.Deserialize<PatternLibrary>(text, JsonOptions) is { } lib)
                imported = lib.Patterns;
            else if (JsonSerializer.Deserialize<List<PatternEntry>>(text, JsonOptions) is { } list)
                imported = list;
            else
                return "ERROR: Could not parse pattern file. Expected a PatternLibrary object or an array of PatternEntry.";
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to parse pattern file: {ex.Message}";
        }

        var library = PatternLibrary.Load();
        var added = 0;
        var skipped = 0;
        var overwritten = 0;

        foreach (var entry in imported)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                skipped++;
                continue;
            }

            var existing = library.Find(entry.Name);
            if (existing is not null)
            {
                if (force)
                {
                    existing.Description = entry.Description;
                    existing.Hex = entry.Hex;
                    existing.Tags = entry.Tags;
                    existing.Category = entry.Category;
                    existing.Modified = DateTime.UtcNow;
                    overwritten++;
                }
                else
                {
                    skipped++;
                }
            }
            else
            {
                library.Add(entry);
                added++;
            }
        }

        library.Save();
        return $"Imported {added} pattern(s), overwritten {overwritten}, skipped {skipped} from {path}";
    }

    // ─── Export ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Export patterns to a JSON file, optionally filtered by tag or category.
    /// </summary>
    public static string ExportPatterns(string? tag, string? category, string output)
    {
        var library = PatternLibrary.Load();
        var results = library.Query(tag, category, null);

        if (results.Count == 0)
            return "No patterns match the specified filters. Nothing to export.";

        var export = new PatternLibrary { Version = 1, Patterns = results };
        var json = JsonSerializer.Serialize(export, JsonOptions);

        try
        {
            File.WriteAllText(output, json);
            return $"Exported {results.Count} pattern(s) to {output}";
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to write export file: {ex.Message}";
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string? ValidateHexPattern(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "Hex pattern cannot be empty.";

        var tokens = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return "Hex pattern cannot be empty.";

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token == "??")
                continue;

            if (!byte.TryParse(token, System.Globalization.NumberStyles.AllowHexSpecifier, null, out _))
                return $"Invalid hex byte at position {i + 1}: '{token}'. Use two-digit hex or '??' for wildcard.";
        }

        return null;
    }

    private static int ParseMatchCount(string result)
    {
        // Result format: "Pattern: ...\nFound N match(es):\n..."
        var lines = result.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("Found ") && line.Contains(" match"))
            {
                var parts = line.Split(' ');
                if (parts.Length > 1 && int.TryParse(parts[1], out var count))
                    return count;
            }
        }
        // "Found 0 match(es)." case
        if (result.Contains("Found 0 match"))
            return 0;
        return 0;
    }

    private static string FormatPatternsText(List<PatternEntry> patterns)
    {
        var lines = new List<string> { $"Patterns ({patterns.Count}):" };
        foreach (var p in patterns.OrderBy(p => p.Category).ThenBy(p => p.Name))
        {
            var tags = p.Tags.Count > 0 ? $" [{string.Join(", ", p.Tags)}]" : "";
            var matchInfo = p.MatchCount.HasValue ? $" (last: {p.MatchCount} matches)" : "";
            lines.Add($"  {p.Name,-30} {p.Category,-18}{tags}{matchInfo}");
            lines.Add($"    {p.Hex}");
            if (!string.IsNullOrWhiteSpace(p.Description))
                lines.Add($"    {p.Description}");
        }
        return string.Join('\n', lines);
    }

    private static string FormatPatternsCsv(List<PatternEntry> patterns)
    {
        var lines = new List<string> { "name,description,hex,category,tags,created,match_count" };
        foreach (var p in patterns)
        {
            var desc = EscapeCsv(p.Description);
            var tags = string.Join("; ", p.Tags);
            var matchCount = p.MatchCount?.ToString() ?? "";
            lines.Add($"{EscapeCsv(p.Name)},{desc},{EscapeCsv(p.Hex)},{EscapeCsv(p.Category)},{EscapeCsv(tags)},{p.Created:yyyy-MM-dd},{matchCount}");
        }
        return string.Join('\n', lines);
    }

    private static string FormatPatternsTsv(List<PatternEntry> patterns)
    {
        var lines = new List<string> { "name\tdescription\thex\tcategory\ttags\tcreated\tmatch_count" };
        foreach (var p in patterns)
        {
            var tags = string.Join("; ", p.Tags);
            var matchCount = p.MatchCount?.ToString() ?? "";
            lines.Add($"{EscapeTsv(p.Name)}\t{EscapeTsv(p.Description)}\t{EscapeTsv(p.Hex)}\t{EscapeTsv(p.Category)}\t{EscapeTsv(tags)}\t{p.Created:yyyy-MM-dd}\t{matchCount}");
        }
        return string.Join('\n', lines);
    }

    private static string FormatPatternsKeyValue(List<PatternEntry> patterns)
    {
        var lines = new List<string>();
        foreach (var p in patterns)
        {
            lines.Add($"name={p.Name}");
            lines.Add($"description={p.Description}");
            lines.Add($"hex={p.Hex}");
            lines.Add($"category={p.Category}");
            if (p.Tags.Count > 0)
                lines.Add($"tags={string.Join(",", p.Tags)}");
            lines.Add($"created={p.Created:yyyy-MM-dd}");
            if (p.MatchCount.HasValue)
                lines.Add($"match_count={p.MatchCount}");
            lines.Add("---");
        }
        return string.Join('\n', lines);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string EscapeTsv(string value)
    {
        return value.Replace("\t", " ").Replace("\n", " ");
    }
}