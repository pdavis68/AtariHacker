using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class AtrWriteTools
{
    public static string ExtractAtrFile(
        string filePath,
        string name,
        string output)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"ERROR: ATR file not found: {filePath}";

            var data = File.ReadAllBytes(filePath);
            if (!AtrParser.IsAtr(data))
                return $"ERROR: Not a valid ATR image: {filePath}";

            var geo = AtrParser.ParseGeometry(data);
            var directory = AtrParser.ReadDirectory(data);
            var match = MatchEntry(directory, name);
            if (match is null || match.IsDeleted)
                return $"ERROR: File \"{name}\" not found in ATR directory.";

            var extracted = AtrParser.ExtractFile(data, geo, match);

            var parent = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.WriteAllBytes(output, extracted);
            return $"Extracted {match.FileName}.{match.Extension} ({extracted.Length} bytes) \u2192 {output}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string InjectAtrFile(
        string filePath,
        string name,
        string input,
        bool dryRun = false)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"ERROR: ATR file not found: {filePath}";
            if (!File.Exists(input))
                return $"ERROR: Input file not found: {input}";

            var data = File.ReadAllBytes(filePath);
            if (!AtrParser.IsAtr(data))
                return $"ERROR: Not a valid ATR image: {filePath}";

            var geo = AtrParser.ParseGeometry(data);
            var directory = AtrParser.ReadDirectory(data);
            var match = MatchEntry(directory, name);
            if (match is null || match.IsDeleted)
                return $"ERROR: File \"{name}\" not found in ATR directory.";

            var inputData = File.ReadAllBytes(input);

            // Check if input data fits within the original file's allocated sectors
            var fileCapacity = (geo.SectorSize - 3) * match.SectorCount;
            if (inputData.Length > fileCapacity)
            {
                return $"ERROR: Input file ({inputData.Length} bytes) exceeds available capacity ({fileCapacity} bytes) for {match.SectorCount} sectors.";
            }

            var modifiedPath = GetModifiedPath(filePath);

            if (dryRun)
            {
                return $"# DRY RUN: Inject '{name}' into {filePath}\n"
                    + $"#   File size: {inputData.Length} bytes\n"
                    + $"#   Target file: {match.FileName}.{match.Extension}\n"
                    + $"#   Allocated sectors: {match.SectorCount} (capacity: {fileCapacity} bytes)\n"
                    + $"#   Output: {modifiedPath}\n"
                    + $"# Run without --dry-run to apply changes.";
            }

            // Build modified ATR (copy-on-write)
            var modifiedData = (byte[])data.Clone();

            // Write new data to the sector chain
            var sector = match.StartSector;
            var bytesWritten = 0;
            var remaining = inputData.Length;

            while (sector != 0 && remaining > 0)
            {
                var sectorData = AtrParser.ReadSector(modifiedData, geo, sector);
                var dataCapacity = sectorData.Length - 3;

                // Check for data too small for sector
                if (sectorData.Length < 3) break;

                var chunkSize = Math.Min(remaining, dataCapacity);
                Array.Copy(inputData, bytesWritten, sectorData, 0, chunkSize);

                // Update the sector's count byte (last byte in sector)
                sectorData[^1] = (byte)chunkSize;

                // Write sector back
                WriteSector(modifiedData, geo, sector, sectorData);

                bytesWritten += chunkSize;
                remaining -= chunkSize;

                // Get next sector from chain
                var nextHi = sectorData[^3] & 0x03;
                var nextLo = sectorData[^2];
                sector = (nextHi << 8) | nextLo;
            }

            // Write modified ATR to disk
            File.WriteAllBytes(modifiedPath, modifiedData);

            return $"Injected {input} ({inputData.Length} bytes) into {match.FileName}.{match.Extension} \u2192 {modifiedPath}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string CreateAtr(
        string output,
        int sectors,
        string density,
        bool dryRun = false)
    {
        try
        {
            var sectorSize = density.ToLowerInvariant() switch
            {
                "sd" or "ed" => 128,
                "dd" => 256,
                _ => throw new ArgumentException("Invalid density. Use sd, dd, or ed.")
            };

            var dataBytes = sectorSize == 128
                ? sectors * 128
                : 3 * 128 + (sectors - 3) * 256;

            var totalSize = dataBytes + 16; // 16-byte header

            if (dryRun)
            {
                return $"# DRY RUN: Would create ATR at {output}\n"
                    + $"#   Density: {density.ToUpperInvariant()} ({sectorSize} bytes/sector)\n"
                    + $"#   Sectors: {sectors}\n"
                    + $"#   Total size: {totalSize} bytes\n"
                    + $"# Run without --dry-run to apply.";
            }

            var paragraphs = dataBytes / 16;

            // Build 16-byte ATR header
            var header = new byte[16];
            header[0] = 0x96; // Magic byte 1
            header[1] = 0x02; // Magic byte 2
            header[2] = (byte)(paragraphs & 0xFF);
            header[3] = (byte)((paragraphs >> 8) & 0xFF);
            header[4] = (byte)(sectorSize & 0xFF);
            header[5] = (byte)((sectorSize >> 8) & 0xFF);
            header[6] = (byte)((paragraphs >> 16) & 0xFF);
            header[7] = (byte)((paragraphs >> 24) & 0xFF);
            // bytes 8-15 are reserved (0)

            var parent = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            using var fs = new FileStream(output, FileMode.Create);
            fs.Write(header, 0, header.Length);
            fs.Write(new byte[dataBytes], 0, dataBytes);

            return $"Created ATR: {output} ({sectors} \u00d7 {sectorSize} bytes = {totalSize} bytes total)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string WriteAtrSector(
        string filePath,
        string sector,
        string input,
        bool dryRun = false)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"ERROR: ATR file not found: {filePath}";
            if (!File.Exists(input))
                return $"ERROR: Input file not found: {input}";

            var data = File.ReadAllBytes(filePath);
            if (!AtrParser.IsAtr(data))
                return $"ERROR: Not a valid ATR image: {filePath}";

            var geo = AtrParser.ParseGeometry(data);
            var sectorNum = AddressParser.ParseAddress(sector);

            if (sectorNum < 1 || sectorNum > geo.SectorCount)
                return $"ERROR: Sector {sectorNum} is out of range (1\u2013{geo.SectorCount}).";

            var inputData = File.ReadAllBytes(input);
            var sectorLen = sectorNum <= 3 && geo.SectorSize == 256 ? 128 : geo.SectorSize;

            if (inputData.Length != sectorLen)
                return $"ERROR: Input file size ({inputData.Length} bytes) doesn't match sector size ({sectorLen} bytes).";

            var modifiedPath = GetModifiedPath(filePath);

            if (dryRun)
            {
                // Show current sector bytes for diff preview
                var currentSector = AtrParser.ReadSector(data, geo, sectorNum);
                var diffLines = new System.Text.StringBuilder();
                diffLines.AppendLine($"# DRY RUN: Write to sector {sectorNum} of {filePath}");
                diffLines.AppendLine($"#   Sector size: {sectorLen} bytes");
                diffLines.AppendLine($"#   Input file: {input} ({inputData.Length} bytes)");
                diffLines.AppendLine($"#   Output: {modifiedPath}");
                diffLines.AppendLine("#   Changes:");
                for (var i = 0; i < Math.Min(currentSector.Length, inputData.Length); i++)
                {
                    if (currentSector[i] != inputData[i])
                    {
                        diffLines.AppendLine($"#     [{i}] {Formatting.HexByte(currentSector[i])} \u2192 {Formatting.HexByte(inputData[i])}");
                    }
                }
                diffLines.AppendLine("# Run without --dry-run to apply changes.");
                return diffLines.ToString();
            }

            // Copy-on-write: create modified ATR
            var modifiedData = (byte[])data.Clone();

            var offset = SectorFileOffset(geo, sectorNum);
            Array.Copy(inputData, 0, modifiedData, offset, inputData.Length);

            File.WriteAllBytes(modifiedPath, modifiedData);
            return $"Wrote {inputData.Length} bytes to sector {sectorNum} \u2192 {modifiedPath}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string WriteAtrFile(
        string filePath,
        string name,
        string input,
        string? startSector = null,
        bool dryRun = false)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"ERROR: ATR file not found: {filePath}";
            if (!File.Exists(input))
                return $"ERROR: Input file not found: {input}";

            var data = File.ReadAllBytes(filePath);
            if (!AtrParser.IsAtr(data))
                return $"ERROR: Not a valid ATR image: {filePath}";

            var geo = AtrParser.ParseGeometry(data);
            var inputData = File.ReadAllBytes(input);
            var parsedName = ParseAtariFilename(name);

            // Check directory for free slot
            var directory = AtrParser.ReadDirectory(data);
            if (directory.Count >= 64)
                return "ERROR: Directory is full (64 entries max).";

            // Check for existing file with same name (deleted or active)
            var existing = MatchEntry(directory, name);
            int startSectorNum;
            if (existing is not null && !existing.IsDeleted)
                return $"ERROR: File \"{name}\" already exists in directory. Use InjectAtrFile to replace.";

            // Calculate required sectors
            var dataPerSector = geo.SectorSize - 3;
            var requiredSectors = (inputData.Length + dataPerSector - 1) / dataPerSector;

            if (startSector is not null)
            {
                startSectorNum = AddressParser.ParseAddress(startSector);
            }
            else
            {
                startSectorNum = 369; // First sector after directory
            }

            if (startSectorNum + requiredSectors > geo.SectorCount)
                return $"ERROR: Not enough free sectors (need {requiredSectors}, have {geo.SectorCount - startSectorNum}).";

            var modifiedPath = GetModifiedPath(filePath);

            if (dryRun)
            {
                return $"# DRY RUN: Write file '{name}' to {filePath}\n"
                    + $"#   File size: {inputData.Length} bytes\n"
                    + $"#   Required sectors: {requiredSectors} (at {dataPerSector} bytes/sector)\n"
                    + $"#   Start sector: {startSectorNum}\n"
                    + $"#   Output: {modifiedPath}\n"
                    + $"# Run without --dry-run to apply changes.";
            }

            // Copy-on-write
            var modifiedData = (byte[])data.Clone();

            // Build sector chain and write data
            var bytesWritten = 0;
            var remaining = inputData.Length;
            var currentSector = startSectorNum;

            for (var i = 0; i < requiredSectors; i++)
            {
                var sectorLen = currentSector <= 3 && geo.SectorSize == 256 ? 128 : geo.SectorSize;
                var sectorData = new byte[sectorLen];
                var dataCapacity = sectorLen - 3;
                var chunkSize = Math.Min(remaining, dataCapacity);

                Array.Copy(inputData, bytesWritten, sectorData, 0, chunkSize);
                bytesWritten += chunkSize;
                remaining -= chunkSize;

                // Set chain bytes
                var nextSector = i < requiredSectors - 1 ? currentSector + 1 : 0;
                sectorData[^3] = (byte)((nextSector >> 8) & 0x03);
                sectorData[^2] = (byte)(nextSector & 0xFF);
                sectorData[^1] = (byte)chunkSize;

                WriteSector(modifiedData, geo, currentSector, sectorData);
                currentSector++;
            }

            // Find a free directory slot
            var dirEntryOffset = FindFreeDirectorySlot(modifiedData, geo);
            if (dirEntryOffset < 0)
                return "ERROR: No free directory slots.";

            // Write directory entry
            var dirData = AtrParser.ReadSector(modifiedData, geo, dirEntryOffset / 8 + 361);
            var entryInSector = dirEntryOffset % 8;
            var entryOffset = entryInSector * 16;

            // Flags byte: 0x42 for binary, 0x00 for non-binary (simplified: always mark as non-deleted)
            dirData[entryOffset] = 0x42; // Non-deleted, binary file flag
            dirData[entryOffset + 1] = (byte)(requiredSectors & 0xFF);
            dirData[entryOffset + 2] = (byte)((requiredSectors >> 8) & 0xFF);
            dirData[entryOffset + 3] = (byte)(startSectorNum & 0xFF);
            dirData[entryOffset + 4] = (byte)((startSectorNum >> 8) & 0xFF);

            // Write filename (8 chars, padded with spaces)
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(parsedName.Name.PadRight(8));
            Array.Copy(nameBytes, 0, dirData, entryOffset + 5, Math.Min(8, nameBytes.Length));

            // Write extension (3 chars, padded with spaces)
            var extBytes = System.Text.Encoding.ASCII.GetBytes(parsedName.Extension.PadRight(3));
            Array.Copy(extBytes, 0, dirData, entryOffset + 13, Math.Min(3, extBytes.Length));

            WriteSector(modifiedData, geo, dirEntryOffset / 8 + 361, dirData);

            File.WriteAllBytes(modifiedPath, modifiedData);
            return $"Wrote {parsedName.Name}.{parsedName.Extension} ({inputData.Length} bytes, {requiredSectors} sectors) \u2192 {modifiedPath}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string DefineFilesystem(
        string filePath,
        string directoryOffset,
        int entrySize,
        int filenameLength,
        int extensionLength,
        int startSectorOffset,
        int sectorCountOffset)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"ERROR: ATR file not found: {filePath}";

            var dirOffset = AddressParser.ParseAddress(directoryOffset);

            // Validate parameters
            if (entrySize <= 0) return "ERROR: entrySize must be positive.";
            if (filenameLength <= 0) return "ERROR: filenameLength must be positive.";
            if (extensionLength < 0) return "ERROR: extensionLength must be non-negative.";
            if (startSectorOffset < 0 || startSectorOffset >= entrySize)
                return "ERROR: startSectorOffset must be within entry bounds.";
            if (sectorCountOffset < 0 || sectorCountOffset >= entrySize)
                return "ERROR: sectorCountOffset must be within entry bounds.";

            // Store filesystem definition in sidecar JSON
            var sidecarPath = SessionPersistence.GetSidecarPath(filePath);
            var sidecarData = new Dictionary<string, object>
            {
                ["filesystem"] = new Dictionary<string, object>
                {
                    ["type"] = "custom",
                    ["directoryOffset"] = $"0x{dirOffset:X4}",
                    ["entrySize"] = entrySize,
                    ["filenameLength"] = filenameLength,
                    ["extensionLength"] = extensionLength,
                    ["startSectorOffset"] = startSectorOffset,
                    ["sectorCountOffset"] = sectorCountOffset
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(sidecarData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecarPath, json);

            return $"Custom filesystem defined for {filePath}.\n  Directory: ${dirOffset:X4}\n  Entry size: {entrySize}\n  Filename: {filenameLength}+{extensionLength}\n  Start sector at offset {startSectorOffset}\n  Sector count at offset {sectorCountOffset}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Batch Operations ──────────────────────────────────────────────────

    /// <summary>
    /// Execute a batch of ATR operations from a script file.
    /// Script format: one command per line, with key=value arguments.
    /// Lines starting with # are comments.
    /// Supported commands: extract, inject, extract-all, inject-all, sector-map, vtoc, file-frag, recover
    /// </summary>
    public static string BatchOperations(string filePath, string scriptPath, bool dryRun = false)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"ERROR: ATR file not found: {filePath}";
            if (!File.Exists(scriptPath))
                return $"ERROR: Script file not found: {scriptPath}";

            var script = File.ReadAllLines(scriptPath);
            var results = new List<string>();
            var lineNumber = 0;

            foreach (var rawLine in script)
            {
                lineNumber++;
                var line = rawLine.Trim();

                // Skip comments and blank lines
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                // Strip shell-style redirection
                var redirectIndex = line.IndexOf('>');
                if (redirectIndex >= 0)
                    line = line[..redirectIndex].Trim();

                // Split into command and arguments
                var parts = ParseBatchLine(line);
                if (parts.Count == 0)
                    continue;

                var command = parts[0].ToLowerInvariant();
                var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in parts.Skip(1))
                {
                    var eq = part.IndexOf('=');
                    if (eq >= 0)
                    {
                        var key = part[..eq].Trim();
                        var value = part[(eq + 1)..].Trim().Trim('\'', '"');
                        args[key] = value;
                    }
                }

                var result = ExecuteBatchCommand(filePath, command, args, dryRun);
                results.Add($"# Line {lineNumber}: {rawLine.Trim()}");
                results.Add(result);
                results.Add(string.Empty);
            }

            return string.Join('\n', results);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string ExecuteBatchCommand(string filePath, string command, Dictionary<string, string> args, bool dryRun)
    {
        try
        {
            switch (command)
            {
                case "extract":
                {
                    var name = args.GetValueOrDefault("name") ?? args.GetValueOrDefault("file") ?? string.Empty;
                    var output = args.GetValueOrDefault("output") ?? args.GetValueOrDefault("out") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                        return "ERROR: 'name' argument required for extract";
                    if (string.IsNullOrWhiteSpace(output))
                        output = name;
                    return ExtractAtrFile(filePath, name, output);
                }

                case "inject":
                {
                    var input = args.GetValueOrDefault("input") ?? args.GetValueOrDefault("src") ?? string.Empty;
                    var name = args.GetValueOrDefault("name") ?? args.GetValueOrDefault("file") ?? Path.GetFileName(input);
                    if (string.IsNullOrWhiteSpace(input))
                        return "ERROR: 'input' argument required for inject";
                    if (string.IsNullOrWhiteSpace(name))
                        return "ERROR: 'name' argument required for inject";
                    return InjectAtrFile(filePath, name, input, dryRun);
                }

                case "extract-all":
                {
                    var outputDir = args.GetValueOrDefault("output-dir") ?? args.GetValueOrDefault("dir");
                    return AtrTools.ExtractAll(filePath, outputDir);
                }

                case "inject-all":
                {
                    var sourceDir = args.GetValueOrDefault("source-dir") ?? args.GetValueOrDefault("dir") ?? ".";
                    var pattern = args.GetValueOrDefault("pattern");
                    return AtrTools.InjectAll(filePath, sourceDir, pattern, dryRun);
                }

                case "sector-map":
                {
                    var format = args.GetValueOrDefault("format") ?? "text";
                    return AtrForensicTools.SectorMap(filePath, format);
                }

                case "vtoc":
                {
                    return AtrForensicTools.ShowVtoc(filePath);
                }

                case "file-frag":
                {
                    var name = args.GetValueOrDefault("name") ?? args.GetValueOrDefault("file") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                        return "ERROR: 'name' argument required for file-frag";
                    return AtrForensicTools.FileFragmentation(filePath, name);
                }

                case "recover":
                {
                    var name = args.GetValueOrDefault("name") ?? args.GetValueOrDefault("file") ?? string.Empty;
                    var output = args.GetValueOrDefault("output") ?? args.GetValueOrDefault("out") ?? name;
                    if (string.IsNullOrWhiteSpace(name))
                        return "ERROR: 'name' argument required for recover";
                    return AtrForensicTools.RecoverDeletedFile(filePath, name, output);
                }

                default:
                    return $"ERROR: Unknown command '{command}'";
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static List<string> ParseBatchLine(string line)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;
        var quoteChar = ' ';

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuote)
            {
                if (c == quoteChar)
                {
                    inQuote = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '\'' or '"')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (c == ' ' || c == '\t')
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static AtrDirectoryEntry? MatchEntry(IReadOnlyList<AtrDirectoryEntry> directory, string name)
    {
        var parsed = ParseAtariFilename(name);
        foreach (var entry in directory)
        {
            if (entry.FileName.Equals(parsed.Name, StringComparison.OrdinalIgnoreCase)
                && entry.Extension.Equals(parsed.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }

    private static (string Name, string Extension) ParseAtariFilename(string name)
    {
        var dot = name.IndexOf('.');
        if (dot >= 0)
        {
            return (name[..dot], name[(dot + 1)..]);
        }
        return (name, "DAT");
    }

    internal static string GetModifiedPath(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var ext = Path.GetExtension(originalPath);
        return Path.Combine(dir, $"{name}.modified{ext}");
    }

    private static int SectorFileOffset(AtrGeometry geometry, int sectorNumber)
    {
        // Same logic as AtrParser.SectorFileOffset
        if (geometry.SectorSize == 256 && sectorNumber > 3)
        {
            return 16 + (3 * 128) + ((sectorNumber - 4) * geometry.SectorSize);
        }
        return 16 + ((sectorNumber - 1) * geometry.SectorSize);
    }

    private static void WriteSector(byte[] data, AtrGeometry geometry, int sectorNumber, byte[] sectorData)
    {
        var offset = SectorFileOffset(geometry, sectorNumber);
        if (offset + sectorData.Length > data.Length)
            throw new InvalidOperationException($"Sector {sectorNumber} extends beyond ATR data.");
        Array.Copy(sectorData, 0, data, offset, sectorData.Length);
    }

    private static int FindFreeDirectorySlot(byte[] data, AtrGeometry geometry)
    {
        for (var sector = 361; sector <= 368 && sector <= geometry.SectorCount; sector++)
        {
            var dirData = AtrParser.ReadSector(data, geometry, sector);
            for (var i = 0; i < 8; i++)
            {
                var offset = i * 16;
                if (dirData[offset] == 0)
                {
                    // Free slot found
                    return (sector - 361) * 8 + i;
                }
            }
        }
        return -1;
    }
}