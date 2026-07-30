using AtariHacker.Atari;
using AtariHacker.Helpers;

namespace AtariHacker.Tools;

// ─── Data Structures ─────────────────────────────────────────────────────

public enum SectorType { Boot, VTOC, Directory, FileData, Free }

public sealed record SectorInfo(
    int SectorNumber,
    SectorType Type,
    int? FileIndex,
    int? NextSector
);

public sealed record FragmentationResult(
    string FileName,
    int FileSize,
    int TotalSectors,
    int FragmentCount,
    double FragmentationRatio,
    List<int> SectorChain,
    List<(int From, int To)> Gaps
);

public sealed record RecoveryResult(
    string FileName,
    int OriginalSize,
    int RecoveredSize,
    int StartSector,
    bool Success,
    string? ErrorMessage
);

// ─── Forensic Tools ──────────────────────────────────────────────────────

public static class AtrForensicTools
{
    // ─────────────────────────────────────────────────────────────────────
    // Sector Map
    // ─────────────────────────────────────────────────────────────────────

    public static string SectorMap(string filePath, string format = "text")
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var data = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(data))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(data);
            var sectorInfo = BuildSectorInfo(data, geometry);

            return format.ToLowerInvariant() switch
            {
                "ascii" => FormatSectorMapAscii(resolvedPath, geometry, sectorInfo),
                "svg" => "ERROR: SVG format not yet implemented. Use 'text' or 'ascii'.",
                _ => FormatSectorMapText(resolvedPath, geometry, sectorInfo)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static List<SectorInfo> BuildSectorInfo(byte[] data, AtrGeometry geometry)
    {
        var info = new List<SectorInfo>();
        var hasDos = AtrParser.HasDosFilesystem(data);
        var hasSparta = !hasDos && AtrParser.HasSpartaDosFilesystem(data);
        var hasMyDos = !hasDos && !hasSparta && AtrParser.HasMyDosFilesystem(data);
        var directory = hasDos ? AtrParser.ReadDirectory(data) : null;
        List<MyDosDirectoryEntry>? myDosDirectory = null;
        HashSet<int>? myDosVtocSectors = null;
        List<SpartaDirEntry>? spartaDirectory = null;
        HashSet<int>? spartaDirSectors = null;

        if (hasMyDos)
        {
            myDosDirectory = AtrParser.ReadMyDosDirectory(data).ToList();
            try
            {
                myDosVtocSectors = new HashSet<int>(AtrParser.GetMyDosVtocChain(data, geometry));
            }
            catch
            {
                myDosVtocSectors = new HashSet<int> { 360 };
            }
        }

        if (hasSparta)
        {
            spartaDirectory = AtrParser.ReadSpartaDirectory(data);
            spartaDirSectors = new HashSet<int>(AtrParser.GetSpartaDirectorySectors(data, geometry));
        }

        for (var sector = 1; sector <= geometry.SectorCount; sector++)
        {
            var type = SectorType.Free;
            int? fileIndex = null;
            int? nextSector = null;

            if (sector <= 3)
            {
                type = SectorType.Boot;
            }
            else if (sector == 360 && hasDos)
            {
                type = SectorType.VTOC;
            }
            else if (sector >= 361 && sector <= 368 && (hasDos || hasMyDos))
            {
                type = SectorType.Directory;
            }
            else if (hasMyDos && myDosVtocSectors is not null && myDosVtocSectors.Contains(sector))
            {
                type = SectorType.VTOC;
            }
            else if (sector == 4 && hasSparta)
            {
                type = SectorType.VTOC;
            }
            else if (spartaDirSectors is not null && spartaDirSectors.Contains(sector))
            {
                type = SectorType.Directory;
            }
            else if (directory is not null)
            {
                // Check if this sector belongs to any DOS 2.x file
                var found = false;
                for (var fi = 0; fi < directory.Count; fi++)
                {
                    var entry = directory[fi];
                    if (entry.IsDeleted) continue;

                    try
                    {
                        var chain = AtrParser.GetSectorChain(data, geometry, entry.StartSector);
                        var idx = chain.IndexOf(sector);
                        if (idx >= 0)
                        {
                            type = SectorType.FileData;
                            fileIndex = fi;
                            nextSector = idx + 1 < chain.Count ? chain[idx + 1] : null;
                            found = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Skip files with corrupted chains
                    }
                }

                if (!found)
                {
                    type = SectorType.Free;
                }
            }
            else if (myDosDirectory is not null)
            {
                // Check if this sector belongs to any MyDOS file
                var found = false;
                for (var fi = 0; fi < myDosDirectory.Count; fi++)
                {
                    var entry = myDosDirectory[fi];
                    if (entry.IsDeleted) continue;

                    try
                    {
                        var chain = AtrParser.GetSectorChain(data, geometry, entry.StartSector);
                        var idx = chain.IndexOf(sector);
                        if (idx >= 0)
                        {
                            type = SectorType.FileData;
                            fileIndex = fi;
                            nextSector = idx + 1 < chain.Count ? chain[idx + 1] : null;
                            found = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Skip files with corrupted chains
                    }
                }

                if (!found)
                {
                    type = SectorType.Free;
                }
            }
            else if (spartaDirectory is not null)
            {
                // Check if this sector belongs to any SpartaDOS file
                var found = false;
                for (var fi = 0; fi < spartaDirectory.Count; fi++)
                {
                    var entry = spartaDirectory[fi];
                    if (entry.IsDeleted) continue;

                    try
                    {
                        var chain = AtrParser.GetSectorChain(data, geometry, entry.StartSector);
                        var idx = chain.IndexOf(sector);
                        if (idx >= 0)
                        {
                            type = SectorType.FileData;
                            fileIndex = fi;
                            nextSector = idx + 1 < chain.Count ? chain[idx + 1] : null;
                            found = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Skip files with corrupted chains
                    }
                }

                if (!found)
                {
                    type = SectorType.Free;
                }
            }
            // else: type stays SectorType.Free

            info.Add(new SectorInfo(sector, type, fileIndex, nextSector));
        }

        return info;
    }

    private static string FormatSectorMapText(string path, AtrGeometry geometry, List<SectorInfo> sectorInfo)
    {
        var lines = new List<string>
        {
            $"Sector map for {Path.GetFileName(path)} ({geometry.SectorCount} sectors, {geometry.Density}):"
        };

        // Group consecutive sectors of the same type
        var groups = new List<(SectorType Type, int Start, int End, string Label)>();
        var start = 1;
        while (start <= geometry.SectorCount)
        {
            var currentType = sectorInfo[start - 1].Type;
            var end = start;
            while (end < geometry.SectorCount && sectorInfo[end].Type == currentType)
            {
                end++;
            }

            var label = currentType switch
            {
                SectorType.Boot => "Boot loader",
                SectorType.VTOC => "Volume Table of Contents",
                SectorType.Directory => "Directory",
                SectorType.FileData => "File data",
                SectorType.Free => "Free",
                _ => "Unknown"
            };

            groups.Add((currentType, start, end, label));
            start = end + 1;
        }

        foreach (var g in groups)
        {
            if (g.Start == g.End)
            {
                lines.Add($"  Sectors {g.Start:D3}-{g.End:D3}: [{g.Type,-8}] {g.Label}");
            }
            else
            {
                lines.Add($"  Sectors {g.Start:D3}-{g.End:D3}: [{g.Type,-8}] {g.Label}");
            }
        }

        var used = sectorInfo.Count(s => s.Type != SectorType.Free);
        var free = sectorInfo.Count - used;
        var usedPct = (double)used / geometry.SectorCount * 100;

        lines.Add(string.Empty);
        lines.Add($"Usage: {used}/{geometry.SectorCount} sectors ({usedPct:F1}% used)");

        // Count free regions and find largest
        var freeRegions = new List<(int Start, int End)>();
        var i = 0;
        while (i < sectorInfo.Count)
        {
            if (sectorInfo[i].Type == SectorType.Free)
            {
                var fs = i + 1;
                while (i < sectorInfo.Count && sectorInfo[i].Type == SectorType.Free) i++;
                freeRegions.Add((fs, i));
            }
            else
            {
                i++;
            }
        }

        var largestFree = freeRegions.Count > 0 ? freeRegions.Max(r => r.End - r.Start + 1) : 0;
        lines.Add($"Fragmentation: {freeRegions.Count} free regions, largest: {largestFree} sectors");

        return string.Join('\n', lines);
    }

    private static string FormatSectorMapAscii(string path, AtrGeometry geometry, List<SectorInfo> sectorInfo)
    {
        var lines = new List<string>
        {
            $"Sector Map: {Path.GetFileName(path)} ({geometry.SectorCount} sectors)"
        };

        // Calculate grid dimensions: 60 chars wide
        const int gridWidth = 60;
        var rows = (int)Math.Ceiling((double)geometry.SectorCount / gridWidth);

        lines.Add("┌" + new string('─', gridWidth) + "┐");
        for (var row = 0; row < rows; row++)
        {
            var rowChars = new char[gridWidth];
            for (var col = 0; col < gridWidth; col++)
            {
                var sectorIdx = row * gridWidth + col;
                if (sectorIdx < geometry.SectorCount)
                {
                    rowChars[col] = sectorInfo[sectorIdx].Type == SectorType.Free ? '░' : '▓';
                }
                else
                {
                    rowChars[col] = ' ';
                }
            }
            lines.Add("│" + new string(rowChars) + "│");
        }
        lines.Add("└" + new string('─', gridWidth) + "┘");
        lines.Add("▓=Used  ░=Free");

        return string.Join('\n', lines);
    }

    // ─────────────────────────────────────────────────────────────────────
    // File Fragmentation
    // ─────────────────────────────────────────────────────────────────────

    public static string FileFragmentation(string filePath, string name)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var data = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(data))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(data);

            // Try DOS 2.x first
            if (AtrParser.HasDosFilesystem(data))
            {
                return AnalyzeFragmentationDos(data, geometry, name);
            }

            // Try SpartaDOS
            if (AtrParser.HasSpartaDosFilesystem(data))
            {
                return AnalyzeFragmentationSparta(data, geometry, name);
            }

            // Try MyDOS
            if (AtrParser.HasMyDosFilesystem(data))
            {
                return AnalyzeFragmentationMyDos(data, geometry, name);
            }

            return "ERROR: No DOS 2.x or SpartaDOS filesystem detected.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string AnalyzeFragmentationDos(byte[] data, AtrGeometry geometry, string name)
    {
        var directory = AtrParser.ReadDirectory(data);
        var match = MatchEntry(directory, name);
        if (match is null)
            return $"ERROR: File \"{name}\" not found in ATR directory.";

        var chain = AtrParser.GetSectorChain(data, geometry, match.StartSector);
        var fileData = AtrParser.ExtractFile(data, geometry, match);
        return FormatFragmentationResult(match.FileName + "." + match.Extension, fileData.Length, chain);
    }

    private static string AnalyzeFragmentationSparta(byte[] data, AtrGeometry geometry, string name)
    {
        var directory = AtrParser.ReadSpartaDirectory(data);
        var match = MatchSpartaEntry(directory, name);
        if (match is null)
            return $"ERROR: File \"{name}\" not found in SpartaDOS directory.";

        var chain = AtrParser.GetSectorChain(data, geometry, match.StartSector);
        var fileData = ExtractSpartaFile(data, geometry, match);
        return FormatFragmentationResult(match.FileName, fileData.Length, chain);
    }

    private static string AnalyzeFragmentationMyDos(byte[] data, AtrGeometry geometry, string name)
    {
        var directory = AtrParser.ReadMyDosDirectory(data);
        var match = directory.FirstOrDefault(e =>
            !e.IsDeleted && string.Equals(
                (string.IsNullOrWhiteSpace(e.Extension) ? e.FileName : $"{e.FileName}.{e.Extension}"),
                name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return $"ERROR: File \"{name}\" not found in MyDOS directory.";

        var chain = AtrParser.GetSectorChain(data, geometry, match.StartSector);
        var fileData = AtrParser.ExtractFile(data, geometry,
            new AtrDirectoryEntry(match.Index, match.FileName, match.Extension,
                match.StartSector, match.SectorCount, match.IsDeleted, match.IsLocked, match.IsBinary));
        return FormatFragmentationResult(match.FileName + "." + match.Extension, fileData.Length, chain);
    }

    private static string FormatFragmentationResult(string fileName, int fileSize, List<int> chain)
    {
        // Detect fragments: gaps where next sector != current + 1
        var gaps = new List<(int From, int To)>();
        for (var i = 0; i < chain.Count - 1; i++)
        {
            if (chain[i + 1] != chain[i] + 1)
            {
                gaps.Add((chain[i], chain[i + 1]));
            }
        }

        var fragmentCount = gaps.Count + 1;
        var fragRatio = chain.Count > 0 ? (double)gaps.Count / chain.Count * 100 : 0;

        var lines = new List<string>
        {
            $"Fragmentation analysis for {fileName}:",
            $"  File size: {fileSize:N0} bytes",
            $"  Total sectors: {chain.Count}",
            $"  Fragments: {fragmentCount}",
            $"  Fragmentation ratio: {fragRatio:F1}% {(fragRatio < 10 ? "(low)" : fragRatio < 30 ? "(moderate)" : "(high)")}",
            string.Empty,
            "  Sector chain:"
        };

        // Show sector chain in groups of 10
        for (var i = 0; i < chain.Count; i += 10)
        {
            var batch = chain.Skip(i).Take(10);
            lines.Add("    " + string.Join(" → ", batch.Select(s => $"{s:D3}")));
        }

        if (gaps.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("  Gaps:");
            foreach (var gap in gaps)
            {
                var gapSize = gap.To - gap.From;
                lines.Add($"    {gap.From:D3} → {gap.To:D3} (gap of {gapSize} sectors)");
            }
        }

        return string.Join('\n', lines);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Recover Deleted File
    // ─────────────────────────────────────────────────────────────────────

    public static string RecoverDeletedFile(string filePath, string name, string output)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var data = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(data))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(data);

            // Try DOS 2.x first
            if (AtrParser.HasDosFilesystem(data))
            {
                return RecoverDeletedDos(data, geometry, resolvedPath, name, output);
            }

            // Try SpartaDOS
            if (AtrParser.HasSpartaDosFilesystem(data))
            {
                return RecoverDeletedSparta(data, geometry, resolvedPath, name, output);
            }

            // Try MyDOS
            if (AtrParser.HasMyDosFilesystem(data))
            {
                return RecoverDeletedDos(data, geometry, resolvedPath, name, output);
            }

            return "ERROR: No DOS 2.x or SpartaDOS filesystem detected.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string RecoverDeletedDos(byte[] data, AtrGeometry geometry, string resolvedPath, string name, string output)
    {
        var deletedEntry = AtrParser.FindDeletedEntry(data, name);
        if (deletedEntry is null)
            return $"ERROR: No deleted file named \"{name}\" found in ATR directory.";

        // Find the directory sector and offset for reporting
        var allEntries = AtrParser.ReadDirectory(data);
        var entryIndex = -1;
        for (var i = 0; i < allEntries.Count; i++)
        {
            if (ReferenceEquals(allEntries[i], deletedEntry))
            {
                entryIndex = i;
                break;
            }
        }
        var dirSector = 361 + (entryIndex / 8);
        var dirOffset = (entryIndex % 8) * 16;

        // Recover the data by following the sector chain
        byte[] recovered;
        try
        {
            recovered = AtrParser.ExtractFile(data, geometry, deletedEntry);
        }
        catch (Exception ex)
        {
            return $"ERROR: Recovery failed: {ex.Message}";
        }

        // Write recovered data
        var parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        File.WriteAllBytes(output, recovered);

        var fullName = string.IsNullOrWhiteSpace(deletedEntry.Extension)
            ? deletedEntry.FileName
            : $"{deletedEntry.FileName}.{deletedEntry.Extension}";

        return string.Join('\n',
            $"Recovering deleted file '{fullName}'...",
            $"  Directory entry found at sector {dirSector}, offset {dirOffset}",
            $"  Status: Deleted (flag = $80)",
            $"  Original size: {deletedEntry.SectorCount * (geometry.SectorSize - 3):N0} bytes (allocated)",
            $"  Recovered size: {recovered.Length:N0} bytes",
            $"  Start sector: {deletedEntry.StartSector}",
            $"  Recovering sector chain starting at {deletedEntry.StartSector}...",
            $"  Recovery complete: {recovered.Length:N0} bytes written to {output}"
        );
    }

    private static string RecoverDeletedSparta(byte[] data, AtrGeometry geometry, string resolvedPath, string name, string output)
    {
        var spartaDir = AtrParser.ReadSpartaDirectory(data);
        var deletedEntry = spartaDir.FirstOrDefault(e => e.IsDeleted &&
            string.Equals(e.FileName.Trim().ToUpperInvariant(), name.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));

        if (deletedEntry is null)
            return $"ERROR: No deleted file named \"{name}\" found in SpartaDOS directory.";

        // Recover the data by following the sector chain
        byte[] recovered;
        try
        {
            recovered = ExtractSpartaFile(data, geometry, deletedEntry);
        }
        catch (Exception ex)
        {
            return $"ERROR: Recovery failed: {ex.Message}";
        }

        // Write recovered data
        var parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        File.WriteAllBytes(output, recovered);

        return string.Join('\n',
            $"Recovering deleted file '{deletedEntry.FileName}' (SpartaDOS)...",
            $"  Status: Deleted (flag = ${deletedEntry.Flags:X2})",
            $"  Recovered size: {recovered.Length:N0} bytes",
            $"  Start sector: {deletedEntry.StartSector}",
            $"  Recovery complete: {recovered.Length:N0} bytes written to {output}"
        );
    }

    // ─────────────────────────────────────────────────────────────────────
    // VTOC Display
    // ─────────────────────────────────────────────────────────────────────

    public static string ShowVtoc(string filePath)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var data = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(data))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(data);

            if (AtrParser.HasDosFilesystem(data))
            {
                return ShowVtocDos(data, geometry, resolvedPath);
            }

            if (AtrParser.HasSpartaDosFilesystem(data))
            {
                return ShowVtocSparta(data, geometry, resolvedPath);
            }

            if (AtrParser.HasMyDosFilesystem(data))
            {
                return ShowVtocMyDos(data, geometry, resolvedPath);
            }

            return "ERROR: No DOS 2.x or SpartaDOS filesystem detected.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string ShowVtocDos(byte[] data, AtrGeometry geometry, string resolvedPath)
    {
        var vtoc = AtrParser.ReadSector(data, geometry, 360);
        var bitmap = AtrParser.GetSectorBitmap(data, geometry);

        var freeCount = bitmap.Count(b => b);
        var usedCount = bitmap.Length - freeCount;

        var lines = new List<string>
        {
            $"VTOC analysis for {Path.GetFileName(resolvedPath)}:",
            $"  Filesystem: DOS 2.x",
            $"  Sector: 360",
            $"  Total sectors: {geometry.SectorCount}",
            $"  Free sectors: {freeCount}",
            $"  Used sectors: {usedCount}",
            string.Empty,
            "  Bitmap (first 32 bytes):"
        };

        // Display first 32 bytes of the bitmap in hex
        const int bitmapStart = 10; // VTOC bitmap starts at byte 10
        for (var row = 0; row < 4 && (bitmapStart + row * 16) < vtoc.Length; row++)
        {
            var hexParts = new List<string>();
            for (var col = 0; col < 16; col++)
            {
                var idx = bitmapStart + row * 16 + col;
                if (idx < vtoc.Length)
                {
                    hexParts.Add($"{vtoc[idx]:X2}");
                }
                else
                {
                    hexParts.Add("  ");
                }
            }
            lines.Add($"    {string.Join(" ", hexParts.Take(8))}  {string.Join(" ", hexParts.Skip(8))}");
        }

        // Find free sector ranges
        var freeRanges = new List<(int Start, int End)>();
        var i = 0;
        while (i < bitmap.Length)
        {
            if (bitmap[i])
            {
                var fs = i + 1;
                while (i < bitmap.Length && bitmap[i]) i++;
                freeRanges.Add((fs, i));
            }
            else
            {
                i++;
            }
        }

        lines.Add(string.Empty);
        lines.Add("  Free sector ranges:");
        if (freeRanges.Count == 0)
        {
            lines.Add("    (none)");
        }
        else
        {
            foreach (var range in freeRanges)
            {
                var count = range.End - range.Start + 1;
                lines.Add($"    {range.Start:D3}-{range.End:D3} ({count} sectors)");
            }
        }

        return string.Join('\n', lines);
    }

    private static string ShowVtocSparta(byte[] data, AtrGeometry geometry, string resolvedPath)
    {
        var vtoc = AtrParser.ReadSector(data, geometry, 4);
        var bitmap = AtrParser.GetSpartaBitmap(data, geometry);

        var freeCount = bitmap.Count(b => b);
        var usedCount = bitmap.Length - freeCount;

        var firstDirSector = ((vtoc[5] & 0x03) << 8) | vtoc[4];
        var totalFree = vtoc[0] | (vtoc[1] << 8);

        var lines = new List<string>
        {
            $"VTOC analysis for {Path.GetFileName(resolvedPath)}:",
            $"  Filesystem: SpartaDOS",
            $"  Sector: 4",
            $"  Total sectors: {geometry.SectorCount}",
            $"  Free sectors: {freeCount} (stored: {totalFree})",
            $"  Used sectors: {usedCount}",
            $"  First directory sector: {firstDirSector}",
            string.Empty,
            "  Bitmap (first 32 bytes):"
        };

        // SpartaDOS bitmap starts at byte 6
        const int bitmapStart = 6;
        for (var row = 0; row < 4 && (bitmapStart + row * 16) < vtoc.Length; row++)
        {
            var hexParts = new List<string>();
            for (var col = 0; col < 16; col++)
            {
                var idx = bitmapStart + row * 16 + col;
                if (idx < vtoc.Length)
                {
                    hexParts.Add($"{vtoc[idx]:X2}");
                }
                else
                {
                    hexParts.Add("  ");
                }
            }
            lines.Add($"    {string.Join(" ", hexParts.Take(8))}  {string.Join(" ", hexParts.Skip(8))}");
        }

        // Find free sector ranges
        var freeRanges = new List<(int Start, int End)>();
        var i = 0;
        while (i < bitmap.Length)
        {
            if (bitmap[i])
            {
                var fs = i + 1;
                while (i < bitmap.Length && bitmap[i]) i++;
                freeRanges.Add((fs, i));
            }
            else
            {
                i++;
            }
        }

        lines.Add(string.Empty);
        lines.Add("  Free sector ranges:");
        if (freeRanges.Count == 0)
        {
            lines.Add("    (none)");
        }
        else
        {
            foreach (var range in freeRanges)
            {
                var count = range.End - range.Start + 1;
                lines.Add($"    {range.Start:D3}-{range.End:D3} ({count} sectors)");
            }
        }

        return string.Join('\n', lines);
    }

    private static string ShowVtocMyDos(byte[] data, AtrGeometry geometry, string resolvedPath)
    {
        var vtoc = AtrParser.ReadSector(data, geometry, 360);
        var vtocChain = AtrParser.GetMyDosVtocChain(data, geometry);
        var freeCount = AtrParser.GetMyDosFreeSectorCount(data, geometry);
        var usedCount = geometry.SectorCount - freeCount;

        var totalSectors = vtoc[1] | (vtoc[2] << 8);
        var vtocFlag = vtoc[5];
        var nextVtoc = vtoc[6] | (vtoc[7] << 8);

        var lines = new List<string>
        {
            $"VTOC analysis for {Path.GetFileName(resolvedPath)}:",
            $"  Filesystem: MyDOS",
            $"  Primary VTOC sector: 360",
            $"  Total sectors: {totalSectors} (stored in VTOC)",
            $"  Free sectors: {freeCount}",
            $"  Used sectors: {usedCount}",
            $"  VTOC flag: ${vtocFlag:X2} (MyDOS extended)",
            $"  Next VTOC sector: {(nextVtoc == 0 ? "(none)" : nextVtoc.ToString())}",
            $"  VTOC chain: {string.Join(" → ", vtocChain)}",
            string.Empty,
            "  Primary VTOC header:",
            $"    Byte 0: DOS code = ${vtoc[0]:X2}",
            $"    Bytes 1-2: Total sectors = {totalSectors}",
            $"    Bytes 3-4: Free sectors = {freeCount}",
            $"    Byte 5: VTOC flag = ${vtocFlag:X2}",
            $"    Bytes 6-7: Next VTOC = {nextVtoc}",
            string.Empty
        };

        // Show bitmap for primary VTOC
        const int primaryBitmapOffset = 10;
        lines.Add("  Primary VTOC bitmap (first 32 bytes):");
        for (var row = 0; row < 4 && (primaryBitmapOffset + row * 16) < vtoc.Length; row++)
        {
            var hexParts = new List<string>();
            for (var col = 0; col < 16; col++)
            {
                var bmIdx = primaryBitmapOffset + row * 16 + col;
                if (bmIdx < vtoc.Length)
                    hexParts.Add($"{vtoc[bmIdx]:X2}");
                else
                    hexParts.Add("  ");
            }
            lines.Add($"    {string.Join(" ", hexParts.Take(8))}  {string.Join(" ", hexParts.Skip(8))}");
        }

        // Show secondary VTOC sectors
        if (vtocChain.Count > 1)
        {
            lines.Add(string.Empty);
            lines.Add("  Secondary VTOC sectors:");
            for (var i = 1; i < vtocChain.Count; i++)
            {
                var secVtoc = AtrParser.ReadSector(data, geometry, vtocChain[i]);
                var secNext = secVtoc[0] | (secVtoc[1] << 8);
                lines.Add($"    Sector {vtocChain[i]}: next VTOC = {(secNext == 0 ? "(none)" : secNext.ToString())}, bitmap at byte 2");
            }
        }

        // Find free sector ranges
        var bitmap = AtrParser.GetMyDosBitmap(data, geometry);
        var freeRanges = new List<(int Start, int End)>();
        var idx = 0;
        while (idx < bitmap.Length)
        {
            if (bitmap[idx])
            {
                var fs = idx + 1;
                while (idx < bitmap.Length && bitmap[idx]) idx++;
                freeRanges.Add((fs, idx));
            }
            else
            {
                idx++;
            }
        }

        lines.Add(string.Empty);
        lines.Add("  Free sector ranges:");
        if (freeRanges.Count == 0)
        {
            lines.Add("    (none)");
        }
        else
        {
            foreach (var range in freeRanges)
            {
                var count = range.End - range.Start + 1;
                lines.Add($"    {range.Start:D3}-{range.End:D3} ({count} sectors)");
            }
        }

        return string.Join('\n', lines);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static AtrDirectoryEntry? MatchEntry(IReadOnlyList<AtrDirectoryEntry> directory, string name)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return directory.FirstOrDefault(entry =>
        {
            if (entry.IsDeleted) return false;
            var fullName = string.IsNullOrWhiteSpace(entry.Extension)
                ? entry.FileName
                : $"{entry.FileName}.{entry.Extension}";
            return string.Equals(entry.FileName, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullName, normalized, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static SpartaDirEntry? MatchSpartaEntry(IReadOnlyList<SpartaDirEntry> entries, string requestedName)
    {
        var normalized = requestedName.Trim().ToUpperInvariant();
        return entries.FirstOrDefault(entry =>
            !entry.IsDeleted &&
            string.Equals(entry.FileName.Trim().ToUpperInvariant(), normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extract a file from a SpartaDOS filesystem by following the sector chain.
    /// SpartaDOS uses the same sector chain format as DOS 2.x (last 3 bytes of each sector).
    /// </summary>
    private static byte[] ExtractSpartaFile(byte[] data, AtrGeometry geometry, SpartaDirEntry entry)
    {
        var result = new List<byte>();
        var seenSectors = new HashSet<int>();
        var sector = entry.StartSector;

        while (sector != 0)
        {
            if (!seenSectors.Add(sector))
            {
                throw new InvalidDataException($"Sector chain loop detected at sector {sector}.");
            }

            var rawSector = AtrParser.ReadSector(data, geometry, sector);
            if (rawSector.Length < 4)
            {
                throw new InvalidDataException($"Sector {sector} is too small to contain chain metadata.");
            }

            var dataCapacity = rawSector.Length - 3;
            var usedBytes = Math.Min(rawSector[^1], dataCapacity);
            result.AddRange(rawSector.AsSpan(0, usedBytes).ToArray());

            var nextHi = rawSector[^3];
            var nextLo = rawSector[^2];
            sector = (nextHi << 8) | nextLo;
        }

        return result.ToArray();
    }
}