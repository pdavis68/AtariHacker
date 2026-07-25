using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtariHacker.State;

/// <summary>
/// A single pattern entry in the pattern library.
/// </summary>
public sealed class PatternEntry
{
    /// <summary>Unique name for the pattern (used as identifier).</summary>
    public string Name { get; set; } = "";

    /// <summary>Human-readable description of what this pattern matches.</summary>
    public string Description { get; set; } = "";

    /// <summary>Space-separated hex bytes with ?? for wildcards (e.g., "20 ?? ?? 60").</summary>
    public string Hex { get; set; } = "";

    /// <summary>Optional tags for filtering and categorization.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Category grouping (e.g., "code-patterns", "hardware", "uncategorized").</summary>
    public string Category { get; set; } = "uncategorized";

    /// <summary>Timestamp when the pattern was first created.</summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>Timestamp when the pattern was last modified.</summary>
    public DateTime? Modified { get; set; }

    /// <summary>Number of matches found during the most recent search (runtime-only, not persisted).</summary>
    [JsonIgnore]
    public int? MatchCount { get; set; }
}

/// <summary>
/// Persistent pattern library stored as a JSON file alongside the project configuration.
/// Uses the same upward-search discovery logic as <see cref="CliConfig"/>.
/// </summary>
public sealed class PatternLibrary
{
    private const string PatternFileName = ".atari-hacker-patterns.json";

    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The list of pattern entries in the library.</summary>
    public List<PatternEntry> Patterns { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Load the pattern library from the specified directory, or search upward from the current directory.
    /// Returns an empty library if no file is found.
    /// </summary>
    public static PatternLibrary Load(string? directory = null)
    {
        var path = FindPatternFile(directory);
        if (path is null)
            return new PatternLibrary();

        try
        {
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PatternLibrary>(text, SerializerOptions) ?? new PatternLibrary();
        }
        catch
        {
            Console.Error.WriteLine($"Warning: Failed to parse pattern library file: {path}");
            return new PatternLibrary();
        }
    }

    /// <summary>
    /// Save the pattern library to the specified directory, or the current directory.
    /// </summary>
    public void Save(string? directory = null)
    {
        var dir = directory ?? Directory.GetCurrentDirectory();
        var path = Path.Combine(dir, PatternFileName);
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Add a new pattern entry. Throws if a pattern with the same name already exists.
    /// </summary>
    public void Add(PatternEntry entry)
    {
        if (Patterns.Any(p => string.Equals(p.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A pattern named '{entry.Name}' already exists. Use --force to overwrite.");

        entry.Created = DateTime.UtcNow;
        entry.Modified = null;
        Patterns.Add(entry);
    }

    /// <summary>
    /// Remove a pattern by name. Returns true if found and removed.
    /// </summary>
    public bool Remove(string name)
    {
        var count = Patterns.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return count > 0;
    }

    /// <summary>
    /// Find a pattern by name (case-insensitive). Returns null if not found.
    /// </summary>
    public PatternEntry? Find(string name)
    {
        return Patterns.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Search patterns by optional tag, category, and/or text query (matches name and description).
    /// All filters are optional and case-insensitive.
    /// </summary>
    public List<PatternEntry> Query(string? tag = null, string? category = null, string? query = null)
    {
        var results = Patterns.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagLower = tag!.ToLowerInvariant();
            results = results.Where(p => p.Tags.Any(t => t.ToLowerInvariant() == tagLower));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var catLower = category!.ToLowerInvariant();
            results = results.Where(p => p.Category.ToLowerInvariant() == catLower);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var qLower = query!.ToLowerInvariant();
            results = results.Where(p =>
                p.Name.Contains(qLower, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(qLower, StringComparison.OrdinalIgnoreCase));
        }

        return results.ToList();
    }

    /// <summary>
    /// Find the pattern file by searching upward from the given directory (or current directory).
    /// Returns null if no file is found.
    /// </summary>
    private static string? FindPatternFile(string? directory)
    {
        var dir = directory ?? Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, PatternFileName);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
