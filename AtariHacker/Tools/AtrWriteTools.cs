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
        string? filesystem = null,
        string? manifest = null,
        bool dryRun = false)
    {
        try
        {
            // If a manifest is provided, parse it and use its values
            DiskManifest? parsedManifest = null;
            if (manifest is not null)
            {
                parsedManifest = ParseManifest(manifest);
                sectors = parsedManifest.Sectors;
                density = parsedManifest.Density;
                filesystem ??= parsedManifest.Filesystem;
            }

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
                var lines = new List<string>
                {
                    $"# DRY RUN: Would create ATR at {output}",
                    $"#   Density: {density.ToUpperInvariant()} ({sectorSize} bytes/sector)",
                    $"#   Sectors: {sectors}",
                    $"#   Total size: {totalSize} bytes"
                };
                if (filesystem is not null)
                    lines.Add($"#   Filesystem: {filesystem}");
                if (parsedManifest?.Files.Count > 0)
                    lines.Add($"#   Files to inject: {parsedManifest.Files.Count}");
                lines.Add("# Run without --dry-run to apply.");
                return string.Join('\n', lines);
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

            // Create the raw ATR image
            var image = new byte[header.Length + dataBytes];
            Array.Copy(header, 0, image, 0, header.Length);
            // remaining bytes are already zero-initialized

            // Initialize filesystem if requested
            var geo = new AtrGeometry(sectorSize, sectors, density);
            if (string.Equals(filesystem, "dos2", StringComparison.OrdinalIgnoreCase))
            {
                InitializeDosFilesystem(image, geo);
            }
            else if (string.Equals(filesystem, "spartados", StringComparison.OrdinalIgnoreCase))
            {
                InitializeSpartaDosFilesystem(image, geo);
            }

            // Write the image to disk
            File.WriteAllBytes(output, image);

            // If manifest has files, inject them into the created image
            var fileCount = 0;
            if (parsedManifest?.Files.Count > 0)
            {
                var filesystemType = parsedManifest.Filesystem ?? filesystem ?? "dos2";
                foreach (var fileEntry in parsedManifest.Files)
                {
                    var injectResult = InjectFileDuringCreation(
                        output, fileEntry, image, geo, filesystemType, parsedManifest.Boot);
                    if (!injectResult.StartsWith("ERROR:", StringComparison.Ordinal))
                        fileCount++;
                }
            }

            var result = $"Created ATR: {output} ({sectors} \u00d7 {sectorSize} bytes = {totalSize} bytes total)";
            if (filesystem is not null)
                result += $"\n  Filesystem: {filesystem} initialized";
            if (fileCount > 0)
                result += $"\n  Injected {fileCount} file(s) from manifest";
            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Manifest Parsing ───────────────────────────────────────────────────

    private static DiskManifest ParseManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest file not found: {manifestPath}");

        var json = File.ReadAllText(manifestPath);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<DiskManifest>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest is null)
            throw new InvalidDataException("Failed to parse manifest file.");

        if (manifest.Sectors <= 0)
            throw new InvalidDataException("Manifest must specify a positive number of sectors.");

        return manifest;
    }

    // ─── DOS 2.x Filesystem Initialization ─────────────────────────────────

    private static void InitializeDosFilesystem(byte[] image, AtrGeometry geo)
    {
        if (geo.SectorCount < 368)
            throw new InvalidDataException($"DOS 2.x filesystem requires at least 368 sectors (got {geo.SectorCount}).");

        // Write boot sectors (sectors 1-3)
        // Standard DOS 2.x boot header: flag=0, 3 sectors, load=$0700, init=$0700
        WriteBootSector(image, geo, 1, InitializeBootSectorData(0, 3, 0x0700, 0x0700));
        WriteBootSector(image, geo, 2, new byte[SectorSizeForSector(geo, 2)]);
        WriteBootSector(image, geo, 3, new byte[SectorSizeForSector(geo, 3)]);

        // Write VTOC sector (sector 360)
        WriteVtocSector(image, geo);

        // Write empty directory sectors (sectors 361-368)
        var maxDirSector = Math.Min(368, geo.SectorCount);
        for (var sector = 361; sector <= maxDirSector; sector++)
        {
            var dirSector = new byte[SectorSizeForSector(geo, sector)];
            WriteSectorRaw(image, geo, sector, dirSector);
        }
    }

    private static void WriteBootSector(byte[] image, AtrGeometry geo, int sectorNumber, byte[] data)
    {
        var offset = SectorFileOffset(geo, sectorNumber);
        Array.Copy(data, 0, image, offset, data.Length);
    }

    private static byte[] InitializeBootSectorData(byte flag, int sectorCount, ushort loadAddress, ushort initAddress)
    {
        var data = new byte[128];
        data[0] = flag;
        data[1] = (byte)sectorCount;
        data[2] = (byte)(loadAddress & 0xFF);
        data[3] = (byte)((loadAddress >> 8) & 0xFF);
        data[4] = (byte)(initAddress & 0xFF);
        data[5] = (byte)((initAddress >> 8) & 0xFF);
        return data;
    }

    private static void WriteVtocSector(byte[] image, AtrGeometry geo)
    {
        var vtoc = new byte[geo.SectorSize == 256 ? 256 : 128];
        // DOS 2.x VTOC format:
        // Byte 0: Directory sectors (8 for a standard 720-sector disk)
        var dirSectors = Math.Min(geo.SectorCount - 360, 8);
        vtoc[0] = (byte)dirSectors;
        // Bytes 1-2: Total sectors (little-endian)
        vtoc[1] = (byte)(geo.SectorCount & 0xFF);
        vtoc[2] = (byte)((geo.SectorCount >> 8) & 0xFF);
        // Bytes 3-4: Free sectors
        var freeSectors = geo.SectorCount - 9 - dirSectors; // 3 boot + 1 VTOC + dirSectors
        vtoc[3] = (byte)(freeSectors & 0xFF);
        vtoc[4] = (byte)((freeSectors >> 8) & 0xFF);
        // Byte 5: MAP flag (0x00 for standard)
        vtoc[5] = 0x00;
        // Bytes 6-9: Reserved (usually 0)
        // Bytes 10+: Bitmap (1 = free, 0 = allocated)
        // Mark boot sectors (1-3), VTOC (360), and directory sectors (361-368) as used
        var bitmap = new bool[geo.SectorCount];
        // Mark all sectors as free initially
        for (var i = 0; i < geo.SectorCount; i++)
            bitmap[i] = true;
        // Mark boot sectors as used
        bitmap[0] = false; // sector 1
        bitmap[1] = false; // sector 2
        bitmap[2] = false; // sector 3
        // Mark VTOC as used
        if (360 <= geo.SectorCount)
            bitmap[359] = false; // sector 360 (0-based index)
        // Mark directory sectors as used
        for (var i = 361; i <= 368 && i <= geo.SectorCount; i++)
            bitmap[i - 1] = false;

        // Write bitmap into VTOC starting at byte 10
        const int bitmapOffset = 10;
        for (var sector = 0; sector < geo.SectorCount; sector++)
        {
            var byteIndex = bitmapOffset + (sector / 8);
            if (byteIndex >= vtoc.Length) break;
            var bitIndex = sector % 8;
            if (bitmap[sector])
                vtoc[byteIndex] |= (byte)(1 << bitIndex);
        }

        WriteSectorRaw(image, geo, 360, vtoc);
    }

    // ─── SpartaDOS Filesystem Initialization ────────────────────────────────

    private static void InitializeSpartaDosFilesystem(byte[] image, AtrGeometry geo)
    {
        if (geo.SectorCount < 5)
            throw new InvalidDataException($"SpartaDOS filesystem requires at least 5 sectors (got {geo.SectorCount}).");

        // Write boot sectors (sectors 1-3)
        WriteBootSector(image, geo, 1, InitializeBootSectorData(0, 3, 0x0700, 0x0700));
        WriteBootSector(image, geo, 2, new byte[SectorSizeForSector(geo, 2)]);
        WriteBootSector(image, geo, 3, new byte[SectorSizeForSector(geo, 3)]);

        // Write VTOC/bitmap sector (sector 4)
        WriteSpartaVtocSector(image, geo);

        // Write first directory sector (sector 5)
        var dirSector = new byte[SectorSizeForSector(geo, 5)];
        // Last 3 bytes are sector chain: 0 means no next sector
        WriteSectorRaw(image, geo, 5, dirSector);
    }

    private static void WriteSpartaVtocSector(byte[] image, AtrGeometry geo)
    {
        var vtoc = new byte[SectorSizeForSector(geo, 4)];
        // SpartaDOS VTOC format:
        // Bytes 0-1: Total free sectors (little-endian)
        var freeSectors = geo.SectorCount - 5; // 3 boot + 1 VTOC + 1 directory
        vtoc[0] = (byte)(freeSectors & 0xFF);
        vtoc[1] = (byte)((freeSectors >> 8) & 0xFF);
        // Bytes 2-3: Reserved (usually 0)
        // Bytes 4-5: First directory sector (10-bit value)
        var firstDirSector = 5;
        vtoc[4] = (byte)(firstDirSector & 0xFF);
        vtoc[5] = (byte)((firstDirSector >> 8) & 0x03);
        // Bytes 6+: Bitmap (1 = free, 0 = allocated)
        const int bitmapOffset = 6;
        // Mark boot sectors (1-3), VTOC (4), and directory (5) as used
        for (var sector = 0; sector < geo.SectorCount; sector++)
        {
            var byteIndex = bitmapOffset + (sector / 8);
            if (byteIndex >= vtoc.Length) break;
            var bitIndex = sector % 8;
            // Mark as free (1) by default, used (0) for sectors 1-5
            if (sector >= 5) // sectors 6+ are free
                vtoc[byteIndex] |= (byte)(1 << bitIndex);
        }

        WriteSectorRaw(image, geo, 4, vtoc);
    }

    // ─── File Injection During Creation ─────────────────────────────────────

    private static string InjectFileDuringCreation(
        string outputPath,
        FileManifest fileEntry,
        byte[] image,
        AtrGeometry geo,
        string filesystemType,
        BootManifest? bootManifest)
    {
        if (!File.Exists(fileEntry.File))
            return $"ERROR: Source file not found: {fileEntry.File}";

        var inputData = File.ReadAllBytes(fileEntry.File);

        if (string.Equals(filesystemType, "spartados", StringComparison.OrdinalIgnoreCase))
        {
            return InjectSpartaFileDuringCreation(outputPath, fileEntry, image, geo, inputData);
        }
        else
        {
            return InjectDosFileDuringCreation(outputPath, fileEntry, image, geo, inputData);
        }
    }

    private static string InjectDosFileDuringCreation(
        string outputPath,
        FileManifest fileEntry,
        byte[] image,
        AtrGeometry geo,
        byte[] inputData)
    {
        var dataPerSector = geo.SectorSize - 3;
        var requiredSectors = (inputData.Length + dataPerSector - 1) / dataPerSector;

        // Find free sectors starting from 369 (after directory)
        var startSector = FindNextFreeSector(image, geo, 369);
        if (startSector < 0)
            return "ERROR: No free sectors available for file injection.";

        if (startSector + requiredSectors > geo.SectorCount)
            return $"ERROR: Not enough free sectors (need {requiredSectors}, have {geo.SectorCount - startSector}).";

        // Write data to sectors
        var bytesWritten = 0;
        var remaining = inputData.Length;
        var currentSector = startSector;

        for (var i = 0; i < requiredSectors; i++)
        {
            var sectorLen = SectorSizeForSector(geo, currentSector);
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

            WriteSectorRaw(image, geo, currentSector, sectorData);
            currentSector++;
        }

        // Find a free directory slot
        var dirEntryOffset = FindFreeDirectorySlotInImage(image, geo);
        if (dirEntryOffset < 0)
            return "ERROR: No free directory slots.";

        // Write directory entry
        var dirSectorNum = dirEntryOffset / 8 + 361;
        var dirData = ReadSectorFromImage(image, geo, dirSectorNum);
        var entryInSector = dirEntryOffset % 8;
        var entryOffset = entryInSector * 16;

        // Parse filename (8.3 format)
        var parsedName = ParseAtariFilename(fileEntry.Name);

        // Flags byte: 0x42 for binary, 0x00 for non-binary
        dirData[entryOffset] = 0x42;
        dirData[entryOffset + 1] = (byte)(requiredSectors & 0xFF);
        dirData[entryOffset + 2] = (byte)((requiredSectors >> 8) & 0xFF);
        dirData[entryOffset + 3] = (byte)(startSector & 0xFF);
        dirData[entryOffset + 4] = (byte)((startSector >> 8) & 0xFF);

        // Write filename (8 chars, padded with spaces)
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(parsedName.Name.PadRight(8));
        Array.Copy(nameBytes, 0, dirData, entryOffset + 5, Math.Min(8, nameBytes.Length));

        // Write extension (3 chars, padded with spaces)
        var extBytes = System.Text.Encoding.ASCII.GetBytes(parsedName.Extension.PadRight(3));
        Array.Copy(extBytes, 0, dirData, entryOffset + 13, Math.Min(3, extBytes.Length));

        WriteSectorRaw(image, geo, dirSectorNum, dirData);

        // Update VTOC bitmap
        UpdateVtocBitmap(image, geo, startSector, requiredSectors, false);

        return $"Injected {parsedName.Name}.{parsedName.Extension} ({inputData.Length} bytes, {requiredSectors} sectors)";
    }

    private static string InjectSpartaFileDuringCreation(
        string outputPath,
        FileManifest fileEntry,
        byte[] image,
        AtrGeometry geo,
        byte[] inputData)
    {
        var dataPerSector = geo.SectorSize - 3;
        var requiredSectors = (inputData.Length + dataPerSector - 1) / dataPerSector;

        // Find free sectors starting from 6 (after boot, VTOC, and directory)
        var startSector = FindNextFreeSectorInSparta(image, geo, 6);
        if (startSector < 0)
            return "ERROR: No free sectors available for file injection.";

        if (startSector + requiredSectors > geo.SectorCount)
            return $"ERROR: Not enough free sectors (need {requiredSectors}, have {geo.SectorCount - startSector}).";

        // Write data to sectors
        var bytesWritten = 0;
        var remaining = inputData.Length;
        var currentSector = startSector;

        for (var i = 0; i < requiredSectors; i++)
        {
            var sectorLen = SectorSizeForSector(geo, currentSector);
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

            WriteSectorRaw(image, geo, currentSector, sectorData);
            currentSector++;
        }

        // Find a free SpartaDOS directory entry slot
        var dirSectorNum = 5;
        var dirData = ReadSectorFromImage(image, geo, dirSectorNum);
        var entriesPerSector = (dirData.Length - 3) / 16;
        var freeSlot = -1;

        for (var i = 0; i < entriesPerSector; i++)
        {
            if (dirData[i * 16] == 0)
            {
                freeSlot = i;
                break;
            }
        }

        if (freeSlot < 0)
            return "ERROR: No free SpartaDOS directory slots.";

        // Write SpartaDOS directory entry (16 bytes)
        var entryOffset = freeSlot * 16;
        // Flags: 0x01 (binary) | 0x00 (non-deleted)
        var flags = (byte)0x01;
        dirData[entryOffset] = flags;
        // Time (2 bytes): 0x0000 for simplicity
        dirData[entryOffset + 1] = 0x00;
        dirData[entryOffset + 2] = 0x00;
        // Date (1 byte): 0x00 for simplicity
        dirData[entryOffset + 3] = 0x00;
        // Name length
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(fileEntry.Name);
        dirData[entryOffset + 4] = (byte)Math.Min(nameBytes.Length, 11);
        // Filename (up to 11 bytes)
        Array.Copy(nameBytes, 0, dirData, entryOffset + 5, Math.Min(nameBytes.Length, 11));
        // Start sector (10-bit value, bytes 13-14)
        dirData[entryOffset + 13] = (byte)(startSector & 0xFF);
        dirData[entryOffset + 14] = (byte)((startSector >> 8) & 0x03);
        // Byte 15: unused (0)

        WriteSectorRaw(image, geo, dirSectorNum, dirData);

        // Update SpartaDOS VTOC bitmap
        UpdateSpartaBitmap(image, geo, startSector, requiredSectors, false);

        return $"Injected {fileEntry.Name} ({inputData.Length} bytes, {requiredSectors} sectors)";
    }

    // ─── Helper Methods ─────────────────────────────────────────────────────

    private static int SectorSizeForSector(AtrGeometry geo, int sectorNumber)
    {
        return sectorNumber <= 3 && geo.SectorSize == 256 ? 128 : geo.SectorSize;
    }

    private static void WriteSectorRaw(byte[] image, AtrGeometry geo, int sectorNumber, byte[] sectorData)
    {
        var offset = SectorFileOffset(geo, sectorNumber);
        if (offset + sectorData.Length > image.Length)
            throw new InvalidOperationException($"Sector {sectorNumber} extends beyond ATR image.");
        Array.Copy(sectorData, 0, image, offset, sectorData.Length);
    }

    private static byte[] ReadSectorFromImage(byte[] image, AtrGeometry geo, int sectorNumber)
    {
        var length = SectorSizeForSector(geo, sectorNumber);
        var offset = SectorFileOffset(geo, sectorNumber);
        var buffer = new byte[length];
        Array.Copy(image, offset, buffer, 0, length);
        return buffer;
    }

    private static int FindNextFreeSector(byte[] image, AtrGeometry geo, int startFrom)
    {
        var bitmap = AtrParser.GetSectorBitmap(image, geo);
        for (var i = startFrom - 1; i < bitmap.Length; i++)
        {
            if (bitmap[i])
                return i + 1;
        }
        return -1;
    }

    private static int FindNextFreeSectorInSparta(byte[] image, AtrGeometry geo, int startFrom)
    {
        var bitmap = AtrParser.GetSpartaBitmap(image, geo);
        for (var i = startFrom - 1; i < bitmap.Length; i++)
        {
            if (bitmap[i])
                return i + 1;
        }
        return -1;
    }

    private static int FindFreeDirectorySlotInImage(byte[] image, AtrGeometry geo)
    {
        for (var sector = 361; sector <= 368 && sector <= geo.SectorCount; sector++)
        {
            var dirData = ReadSectorFromImage(image, geo, sector);
            for (var i = 0; i < 8; i++)
            {
                var offset = i * 16;
                if (dirData[offset] == 0)
                {
                    return (sector - 361) * 8 + i;
                }
            }
        }
        return -1;
    }

    private static void UpdateVtocBitmap(byte[] image, AtrGeometry geo, int startSector, int sectorCount, bool isFree)
    {
        var vtoc = ReadSectorFromImage(image, geo, 360);
        if (vtoc.Length <= 10) return;

        const int bitmapOffset = 10;
        for (var i = 0; i < sectorCount; i++)
        {
            var sector = startSector + i - 1;
            if (sector < 0 || sector >= geo.SectorCount) continue;

            var byteIndex = bitmapOffset + (sector / 8);
            if (byteIndex >= vtoc.Length) break;
            var bitIndex = sector % 8;

            if (isFree)
                vtoc[byteIndex] |= (byte)(1 << bitIndex);
            else
                vtoc[byteIndex] &= (byte)~(1 << bitIndex);
        }

        // Update free sector count in VTOC
        var freeCount = UpdateFreeSectorCount(image, geo, vtoc, sectorCount, isFree);
        vtoc[3] = (byte)(freeCount & 0xFF);
        vtoc[4] = (byte)((freeCount >> 8) & 0xFF);

        WriteSectorRaw(image, geo, 360, vtoc);
    }

    private static void UpdateSpartaBitmap(byte[] image, AtrGeometry geo, int startSector, int sectorCount, bool isFree)
    {
        var vtoc = ReadSectorFromImage(image, geo, 4);
        if (vtoc.Length <= 6) return;

        const int bitmapOffset = 6;
        for (var i = 0; i < sectorCount; i++)
        {
            var sector = startSector + i - 1;
            if (sector < 0 || sector >= geo.SectorCount) continue;

            var byteIndex = bitmapOffset + (sector / 8);
            if (byteIndex >= vtoc.Length) break;
            var bitIndex = sector % 8;

            if (isFree)
                vtoc[byteIndex] |= (byte)(1 << bitIndex);
            else
                vtoc[byteIndex] &= (byte)~(1 << bitIndex);
        }

        // Update free sector count in VTOC
        var currentFree = vtoc[0] | (vtoc[1] << 8);
        if (isFree)
            currentFree += sectorCount;
        else
            currentFree -= sectorCount;
        vtoc[0] = (byte)(currentFree & 0xFF);
        vtoc[1] = (byte)((currentFree >> 8) & 0xFF);

        WriteSectorRaw(image, geo, 4, vtoc);
    }

    private static int UpdateFreeSectorCount(byte[] image, AtrGeometry geo, byte[] vtoc, int sectorCount, bool isFree)
    {
        var currentFree = vtoc[3] | (vtoc[4] << 8);
        return isFree ? currentFree + sectorCount : currentFree - sectorCount;
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