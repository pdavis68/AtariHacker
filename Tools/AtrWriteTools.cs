using AtariHackerMCP.Atari;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

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
            return $"Extracted {match.FileName}.{match.Extension} ({extracted.Length} bytes) → {output}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string InjectAtrFile(
        string filePath,
        string name,
        string input)
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
            var originalCapacity = match.SectorCount * (geo.SectorSize - 3); // -3 for chain bytes
            // First sector of a file uses full sector capacity, subsequent use sector - 3
            var fileCapacity = (geo.SectorSize - 3) * match.SectorCount;
            if (inputData.Length > fileCapacity)
            {
                return $"ERROR: Input file ({inputData.Length} bytes) exceeds available capacity ({fileCapacity} bytes) for {match.SectorCount} sectors.";
            }

            // Build modified ATR (copy-on-write)
            var modifiedPath = GetModifiedPath(filePath);
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

            return $"Injected {input} ({inputData.Length} bytes) into {match.FileName}.{match.Extension} → {modifiedPath}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string CreateAtr(
        string output,
        int sectors,
        string density)
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

            return $"Created ATR: {output} ({sectors} × {sectorSize} bytes = {dataBytes + 16} bytes total)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string WriteAtrSector(
        string filePath,
        string sector,
        string input)
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
                return $"ERROR: Sector {sectorNum} is out of range (1–{geo.SectorCount}).";

            var inputData = File.ReadAllBytes(input);
            var sectorLen = sectorNum <= 3 && geo.SectorSize == 256 ? 128 : geo.SectorSize;

            if (inputData.Length != sectorLen)
                return $"ERROR: Input file size ({inputData.Length} bytes) doesn't match sector size ({sectorLen} bytes).";

            // Copy-on-write: create modified ATR
            var modifiedPath = GetModifiedPath(filePath);
            var modifiedData = (byte[])data.Clone();

            var offset = SectorFileOffset(geo, sectorNum);
            Array.Copy(inputData, 0, modifiedData, offset, inputData.Length);

            File.WriteAllBytes(modifiedPath, modifiedData);
            return $"Wrote {inputData.Length} bytes to sector {sectorNum} → {modifiedPath}";
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
        string? startSector = null)
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

            // Copy-on-write
            var modifiedPath = GetModifiedPath(filePath);
            var modifiedData = (byte[])data.Clone();

            // Check directory for free slot
            var directory = AtrParser.ReadDirectory(modifiedData);
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

            // Check VTOC for free sectors
            // (Simplified: allocate from end of disk)
            if (startSector is not null)
            {
                startSectorNum = AddressParser.ParseAddress(startSector);
            }
            else
            {
                // Find first free sector after directory
                startSectorNum = 369; // First sector after directory
            }

            if (startSectorNum + requiredSectors > geo.SectorCount)
                return $"ERROR: Not enough free sectors (need {requiredSectors}, have {geo.SectorCount - startSectorNum}).";

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
            return $"Wrote {parsedName.Name}.{parsedName.Extension} ({inputData.Length} bytes, {requiredSectors} sectors) → {modifiedPath}";
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

    private static string GetModifiedPath(string originalPath)
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