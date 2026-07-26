using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Analysis;

/// <summary>
/// Core matching engine that scans memory ranges and attempts to match
/// structural templates against the data.
/// </summary>
public static class StructureMatcher
{
    /// <summary>
    /// Scan a memory range and attempt to match all templates from the library.
    /// </summary>
    /// <param name="data">The raw ROM/disk data.</param>
    /// <param name="baseAddress">The base memory address of the data (0 for raw binaries).</param>
    /// <param name="rangeStart">Start of the memory range to scan (inclusive).</param>
    /// <param name="rangeEnd">End of the memory range to scan (inclusive).</param>
    /// <param name="templates">List of templates to match against.</param>
    /// <param name="step">Address step between candidate positions (default 1).</param>
    /// <returns>List of matches sorted by confidence descending.</returns>
    public static List<StructureMatch> MatchAll(
        byte[] data,
        ushort baseAddress,
        ushort rangeStart,
        ushort rangeEnd,
        List<StructureTemplate> templates,
        int step = 1)
    {
        var results = new List<StructureMatch>();

        foreach (var template in templates)
        {
            var matches = MatchTemplate(data, baseAddress, rangeStart, rangeEnd, template, step);
            results.AddRange(matches);
        }

        results.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        return results;
    }

    /// <summary>
    /// Scan a memory range and attempt to match a single template.
    /// </summary>
    public static List<StructureMatch> MatchTemplate(
        byte[] data,
        ushort baseAddress,
        ushort rangeStart,
        ushort rangeEnd,
        StructureTemplate template,
        int step = 1)
    {
        var matches = new List<StructureMatch>();
        var totalSize = ComputeTemplateSize(template);
        if (totalSize <= 0) return matches;

        for (var addr = rangeStart; addr + totalSize - 1 <= rangeEnd && addr + totalSize - 1 < data.Length; addr += (ushort)step)
        {
            var match = TryMatchAtAddress(data, baseAddress, addr, template);
            if (match is not null)
                matches.Add(match);
        }

        return matches;
    }

    /// <summary>
    /// Attempt to match a template at a specific memory address.
    /// Returns null if validation fails.
    /// </summary>
    private static StructureMatch? TryMatchAtAddress(
        byte[] data,
        ushort baseAddress,
        ushort address,
        StructureTemplate template)
    {
        var fieldValues = new Dictionary<string, object>();
        var validationResults = new List<string>();
        var allValid = true;

        // Step 1: Read all fields
        foreach (var field in template.Fields)
        {
            var fileOffset = address + field.Offset - baseAddress;
            if (fileOffset < 0 || fileOffset >= data.Length)
            {
                allValid = false;
                break;
            }

            var result = ReadField(data, fileOffset, field);
            if (result is null)
            {
                allValid = false;
                break;
            }

            fieldValues[field.Name] = result;
        }

        if (!allValid)
            return null;

        // Step 2: Validate fields
        foreach (var validation in template.Validation)
        {
            if (!fieldValues.TryGetValue(validation.Field, out var value))
            {
                validationResults.Add($"Field '{validation.Field}' not found in template.");
                continue;
            }

            if (value is int intVal)
            {
                if (validation.Min.HasValue && intVal < validation.Min.Value)
                {
                    allValid = false;
                    validationResults.Add($"Field '{validation.Field}' = {intVal} < min {validation.Min.Value}");
                    continue;
                }
                if (validation.Max.HasValue && intVal > validation.Max.Value)
                {
                    allValid = false;
                    validationResults.Add($"Field '{validation.Field}' = {intVal} > max {validation.Max.Value}");
                    continue;
                }
                if (validation.Range is { Length: 2 })
                {
                    try
                    {
                        var rangeMin = AddressParser.ParseAddress(validation.Range[0]);
                        var rangeMax = AddressParser.ParseAddress(validation.Range[1]);
                        if (intVal < rangeMin || intVal > rangeMax)
                        {
                            allValid = false;
                            validationResults.Add($"Field '{validation.Field}' = ${intVal:X4} outside range [{validation.Range[0]}, {validation.Range[1]}]");
                            continue;
                        }
                    }
                    catch
                    {
                        validationResults.Add($"Field '{validation.Field}': could not parse range [{string.Join(", ", validation.Range)}]");
                    }
                }
                validationResults.Add($"Field '{validation.Field}' = {DescribeValue(intVal, field: template.Fields.FirstOrDefault(f => f.Name == validation.Field))}");
            }
            else if (value is byte byteVal)
            {
                if (validation.Min.HasValue && byteVal < validation.Min.Value)
                {
                    allValid = false;
                    validationResults.Add($"Field '{validation.Field}' = {byteVal} < min {validation.Min.Value}");
                    continue;
                }
                if (validation.Max.HasValue && byteVal > validation.Max.Value)
                {
                    allValid = false;
                    validationResults.Add($"Field '{validation.Field}' = {byteVal} > max {validation.Max.Value}");
                    continue;
                }
                validationResults.Add($"Field '{validation.Field}' = {byteVal}");
            }
            else
            {
                validationResults.Add($"Field '{validation.Field}' = {value}");
            }
        }

        if (!allValid)
            return null;

        // Step 3: Compute confidence score
        var confidence = ComputeConfidence(template, fieldValues, data, baseAddress);

        return new StructureMatch
        {
            TemplateName = template.Name,
            Address = address,
            Confidence = confidence,
            FieldValues = fieldValues,
            ValidationResults = validationResults
        };
    }

    /// <summary>
    /// Read a single field's value from the data at the specified file offset.
    /// </summary>
    private static object? ReadField(byte[] data, int fileOffset, StructureField field)
    {
        switch (field.Type.ToLowerInvariant())
        {
            case "byte":
                return (int)data[fileOffset];

            case "word_le":
                if (fileOffset + 1 >= data.Length) return null;
                return data[fileOffset] | (data[fileOffset + 1] << 8);

            case "word_be":
                if (fileOffset + 1 >= data.Length) return null;
                return (data[fileOffset] << 8) | data[fileOffset + 1];

            case "bytes":
                if (!field.Length.HasValue || field.Length.Value <= 0) return null;
                if (fileOffset + field.Length.Value > data.Length) return null;
                var bytes = new byte[field.Length.Value];
                Array.Copy(data, fileOffset, bytes, 0, field.Length.Value);
                return bytes;

            case "string":
            {
                // Read null-terminated ATASCII string
                var strBytes = new List<byte>();
                for (var i = fileOffset; i < data.Length; i++)
                {
                    if (data[i] == 0x00) break;
                    strBytes.Add(data[i]);
                }
                return strBytes.Count > 0 ? AtasciiDecoder.Decode(strBytes.ToArray()) : "";

            }

            case "bitfield":
                return (int)data[fileOffset];

            case "skip":
                // Skip fields have no value to read
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Compute a confidence score (0.0 to 1.0) for a matched template.
    /// </summary>
    private static double ComputeConfidence(
        StructureTemplate template,
        Dictionary<string, object> fieldValues,
        byte[] data,
        ushort baseAddress)
    {
        var score = 0.5; // Base score for passing all validations
        var totalWeight = 0.5;
        var checks = 0;

        foreach (var kvp in fieldValues)
        {
            var field = template.Fields.FirstOrDefault(f => f.Name == kvp.Key);
            if (field is null) continue;

            var value = kvp.Value;

            // Check 1: Pointer fields pointing to valid memory ranges
            if (field.Type is "word_le" or "word_be" && value is int addr)
            {
                checks++;
                if (addr >= baseAddress && addr < baseAddress + data.Length)
                {
                    score += 0.15; // Pointer points to valid memory
                    totalWeight += 0.15;
                }
                else if (addr >= 0xD000 && addr <= 0xFFFF)
                {
                    score += 0.10; // Pointer points to hardware/OS ROM
                    totalWeight += 0.10;
                }
            }

            // Check 2: String fields with reasonable content
            if (field.Type == "string" && value is string strVal)
            {
                checks++;
                if (strVal.Length >= 1 && strVal.Length <= 40)
                {
                    score += 0.10; // Reasonable string length
                    totalWeight += 0.10;
                }
            }

            // Check 3: Byte fields with reasonable values
            if (field.Type == "byte" && value is int byteVal)
            {
                checks++;
                if (byteVal is >= 1 and <= 240)
                {
                    score += 0.05; // Reasonable byte value
                    totalWeight += 0.05;
                }
            }

            // Check 4: Bitfield fields with mixed bits
            if (field.Type == "bitfield" && value is int bitVal)
            {
                checks++;
                // At least one bit set, not all bits set
                if (bitVal is > 0 and < 255)
                {
                    score += 0.05;
                    totalWeight += 0.05;
                }
            }
        }

        // Check 5: Validation rules satisfied (already passed all, so this is a bonus)
        if (template.Validation.Count > 0)
        {
            score += 0.10;
            totalWeight += 0.10;
        }

        // Check 6: Byte entropy — data shouldn't be all $00 or $FF
        var entropyCheck = CheckEntropy(template, fieldValues, data, baseAddress);
        score += entropyCheck;
        totalWeight += 0.10;

        return totalWeight > 0 ? Math.Min(score / totalWeight, 1.0) : 0.0;
    }

    /// <summary>
    /// Check that the template's byte sequence isn't trivial padding.
    /// </summary>
    private static double CheckEntropy(
        StructureTemplate template,
        Dictionary<string, object> fieldValues,
        byte[] data,
        ushort baseAddress)
    {
        // Find the total byte range covered by template fields
        var minOffset = int.MaxValue;
        var maxOffset = int.MinValue;

        foreach (var field in template.Fields)
        {
            if (field.Type == "skip" || field.Type == "string")
                continue;

            var fieldEnd = field.Type switch
            {
                "byte" or "bitfield" => field.Offset + 1,
                "word_le" or "word_be" => field.Offset + 2,
                "bytes" when field.Length.HasValue => field.Offset + field.Length.Value,
                _ => field.Offset + 1
            };

            if (field.Offset < minOffset) minOffset = field.Offset;
            if (fieldEnd > maxOffset) maxOffset = fieldEnd;
        }

        if (minOffset == int.MaxValue || maxOffset == int.MinValue)
            return 0.0;

        // Sample some bytes in the range
        var unique = new HashSet<byte>();
        for (var i = minOffset; i < maxOffset && i < 16; i++)
        {
            // Can't easily get the actual data bytes here since we're at the template level
            // Instead, check field values for variety
        }

        // Check field values for variety
        var nonZeroCount = 0;
        var totalCount = 0;
        foreach (var kvp in fieldValues)
        {
            if (kvp.Value is int intVal && intVal != 0)
                nonZeroCount++;
            else if (kvp.Value is byte byteVal && byteVal != 0)
                nonZeroCount++;
            else if (kvp.Value is byte[] bytes && bytes.Any(b => b != 0))
                nonZeroCount++;
            totalCount++;
        }

        if (totalCount == 0) return 0.0;
        var ratio = (double)nonZeroCount / totalCount;

        return ratio > 0.3 ? 0.10 : 0.0;
    }

    /// <summary>
    /// Compute the total byte size of a template (sum of all field sizes).
    /// </summary>
    public static int ComputeTemplateSize(StructureTemplate template)
    {
        var maxOffset = 0;
        foreach (var field in template.Fields)
        {
            var fieldEnd = field.Type.ToLowerInvariant() switch
            {
                "byte" or "bitfield" => field.Offset + 1,
                "word_le" or "word_be" => field.Offset + 2,
                "bytes" when field.Length.HasValue => field.Offset + field.Length.Value,
                "skip" when field.Length.HasValue => field.Offset + field.Length.Value,
                "string" => field.Offset + 1, // At minimum 1 byte (null terminator)
                _ => field.Offset + 1
            };
            if (fieldEnd > maxOffset) maxOffset = fieldEnd;
        }
        return maxOffset;
    }

    /// <summary>
    /// Get a human-readable description of a field value.
    /// </summary>
    private static string DescribeValue(int value, StructureField? field)
    {
        if (field is null) return value.ToString();

        return field.Type switch
        {
            "word_le" or "word_be" => $"${value:X4}",
            "byte" or "bitfield" => $"${value:X2} ({value})",
            _ => value.ToString()
        };
    }
}