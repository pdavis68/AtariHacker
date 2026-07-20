using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Analysis;

/// <summary>
/// Result of probing a memory range for data type identification.
/// </summary>
public sealed record ProbeResult(
    string Description,
    string Confidence,  // "High", "Medium", "Low"
    List<string> Details);

/// <summary>
/// Automatic data type detection heuristics for memory ranges.
/// </summary>
public static class DataProber
{
    /// <summary>
    /// Analyze a memory range to identify the data type.
    /// </summary>
    public static ProbeResult ProbeData(byte[] data, ushort start, ushort end)
    {
        if (data is null || start > end || end >= data.Length)
        {
            return new ProbeResult("Invalid range", "Low", new List<string> { "Range exceeds available data." });
        }

        var length = end - start + 1;
        var range = new byte[length];
        Array.Copy(data, start, range, 0, length);

        var results = new List<(string Description, string Confidence, List<string> Details)>();

        // Try each heuristic
        var stringResult = DetectStrings(range, start);
        if (stringResult is not null) results.Add(stringResult.Value);

        var paddingResult = DetectPadding(range, start);
        if (paddingResult is not null) results.Add(paddingResult.Value);

        var charsetResult = DetectCharacterSet(range, start);
        if (charsetResult is not null) results.Add(charsetResult.Value);

        var tableResult = DetectTable(range, start);
        if (tableResult is not null) results.Add(tableResult.Value);

        var displayListResult = DetectDisplayList(range, start);
        if (displayListResult is not null) results.Add(displayListResult.Value);

        var spriteResult = DetectSpriteData(range, start);
        if (spriteResult is not null) results.Add(spriteResult.Value);

        var mapResult = DetectMapData(range, start);
        if (mapResult is not null) results.Add(mapResult.Value);

        // Return the best result or a summary
        if (results.Count == 0)
        {
            return new ProbeResult(
                $"${start:X4}–${end:X4}: Unknown data ({length} bytes)",
                "Low",
                new List<string> { "No known data pattern detected." });
        }

        // Sort by confidence: High > Medium > Low
        results.Sort((a, b) => CompareConfidence(b.Confidence, a.Confidence));

        var best = results[0];
        var allDetails = new List<string>();
        foreach (var r in results)
        {
            allDetails.AddRange(r.Details);
        }

        return new ProbeResult(best.Description, best.Confidence, allDetails);
    }

    /// <summary>
    /// Heuristic 1: String detection — runs of printable ATASCII/ASCII characters.
    /// </summary>
    private static (string Description, string Confidence, List<string> Details)? DetectStrings(byte[] range, ushort startAddress)
    {
        var strings = new List<(int Offset, string Text)>();
        var currentStart = -1;
        var currentBytes = new List<byte>();

        for (var i = 0; i < range.Length; i++)
        {
            var b = range[i];
            var isPrintable = (b >= 0x20 && b <= 0x7E) || b == 0x9B; // ATASCII EOL
            // Also accept inverse video ATASCII (bit 7 set)
            var isInverse = (b >= 0xA0 && b <= 0xFE);
            var isAtasciiPrintable = isPrintable || isInverse;

            if (isAtasciiPrintable)
            {
                if (currentStart < 0) currentStart = i;
                currentBytes.Add(b);
            }
            else
            {
                if (currentBytes.Count >= 4)
                {
                    strings.Add((currentStart, AtasciiDecoder.Decode(currentBytes.ToArray())));
                }
                currentStart = -1;
                currentBytes.Clear();
            }
        }

        // Check for trailing string
        if (currentBytes.Count >= 4)
        {
            strings.Add((currentStart, AtasciiDecoder.Decode(currentBytes.ToArray())));
        }

        if (strings.Count == 0)
        {
            return null;
        }

        var totalStringBytes = strings.Sum(s => s.Text.Length);
        var ratio = (double)totalStringBytes / range.Length;

        // Detect if strings are $9B-terminated (ATASCII EOL)
        var eolTerminated = strings.Count > 1 && range.Contains((byte)0x9B);
        var nullTerminated = strings.Count > 1 && range.Contains((byte)0x00);

        string structure;
        if (eolTerminated) structure = "$9B-terminated string table (ATASCII EOL)";
        else if (nullTerminated) structure = "Null-terminated string table";
        else structure = "Contiguous string data";

        var sampleStrings = strings.Take(5).Select(s => $"  \"{Truncate(s.Text, 60)}\"").ToList();

        var details = new List<string>
        {
            $"Strings detected: {strings.Count}",
            $"Structure: {structure}",
            $"Confidence: {(ratio > 0.7 ? "High" : ratio > 0.4 ? "Medium" : "Low")}"
        };
        details.AddRange(sampleStrings);

        return (
            $"${startAddress:X4}–${startAddress + range.Length - 1:X4}: ATASCII/ASCII text ({range.Length} bytes)",
            ratio > 0.7 ? "High" : ratio > 0.4 ? "Medium" : "Low",
            details);
    }

    /// <summary>
    /// Heuristic 2: Padding detection — runs of $00, $FF, or $1A.
    /// </summary>
    private static (string Description, string Confidence, List<string> Details)? DetectPadding(byte[] range, ushort startAddress)
    {
        var zeroRuns = CountConsecutiveRuns(range, 0x00, 8);
        var ffRuns = CountConsecutiveRuns(range, 0xFF, 8);
        var eolRuns = CountConsecutiveRuns(range, 0x1A, 4);

        var totalPadding = zeroRuns + ffRuns + eolRuns;
        if (totalPadding == 0)
        {
            return null;
        }

        var ratio = (double)totalPadding / range.Length;

        var parts = new List<string>();
        if (zeroRuns > 0) parts.Add($"{zeroRuns} bytes of $00");
        if (ffRuns > 0) parts.Add($"{ffRuns} bytes of $FF");
        if (eolRuns > 0) parts.Add($"{eolRuns} bytes of $1A");

        return (
            $"${startAddress:X4}–${startAddress + range.Length - 1:X4}: Padding/filler ({string.Join(", ", parts)})",
            ratio > 0.5 ? "High" : "Medium",
            new List<string> { $"Padding ratio: {ratio:P1}" });
    }

    /// <summary>
    /// Heuristic 3: Character set detection — 1024 or 512 byte blocks with 8-byte alignment.
    /// </summary>
    private static (string Description, string Confidence, List<string> Details)? DetectCharacterSet(byte[] range, ushort startAddress)
    {
        if (range.Length == 1024)
        {
            var aligned = true;
            for (var i = 0; i < 128; i++)
            {
                // Each character is 8 bytes; check that each row starts at an 8-byte boundary
                if ((startAddress + i * 8) % 8 != 0)
                {
                    aligned = false;
                    break;
                }
            }

            return (
                $"${startAddress:X4}–${startAddress + 1023:X4}: Character set (1024 bytes)",
                "High",
                new List<string>
                {
                    "Format: 128 characters × 8 bytes each",
                    "Mode: ANTIC mode 2 (standard character set)",
                    "Alignment: " + (aligned ? "8-byte aligned" : "misaligned")
                });
        }

        if (range.Length == 512)
        {
            return (
                $"${startAddress:X4}–${startAddress + 511:X4}: Character set (512 bytes)",
                "High",
                new List<string>
                {
                    "Format: 64 characters × 8 bytes each",
                    "Mode: ANTIC mode 2 (narrow character set)",
                    "Alignment: 8-byte aligned"
                });
        }

        return null;
    }

    /// <summary>
    /// Heuristic 4: Table detection — 2-byte address tables, 1-byte lookup tables.
    /// </summary>
    private static (string Description, string Confidence, List<string> Details)? DetectTable(byte[] range, ushort startAddress)
    {
        if (range.Length < 4) return null;

        // Check for 2-byte address table (little-endian word sequences)
        var addressTableScore = 0;
        var wordCount = range.Length / 2;
        for (var i = 0; i < wordCount; i++)
        {
            if (i * 2 + 1 < range.Length)
            {
                var word = range[i * 2] | (range[i * 2 + 1] << 8);
                // Addresses typically fall in $0000–$FFFF range
                if (word >= 0x0600 && word <= 0xFFFF)
                {
                    addressTableScore++;
                }
            }
        }

        var addressRatio = (double)addressTableScore / wordCount;

        // Check for 1-byte lookup table (values in 0-255 range, non-instruction distribution)
        var byteTableScore = 0;
        for (var i = 0; i < range.Length; i++)
        {
            var b = range[i];
            // Non-instruction-like values: low values (<$20), high values (>$EA), specific patterns
            if (b < 0x20 || b > 0xEA || (b >= 0x80 && b <= 0x9F))
            {
                byteTableScore++;
            }
        }

        var byteRatio = (double)byteTableScore / range.Length;

        var details = new List<string>();
        if (addressRatio > 0.6)
        {
            details.Add($"Address table: {addressTableScore}/{wordCount} words are valid addresses ({addressRatio:P1})");
            return (
                $"${startAddress:X4}–${startAddress + range.Length - 1:X4}: Address/jump table ({range.Length} bytes, {wordCount} entries)",
                addressRatio > 0.8 ? "High" : "Medium",
                details);
        }

        if (byteRatio > 0.5)
        {
            details.Add($"Lookup table: {byteTableScore}/{range.Length} bytes are non-instruction-like ({byteRatio:P1})");
            return (
                $"${startAddress:X4}–${startAddress + range.Length - 1:X4}: Lookup/data table ({range.Length} bytes)",
                byteRatio > 0.7 ? "Medium" : "Low",
                details);
        }

        return null;
    }

    /// <summary>
    /// Heuristic 5: Display list detection — ANTIC display list opcodes.
    /// </summary>
    private static (string Description, string Confidence, List<string> Details)? DetectDisplayList(byte[] range, ushort startAddress)
    {
        // ANTIC display list opcodes: $40–$4F, $70, $80–$8F, $C0–$CF, $F0
        var dlOpcodeCount = 0;
        var lmsCount = 0;

        for (var i = 0; i < range.Length; )
        {
            var b = range[i];
            var isDlOpcodes = (b >= 0x40 && b <= 0x4F) || b == 0x70
                || (b >= 0x80 && b <= 0x8F) || (b >= 0xC0 && b <= 0xCF) || b == 0xF0;

            if (isDlOpcodes)
            {
                dlOpcodeCount++;
                // Check if this is an LMS instruction (bit 6 set, which means it has a 2-byte address operand)
                if ((b & 0x40) != 0 && i + 2 < range.Length)
                {
                    lmsCount++;
                    i += 3; // opcode + 2-byte address
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        if (dlOpcodeCount >= 3 && (double)dlOpcodeCount / range.Length > 0.3)
        {
            return (
                $"${startAddress:X4}–${startAddress + range.Length - 1:X4}: ANTIC display list ({range.Length} bytes)",
                dlOpcodeCount >= 5 ? "High" : "Medium",
                new List<string>
                {
                    $"Display list opcodes: {dlOpcodeCount}",
                    $"LMS instructions: {lmsCount}"
                });
        }

        return null;
    }

    /// <summary>
    /// Heuristic 6: Sprite data detection — 8/16/32-byte aligned blocks.
    /// </summary>
    private static (string Description, string Confidence, List<string> Details)? DetectSpriteData(byte[] range, ushort startAddress)
    {
        var details = new List<string>();

        // Check for common sprite sizes
        var alignment8 = range.Length % 8 == 0;
        var alignment16 = range.Length % 16 == 0;
        var alignment32 = range.Length % 32 == 0;

        if (alignment8 || alignment16 || alignment32)
        {
            var blockSize = alignment32 ? 32 : alignment16 ? 16 : 8;
            var blockCount = range.Length / blockSize;

            // Sprites typically have varied byte values (not all $00 or $FF)
            var uniqueBytes = new HashSet<byte>(range).Count;
            var hasVariety = uniqueBytes > 4;

            if (hasVariety)
            {
                details.Add($"Block size: {blockSize} bytes × {blockCount} blocks");
                details.Add($"Unique byte values: {uniqueBytes}/256");

                return (
                    $"${startAddress:X4}–${startAddress + range.Length - 1:X4}: Possible sprite data ({range.Length} bytes)",
                    blockCount >= 2 ? "Medium" : "Low",
                    details);
            }
        }

        return null;
    }

    /// <summary>
    /// Heuristic 7: Map data detection — 2D grid patterns.
    /// </summary>
    private static (string Description, string Confidence, List<string> Details)? DetectMapData(byte[] range, ushort startAddress)
    {
        // Check for consistent row lengths (common: 20, 40, 80, 24, 48 bytes per row)
        var potentialRowLengths = new[] { 20, 24, 30, 40, 48, 60, 80, 96, 120, 160, 200 };
        var details = new List<string>();

        foreach (var rowLen in potentialRowLengths)
        {
            if (range.Length % rowLen == 0 && range.Length >= rowLen * 2)
            {
                var rows = range.Length / rowLen;
                // Check for repeating byte patterns at row boundaries
                var patternConsistency = 0;
                for (var row = 1; row < rows; row++)
                {
                    if (range[row * rowLen] == range[0]) patternConsistency++;
                }

                if (patternConsistency > rows / 2)
                {
                    details.Add($"Row length: {rowLen} bytes × {rows} rows");
                    details.Add($"Row start byte consistency: {patternConsistency}/{rows - 1}");

                    return (
                        $"${startAddress:X4}–${startAddress + range.Length - 1:X4}: Possible map/tile data ({range.Length} bytes)",
                        "Medium",
                        details);
                }
            }
        }

        return null;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static int CountConsecutiveRuns(byte[] data, byte value, int minRunLength)
    {
        var total = 0;
        var runLength = 0;

        foreach (var b in data)
        {
            if (b == value)
            {
                runLength++;
            }
            else
            {
                if (runLength >= minRunLength)
                {
                    total += runLength;
                }
                runLength = 0;
            }
        }

        if (runLength >= minRunLength)
        {
            total += runLength;
        }

        return total;
    }

    private static int CompareConfidence(string a, string b)
    {
        var order = new Dictionary<string, int> { ["High"] = 3, ["Medium"] = 2, ["Low"] = 1 };
        return order.GetValueOrDefault(a, 0).CompareTo(order.GetValueOrDefault(b, 0));
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}