using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtariHacker.State;

/// <summary>
/// Defines a single field within a structural data template.
/// </summary>
public sealed class StructureField
{
    /// <summary>Field name (e.g., "width", "tile_map_ptr").</summary>
    public string Name { get; set; } = "";

    /// <summary>Byte offset from the start of the structure.</summary>
    public int Offset { get; set; }

    /// <summary>
    /// Field type: byte, word_le, word_be, bytes, string, bitfield, skip.
    /// </summary>
    public string Type { get; set; } = "byte";

    /// <summary>Required for 'bytes' and 'skip' types; ignored otherwise.</summary>
    public int? Length { get; set; }

    /// <summary>Optional human-readable description of the field.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Validation constraint for a single field in a structural template.
/// </summary>
public sealed class FieldValidation
{
    /// <summary>Name of the field to validate.</summary>
    public string Field { get; set; } = "";

    /// <summary>Minimum allowed value (inclusive) for numeric fields.</summary>
    public int? Min { get; set; }

    /// <summary>Maximum allowed value (inclusive) for numeric fields.</summary>
    public int? Max { get; set; }

    /// <summary>
    /// Range constraint for address/pointer fields.
    /// Format: ["$A000", "$BFFF"] — parsed as hex addresses.
    /// </summary>
    public string[]? Range { get; set; }
}

/// <summary>
/// A reusable structural template that describes the layout of a data structure
/// at a given memory address, including field types, offsets, and validation rules.
/// </summary>
public sealed class StructureTemplate
{
    /// <summary>Unique name for the template (used as identifier).</summary>
    public string Name { get; set; } = "";

    /// <summary>Human-readable description.</summary>
    public string Description { get; set; } = "";

    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Ordered list of fields defining the structure layout.</summary>
    public List<StructureField> Fields { get; set; } = new();

    /// <summary>Optional validation constraints applied during matching.</summary>
    public List<FieldValidation> Validation { get; set; } = new();

    /// <summary>Tags for filtering and categorization.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Category grouping (e.g., "game-templates", "disk-structures").</summary>
    public string Category { get; set; } = "game-templates";

    /// <summary>Timestamp when the template was first created.</summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>Timestamp when the template was last modified.</summary>
    public DateTime? Modified { get; set; }
}

/// <summary>
/// Represents a detected match of a structural template at a specific address.
/// </summary>
public sealed class StructureMatch
{
    /// <summary>Name of the template that matched.</summary>
    public string TemplateName { get; set; } = "";

    /// <summary>Memory address where the match was found.</summary>
    public ushort Address { get; set; }

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Decoded field values keyed by field name.</summary>
    public Dictionary<string, object> FieldValues { get; set; } = new();

    /// <summary>Human-readable validation results.</summary>
    public List<string> ValidationResults { get; set; } = new();
}

/// <summary>
/// Persistent library of structural templates stored as a JSON file.
/// Uses the same upward-search discovery logic as <see cref="PatternLibrary"/>.
/// </summary>
public sealed class StructureLibrary
{
    private const string StructFileName = ".atari-struct.json";

    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The list of structural templates.</summary>
    public List<StructureTemplate> Templates { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Load the structure library from the specified directory, or search upward
    /// from the current directory. Returns an empty library if no file is found.
    /// </summary>
    public static StructureLibrary Load(string? directory = null)
    {
        var path = FindStructFile(directory);
        if (path is null)
            return new StructureLibrary();

        try
        {
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StructureLibrary>(text, SerializerOptions) ?? new StructureLibrary();
        }
        catch
        {
            Console.Error.WriteLine($"Warning: Failed to parse structure library file: {path}");
            return new StructureLibrary();
        }
    }

    /// <summary>
    /// Save the structure library to the specified directory, or the current directory.
    /// </summary>
    public void Save(string? directory = null)
    {
        var dir = directory ?? Directory.GetCurrentDirectory();
        var path = Path.Combine(dir, StructFileName);
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Add a new template. Throws if a template with the same name already exists.
    /// </summary>
    public void Add(StructureTemplate template)
    {
        if (Templates.Any(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A template named '{template.Name}' already exists. Use --force to overwrite.");

        template.Created = DateTime.UtcNow;
        template.Modified = null;
        Templates.Add(template);
    }

    /// <summary>
    /// Remove a template by name. Returns true if found and removed.
    /// </summary>
    public bool Remove(string name)
    {
        var count = Templates.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        return count > 0;
    }

    /// <summary>
    /// Find a template by name (case-insensitive). Returns null if not found.
    /// </summary>
    public StructureTemplate? Find(string name)
    {
        return Templates.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Search templates by optional tag, category, and/or text query.
    /// All filters are optional and case-insensitive.
    /// </summary>
    public List<StructureTemplate> Query(string? tag = null, string? category = null, string? query = null)
    {
        var results = Templates.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagLower = tag!.ToLowerInvariant();
            results = results.Where(t => t.Tags.Any(tg => tg.ToLowerInvariant() == tagLower));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var catLower = category!.ToLowerInvariant();
            results = results.Where(t => t.Category.ToLowerInvariant() == catLower);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var qLower = query!.ToLowerInvariant();
            results = results.Where(t =>
                t.Name.Contains(qLower, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(qLower, StringComparison.OrdinalIgnoreCase));
        }

        return results.ToList();
    }

    /// <summary>
    /// Find the structure file by searching upward from the given directory.
    /// </summary>
    private static string? FindStructFile(string? directory)
    {
        var dir = directory ?? Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, StructFileName);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}