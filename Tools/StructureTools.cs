using System.Text;
using System.Text.Json;
using AtariHacker.Analysis;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class StructureTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ─── List ───────────────────────────────────────────────────────────────

    /// <summary>
    /// List all saved templates with optional filtering.
    /// </summary>
    public static string ListTemplates(string? tag, string? category, string? query, string format)
    {
        var library = StructureLibrary.Load();
        var results = library.Query(tag, category, query);

        if (results.Count == 0)
        {
            if (tag is not null || category is not null || query is not null)
                return "No templates match the specified filters.";
            return "Structure template library is empty. Use 'struct define' to create a new template.";
        }

        return format.ToLowerInvariant() switch
        {
            "csv" => FormatTemplatesCsv(results),
            "tsv" => FormatTemplatesTsv(results),
            "kv" => FormatTemplatesKeyValue(results),
            _ => FormatTemplatesText(results)
        };
    }

    // ─── Define ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Define a new structural template from a JSON file or inline JSON string.
    /// </summary>
    public static string DefineTemplate(string source, bool force)
    {
        StructureTemplate template;

        try
        {
            // Try as file path first
            if (File.Exists(source))
            {
                var text = File.ReadAllText(source);
                template = JsonSerializer.Deserialize<StructureTemplate>(text, JsonOptions)
                    ?? throw new InvalidOperationException("Could not parse template file.");
            }
            else
            {
                // Try as inline JSON
                template = JsonSerializer.Deserialize<StructureTemplate>(source, JsonOptions)
                    ?? throw new InvalidOperationException("Could not parse template JSON.");
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to parse template: {ex.Message}";
        }

        // Validate the template
        var validationError = ValidateTemplate(template);
        if (validationError is not null)
            return $"ERROR: {validationError}";

        var library = StructureLibrary.Load();

        // Check for duplicates
        if (!force && library.Find(template.Name) is not null)
            return $"ERROR: A template named '{template.Name}' already exists. Use --force to overwrite.";

        if (force && library.Find(template.Name) is StructureTemplate existing)
        {
            // Update existing
            existing.Description = template.Description;
            existing.Version = template.Version;
            existing.Fields = template.Fields;
            existing.Validation = template.Validation;
            existing.Tags = template.Tags;
            existing.Category = template.Category;
            existing.Modified = DateTime.UtcNow;
            library.Save();
            return $"Updated template: {template.Name}";
        }

        library.Add(template);
        library.Save();
        return $"Added template: {template.Name}";
    }

    // ─── Remove ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Delete a template by name.
    /// </summary>
    public static string RemoveTemplate(string name)
    {
        var library = StructureLibrary.Load();
        if (!library.Remove(name))
            return $"ERROR: Template '{name}' not found.";

        library.Save();
        return $"Removed template: {name}";
    }

    // ─── Show ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Display full details of a named template.
    /// </summary>
    public static string ShowTemplate(string name)
    {
        var library = StructureLibrary.Load();
        var template = library.Find(name);
        if (template is null)
            return $"ERROR: Template '{name}' not found.";

        var tags = template.Tags.Count > 0 ? string.Join(", ", template.Tags) : "(none)";
        var modified = template.Modified?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "(never)";

        var sb = new StringBuilder();
        sb.AppendLine($"Template: {template.Name}");
        sb.AppendLine($"  Description: {template.Description}");
        sb.AppendLine($"  Version:     {template.Version}");
        sb.AppendLine($"  Category:    {template.Category}");
        sb.AppendLine($"  Tags:        {tags}");
        sb.AppendLine($"  Created:     {template.Created:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"  Modified:    {modified}");
        sb.AppendLine($"  Total size:  {StructureMatcher.ComputeTemplateSize(template)} bytes");
        sb.AppendLine();
        sb.AppendLine("  Fields:");
        foreach (var field in template.Fields)
        {
            var lenInfo = field.Length.HasValue ? $"[{field.Length.Value}]" : "";
            var desc = string.IsNullOrWhiteSpace(field.Description) ? "" : $"  ; {field.Description}";
            sb.AppendLine($"    +{field.Offset:X2}  {field.Type,-8}{lenInfo,-5} {field.Name}{desc}");
        }

        if (template.Validation.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Validation:");
            foreach (var v in template.Validation)
            {
                var constraints = new List<string>();
                if (v.Min.HasValue) constraints.Add($"min={v.Min.Value}");
                if (v.Max.HasValue) constraints.Add($"max={v.Max.Value}");
                if (v.Range is { Length: > 0 }) constraints.Add($"range=[{string.Join(", ", v.Range)}]");
                sb.AppendLine($"    {v.Field}: {string.Join(", ", constraints)}");
            }
        }

        return sb.ToString();
    }

    // ─── Match ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Scan a memory range for structural template matches.
    /// </summary>
    public static string MatchTemplates(
        RomSession session,
        string start,
        string end,
        string? templateName,
        string format,
        VerboseContext? verbose = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";

            var startAddr = AddressParser.ParseAddress(start);
            var endAddr = AddressParser.ParseAddress(end);

            if (startAddr > endAddr)
                return "ERROR: Start address must be <= end address.";

            // Convert to file offsets for bounds checking
            var startOffset = XexAddressResolver.ResolveMemoryAddress(session, startAddr);
            var endOffset = XexAddressResolver.ResolveMemoryAddress(session, endAddr);
            if (startOffset is null || endOffset is null)
                return $"ERROR: Address range ${startAddr:X4}–${endAddr:X4} is not in the loaded data.";

            var library = StructureLibrary.Load();
            List<StructureTemplate> templates;

            if (!string.IsNullOrWhiteSpace(templateName))
            {
                var tpl = library.Find(templateName);
                if (tpl is null)
                    return $"ERROR: Template '{templateName}' not found.";
                templates = new List<StructureTemplate> { tpl };
            }
            else
            {
                templates = library.Templates;
                if (templates.Count == 0)
                    return "No templates defined. Use 'struct define' to create templates first.";
            }

            var baseAddress = session.BaseAddress ?? 0;
            var matches = StructureMatcher.MatchAll(session.Data, baseAddress, startAddr, endAddr, templates);

            if (verbose is not null)
            {
                verbose.BytesProcessed = endAddr - startAddr + 1;
            }

            if (matches.Count == 0)
                return $"No structural matches found in range ${startAddr:X4}–${endAddr:X4}.";

            return format.ToLowerInvariant() switch
            {
                "csv" => FormatMatchesCsv(matches),
                "tsv" => FormatMatchesTsv(matches),
                "kv" => FormatMatchesKeyValue(matches),
                _ => FormatMatchesText(matches, templates)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Import ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Import templates from an external JSON file, merging into the library.
    /// </summary>
    public static string ImportTemplates(string path, bool force)
    {
        if (!File.Exists(path))
            return $"ERROR: File not found: {path}";

        List<StructureTemplate> imported;
        try
        {
            var text = File.ReadAllText(path);
            // Try as a StructureLibrary wrapper first, then as a raw list
            if (JsonSerializer.Deserialize<StructureLibrary>(text, JsonOptions) is { } lib)
                imported = lib.Templates;
            else if (JsonSerializer.Deserialize<List<StructureTemplate>>(text, JsonOptions) is { } list)
                imported = list;
            else
                return "ERROR: Could not parse template file. Expected a StructureLibrary object or an array of StructureTemplate.";
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to parse template file: {ex.Message}";
        }

        var library = StructureLibrary.Load();
        var added = 0;
        var skipped = 0;
        var overwritten = 0;

        foreach (var template in imported)
        {
            if (string.IsNullOrWhiteSpace(template.Name))
            {
                skipped++;
                continue;
            }

            var validationError = ValidateTemplate(template);
            if (validationError is not null)
            {
                skipped++;
                continue;
            }

            var existing = library.Find(template.Name);
            if (existing is not null)
            {
                if (force)
                {
                    existing.Description = template.Description;
                    existing.Version = template.Version;
                    existing.Fields = template.Fields;
                    existing.Validation = template.Validation;
                    existing.Tags = template.Tags;
                    existing.Category = template.Category;
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
                library.Add(template);
                added++;
            }
        }

        library.Save();
        return $"Imported {added} template(s), overwritten {overwritten}, skipped {skipped} from {path}";
    }

    // ─── Export ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Export templates to a JSON file, optionally filtered by tag or category.
    /// </summary>
    public static string ExportTemplates(string? tag, string? category, string output)
    {
        var library = StructureLibrary.Load();
        var results = library.Query(tag, category, null);

        if (results.Count == 0)
            return "No templates match the specified filters. Nothing to export.";

        var export = new StructureLibrary { Version = 1, Templates = results };
        var json = JsonSerializer.Serialize(export, JsonOptions);

        try
        {
            File.WriteAllText(output, json);
            return $"Exported {results.Count} template(s) to {output}";
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to write export file: {ex.Message}";
        }
    }

    // ─── Validation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Validate a template definition for correctness.
    /// </summary>
    private static string? ValidateTemplate(StructureTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
            return "Template name cannot be empty.";

        if (template.Fields.Count == 0)
            return "Template must have at least one field.";

        var validTypes = new HashSet<string> { "byte", "word_le", "word_be", "bytes", "string", "bitfield", "skip" };
        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
                return "All fields must have a name.";

            if (!fieldNames.Add(field.Name))
                return $"Duplicate field name: '{field.Name}'.";

            if (!validTypes.Contains(field.Type.ToLowerInvariant()))
                return $"Invalid field type '{field.Type}' for field '{field.Name}'. Valid types: {string.Join(", ", validTypes)}.";

            if (field.Type.Equals("bytes", StringComparison.OrdinalIgnoreCase) && (!field.Length.HasValue || field.Length.Value <= 0))
                return $"Field '{field.Name}' of type 'bytes' must have a positive Length.";

            if (field.Offset < 0)
                return $"Field '{field.Name}' has a negative offset ({field.Offset}).";
        }

        // Validate that validation references exist
        foreach (var v in template.Validation)
        {
            if (string.IsNullOrWhiteSpace(v.Field))
                return "Validation entry must specify a field name.";

            if (!fieldNames.Contains(v.Field))
                return $"Validation references unknown field '{v.Field}'.";

            if (v.Min is null && v.Max is null && (v.Range is null || v.Range.Length == 0))
                return $"Validation for '{v.Field}' must specify at least one constraint (min, max, or range).";
        }

        return null;
    }

    // ─── Formatting helpers ─────────────────────────────────────────────────

    private static string FormatTemplatesText(List<StructureTemplate> templates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Structure Templates ({templates.Count}):");
        foreach (var t in templates.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            var tags = t.Tags.Count > 0 ? $" [{string.Join(", ", t.Tags)}]" : "";
            var size = StructureMatcher.ComputeTemplateSize(t);
            sb.AppendLine($"  {t.Name,-30} {t.Category,-18}{tags}");
            sb.AppendLine($"    {t.Description} ({size} bytes, {t.Fields.Count} fields)");
        }
        return sb.ToString();
    }

    private static string FormatTemplatesCsv(List<StructureTemplate> templates)
    {
        var lines = new List<string> { "name,description,category,tags,fields,size,version,created" };
        foreach (var t in templates)
        {
            var tags = string.Join("; ", t.Tags);
            var size = StructureMatcher.ComputeTemplateSize(t);
            lines.Add($"{EscapeCsv(t.Name)},{EscapeCsv(t.Description)},{EscapeCsv(t.Category)},{EscapeCsv(tags)},{t.Fields.Count},{size},{t.Version},{t.Created:yyyy-MM-dd}");
        }
        return string.Join('\n', lines);
    }

    private static string FormatTemplatesTsv(List<StructureTemplate> templates)
    {
        var lines = new List<string> { "name\tdescription\tcategory\ttags\tfields\tsize\tversion\tcreated" };
        foreach (var t in templates)
        {
            var tags = string.Join("; ", t.Tags);
            var size = StructureMatcher.ComputeTemplateSize(t);
            lines.Add($"{EscapeTsv(t.Name)}\t{EscapeTsv(t.Description)}\t{EscapeTsv(t.Category)}\t{EscapeTsv(tags)}\t{t.Fields.Count}\t{size}\t{t.Version}\t{t.Created:yyyy-MM-dd}");
        }
        return string.Join('\n', lines);
    }

    private static string FormatTemplatesKeyValue(List<StructureTemplate> templates)
    {
        var lines = new List<string>();
        foreach (var t in templates)
        {
            lines.Add($"name={t.Name}");
            lines.Add($"description={t.Description}");
            lines.Add($"category={t.Category}");
            lines.Add($"version={t.Version}");
            lines.Add($"fields={t.Fields.Count}");
            lines.Add($"size={StructureMatcher.ComputeTemplateSize(t)}");
            if (t.Tags.Count > 0)
                lines.Add($"tags={string.Join(",", t.Tags)}");
            lines.Add($"created={t.Created:yyyy-MM-dd}");
            lines.Add("---");
        }
        return string.Join('\n', lines);
    }

    private static string FormatMatchesText(List<StructureMatch> matches, List<StructureTemplate> templates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Structural Matches ({matches.Count}):");
        sb.AppendLine();

        foreach (var match in matches.Take(20))
        {
            sb.AppendLine($"  Template: {match.TemplateName}");
            sb.AppendLine($"  Address:  ${match.Address:X4}");
            sb.AppendLine($"  Confidence: {match.Confidence:P1}");
            sb.AppendLine("  Field Values:");

            foreach (var kvp in match.FieldValues)
            {
                var field = templates
                    .Where(t => t.Name == match.TemplateName)
                    .SelectMany(t => t.Fields)
                    .FirstOrDefault(f => f.Name == kvp.Key);

                var formatted = FormatFieldValue(kvp.Key, kvp.Value, field);
                sb.AppendLine($"    {formatted}");
            }

            if (match.ValidationResults.Count > 0)
            {
                sb.AppendLine("  Validation:");
                foreach (var vr in match.ValidationResults)
                {
                    sb.AppendLine($"    {vr}");
                }
            }

            sb.AppendLine();
        }

        if (matches.Count > 20)
        {
            sb.AppendLine($"  ... and {matches.Count - 20} more matches");
        }

        return sb.ToString();
    }

    private static string FormatMatchesCsv(List<StructureMatch> matches)
    {
        var lines = new List<string> { "template_name,address,confidence,field_values" };
        foreach (var m in matches)
        {
            var fields = string.Join("; ", m.FieldValues.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            lines.Add($"{EscapeCsv(m.TemplateName)},${m.Address:X4},{m.Confidence:P1},{EscapeCsv(fields)}");
        }
        return string.Join('\n', lines);
    }

    private static string FormatMatchesTsv(List<StructureMatch> matches)
    {
        var lines = new List<string> { "template_name\taddress\tconfidence\tfield_values" };
        foreach (var m in matches)
        {
            var fields = string.Join("; ", m.FieldValues.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            lines.Add($"{EscapeTsv(m.TemplateName)}\t${m.Address:X4}\t{m.Confidence:P1}\t{EscapeTsv(fields)}");
        }
        return string.Join('\n', lines);
    }

    private static string FormatMatchesKeyValue(List<StructureMatch> matches)
    {
        var lines = new List<string>();
        foreach (var m in matches)
        {
            lines.Add($"template_name={m.TemplateName}");
            lines.Add($"address=${m.Address:X4}");
            lines.Add($"confidence={m.Confidence:P1}");
            foreach (var kvp in m.FieldValues)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
            lines.Add("---");
        }
        return string.Join('\n', lines);
    }

    private static string FormatFieldValue(string name, object value, StructureField? field)
    {
        if (value is byte[] bytes)
            return $"  {name} = {Formatting.HexByte(bytes[0])}... ({bytes.Length} bytes)";

        if (field?.Type is "word_le" or "word_be" && value is int intVal)
            return $"  {name} = ${intVal:X4} ({intVal})";

        if (field?.Type is "byte" or "bitfield" && value is int byteVal)
            return $"  {name} = ${byteVal:X2} ({byteVal})";

        return $"  {name} = {value}";
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