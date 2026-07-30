using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class AtrTools
{
    // ─────────────────────────────────────────────────────────────
    // Existing tools (v1, enhanced)
    // ─────────────────────────────────────────────────────────────

    public static string AtrInfo(string filePath)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(bytes))
            {
                return "ERROR: Not a valid ATR image.";
            }

            var geometry = AtrParser.ParseGeometry(bytes);
            var lines = new List<string>
            {
                $"ATR Disk Image: {resolvedPath}",
                $"Density  : {DescribeDensity(geometry)}",
                $"Sectors  : {geometry.SectorCount} x {geometry.SectorSize} bytes = {geometry.SectorCount * geometry.SectorSize:N0} bytes",
                string.Empty
            };

            if (AtrParser.HasDosFilesystem(bytes))
            {
                return FormatAtrInfoDos(bytes, geometry, resolvedPath, lines);
            }

            if (AtrParser.HasSpartaDosFilesystem(bytes))
            {
                return FormatAtrInfoSparta(bytes, geometry, resolvedPath);
            }

            if (AtrParser.HasMyDosFilesystem(bytes))
            {
                return FormatAtrInfoMyDos(bytes, geometry, resolvedPath, lines);
            }

            lines.Add("No DOS 2.x or SpartaDOS filesystem detected. This disk uses a custom/non-DOS layout.");
            lines.Add("Use load_atr_boot to inspect the boot loader or load_rom for raw binary access.");
            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string FormatAtrInfoDos(byte[] bytes, AtrGeometry geometry, string resolvedPath, List<string> lines)
    {
        var freeSectors = AtrParser.FreeSegmentCount(bytes, geometry);
        lines.Add($"Free     : {freeSectors} sectors");
        lines.Add(string.Empty);
        lines.Add("Directory:");
        lines.Add("  #  Filename     Ext  Sectors  Bytes   Start  Flags");

        var directory = AtrParser.ReadDirectory(bytes).Where(entry => !entry.IsDeleted).ToList();
        foreach (var entry in directory)
        {
            try
            {
                var extracted = AtrParser.ExtractFile(bytes, geometry, entry);
                var flags = new List<string>();
                if (entry.IsBinary) flags.Add("binary");
                if (entry.IsLocked) flags.Add("locked");
                var displayFlags = flags.Count == 0 ? "[]" : $"[{string.Join(',', flags)}]";
                lines.Add($"  {entry.Index,2}  {entry.FileName,-12} {entry.Extension,-3} {entry.SectorCount,7} {extracted.Length,6} {entry.StartSector,6}  {displayFlags}");
            }
            catch (Exception)
            {
                lines.Add($"  {entry.Index,2}  {entry.FileName,-12} {entry.Extension,-3} {"???",7} {"???",6} {entry.StartSector,6}  [unreadable]");
            }
        }

        return string.Join('\n', lines);
    }

    private static string FormatAtrInfoSparta(byte[] bytes, AtrGeometry geometry, string resolvedPath)
    {
        var lines = new List<string>
        {
            $"ATR Disk Image: {resolvedPath}",
            $"Density  : {DescribeDensity(geometry)}",
            $"Sectors  : {geometry.SectorCount} x {geometry.SectorSize} bytes = {geometry.SectorCount * geometry.SectorSize:N0} bytes",
            string.Empty,
            "Filesystem: SpartaDOS"
        };

        var bitmap = AtrParser.GetSpartaBitmap(bytes, geometry);
        var freeCount = bitmap.Count(b => b);
        lines.Add($"Free     : {freeCount} sectors");
        lines.Add(string.Empty);
        lines.Add("Directory:");
        lines.Add("  #  Filename                   Start  Flags");

        var directory = AtrParser.ReadSpartaDirectory(bytes).Where(e => !e.IsDeleted).ToList();
        for (var i = 0; i < directory.Count; i++)
        {
            var entry = directory[i];
            var flags = new List<string>();
            if ((entry.Flags & 0x01) != 0) flags.Add("binary");
            if ((entry.Flags & 0x20) != 0) flags.Add("locked");
            var displayFlags = flags.Count == 0 ? "[]" : $"[{string.Join(',', flags)}]";
            lines.Add($"  {i,2}  {entry.FileName,-24} {entry.StartSector,6}  {displayFlags}");
        }

        return string.Join('\n', lines);
    }

    private static string FormatAtrInfoMyDos(byte[] bytes, AtrGeometry geometry, string resolvedPath, List<string> lines)
    {
        lines.Add("Filesystem: MyDOS");
        var freeSectors = AtrParser.GetMyDosFreeSectorCount(bytes, geometry);
        lines.Add($"Free     : {freeSectors} sectors");
        lines.Add(string.Empty);
        lines.Add("Directory:");
        lines.Add("  #  Filename     Ext  Sectors  Bytes   Start  Flags");

        var directory = AtrParser.ReadMyDosDirectory(bytes).Where(entry => !entry.IsDeleted).ToList();
        foreach (var entry in directory)
        {
            try
            {
                var extracted = AtrParser.ExtractFile(bytes, geometry,
                    new AtrDirectoryEntry(entry.Index, entry.FileName, entry.Extension,
                        entry.StartSector, entry.SectorCount, entry.IsDeleted, entry.IsLocked, entry.IsBinary));
                var flags = new List<string>();
                if (entry.IsBinary) flags.Add("binary");
                if (entry.IsLocked) flags.Add("locked");
                if (entry.IsSubdirectory) flags.Add("subdir");
                var displayFlags = flags.Count == 0 ? "[]" : $"[{string.Join(',', flags)}]";
                lines.Add($"  {entry.Index,2}  {entry.FileName,-12} {entry.Extension,-3} {entry.SectorCount,7} {extracted.Length,6} {entry.StartSector,6}  {displayFlags}");
            }
            catch (Exception)
            {
                lines.Add($"  {entry.Index,2}  {entry.FileName,-12} {entry.Extension,-3} {"???",7} {"???",6} {entry.StartSector,6}  [unreadable]");
            }
        }

        return string.Join('\n', lines);
    }

    public static string LoadAtrFile(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SessionPersistence persistence,
        string filePath,
        string fileName,
        ushort? loadAddress = null)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(bytes))
            {
                return "ERROR: Not a valid ATR image.";
            }

            var geometry = AtrParser.ParseGeometry(bytes);

            // Try DOS 2.x first, then SpartaDOS
            AtrDirectoryEntry? dosMatch = null;
            SpartaDirEntry? spartaMatch = null;
            bool isSparta = false;

            if (AtrParser.HasDosFilesystem(bytes))
            {
                var directory = AtrParser.ReadDirectory(bytes);
                dosMatch = MatchEntry(directory, fileName);
            }

            if (dosMatch is null && AtrParser.HasSpartaDosFilesystem(bytes))
            {
                var spartaDir = AtrParser.ReadSpartaDirectory(bytes);
                spartaMatch = MatchSpartaEntry(spartaDir, fileName);
                isSparta = true;
            }

            // Try MyDOS as a final fallback (uses same directory format as DOS 2.x)
            if (dosMatch is null && spartaMatch is null && AtrParser.HasMyDosFilesystem(bytes))
            {
                var myDosDir = AtrParser.ReadMyDosDirectory(bytes);
                // Convert MyDosDirectoryEntry to AtrDirectoryEntry for matching
                var converted = myDosDir.Select(e => new AtrDirectoryEntry(
                    e.Index, e.FileName, e.Extension, e.StartSector, e.SectorCount,
                    e.IsDeleted, e.IsLocked, e.IsBinary)).ToList();
                dosMatch = MatchEntry(converted, fileName);
            }

            if (dosMatch is null && spartaMatch is null)
            {
                return $"ERROR: File \"{fileName}\" not found in ATR directory.";
            }

            if (isSparta)
            {
                if (spartaMatch!.IsDeleted)
                    return $"ERROR: File \"{fileName}\" exists but is deleted.";

                var extracted = ExtractSpartaFile(bytes, geometry, spartaMatch);
                var syntheticPath = BuildSyntheticPath(resolvedPath, spartaMatch.FileName);
                session.Load(syntheticPath, extracted);
                if (loadAddress.HasValue)
                    session.BaseAddress = loadAddress.Value;
                session.SourceAtrPath = resolvedPath;
                FileTools.PopulateMetadata(session, extracted);
                var sidecarLoaded = persistence.TryLoad(syntheticPath);
                return $"Extracted {spartaMatch.FileName} from ATR (SpartaDOS).\n" + FileTools.BuildRomInfo(session, symbols, zeroPageMap, sidecarLoaded);
            }
            else
            {
                if (dosMatch!.IsDeleted)
                    return $"ERROR: File \"{fileName}\" exists but is deleted.";

                var extracted = AtrParser.ExtractFile(bytes, geometry, dosMatch);
                var syntheticPath = BuildSyntheticPath(resolvedPath, dosMatch);
                session.Load(syntheticPath, extracted);
                if (loadAddress.HasValue)
                    session.BaseAddress = loadAddress.Value;
                session.SourceAtrPath = resolvedPath;
                FileTools.PopulateMetadata(session, extracted);
                var sidecarLoaded = persistence.TryLoad(syntheticPath);
                return $"Extracted {dosMatch.FileName}{(string.IsNullOrWhiteSpace(dosMatch.Extension) ? string.Empty : "." + dosMatch.Extension)} from ATR.\n" + FileTools.BuildRomInfo(session, symbols, zeroPageMap, sidecarLoaded);
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string LoadAtrBoot(
        RomSession session,
        SessionPersistence persistence,
        string filePath)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(bytes))
            {
                return "ERROR: Not a valid ATR image.";
            }

            var boot = AtrParser.ExtractBootSectors(bytes);
            var syntheticPath = resolvedPath + "/BOOT";
            session.Load(syntheticPath, boot);
            session.BaseAddress = 0x0700;
            session.SourceAtrPath = resolvedPath;

            // Decode the 6-byte boot header
            session.BootHeader = new BootHeader(
                Flag: boot[0],
                SectorCount: boot[1],
                LoadAddress: (ushort)(boot[2] | (boot[3] << 8)),
                InitAddress: (ushort)(boot[4] | (boot[5] << 8))
            );

            persistence.TryLoad(syntheticPath);
            return $"Loaded ATR boot sectors: {boot.Length} bytes at $0700\n" + HexDumpTool.GenerateHexDump(boot, 0, boot.Length, 0x0700);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // New tools (v2)
    // ─────────────────────────────────────────────────────────────

    public static string AtrHeader(
        string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.GetFullPath(filePath));
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            var geo = AtrParser.ParseGeometry(bytes);
            var paragraphsLow = bytes[2] | (bytes[3] << 8);
            var paragraphsHigh = bytes[6] | (bytes[7] << 8);
            var totalParagraphs = ((uint)paragraphsHigh << 16) | (uint)paragraphsLow;
            var imageBytes = (int)(totalParagraphs * 16u);
            var writeProtect = bytes[8] != 0;

            return string.Join('\n',
                $"ATR Header: {Path.GetFullPath(filePath)}",
                $"  Magic:         $0296",
                $"  Image size:    {imageBytes} bytes ({totalParagraphs} paragraphs)",
                $"  Sector size:   {geo.SectorSize} bytes",
                $"  Sector count:  {geo.SectorCount}",
                $"  Density:       {DescribeDensity(geo)}",
                $"  Write protect: {(writeProtect ? "Yes" : "No")}"
            );
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string ListAtrDirectory(
        string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.GetFullPath(filePath));
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            if (AtrParser.HasDosFilesystem(bytes))
            {
                return FormatListDirectoryDos(bytes, filePath);
            }

            if (AtrParser.HasSpartaDosFilesystem(bytes))
            {
                return FormatListDirectorySparta(bytes, filePath);
            }

            if (AtrParser.HasMyDosFilesystem(bytes))
            {
                return FormatListDirectoryMyDos(bytes, filePath);
            }

            return "ERROR: No DOS 2.x or SpartaDOS filesystem detected on this disk image. " +
                   "This disk may use a custom/non-DOS layout. " +
                   "Use load_rom to load it as a raw binary, or load_atr_boot to inspect the boot loader.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string FormatListDirectoryDos(byte[] bytes, string filePath)
    {
        var geo = AtrParser.ParseGeometry(bytes);
        var allEntries = AtrParser.ReadDirectory(bytes);
        var active = allEntries.Where(e => !e.IsDeleted).ToList();
        var deleted = allEntries.Where(e => e.IsDeleted).ToList();

        var lines = new List<string>
        {
            $"ATR Directory: {Path.GetFullPath(filePath)}",
            "  #  Filename     Ext  Sectors  Start   Flags"
        };

        foreach (var entry in active)
        {
            var flags = new List<string>();
            if (entry.IsBinary) flags.Add("binary");
            if (entry.IsLocked) flags.Add("locked");
            lines.Add($"  {entry.Index,2}  {entry.FileName,-12} {entry.Extension,-3} {entry.SectorCount,7} {entry.StartSector,6}  [{(flags.Count == 0 ? "" : string.Join(',', flags))}]");
        }

        var free = AtrParser.FreeSegmentCount(bytes, geo);
        var used = active.Sum(e => e.SectorCount);

        lines.Add("");
        lines.Add($"{active.Count} files{(deleted.Count > 0 ? $" ({deleted.Count} deleted hidden)" : "")}, {used} sectors used, {free} sectors free");

        return string.Join('\n', lines);
    }

    private static string FormatListDirectorySparta(byte[] bytes, string filePath)
    {
        var geo = AtrParser.ParseGeometry(bytes);
        var allEntries = AtrParser.ReadSpartaDirectory(bytes);
        var active = allEntries.Where(e => !e.IsDeleted).ToList();
        var deleted = allEntries.Where(e => e.IsDeleted).ToList();

        var bitmap = AtrParser.GetSpartaBitmap(bytes, geo);
        var freeCount = bitmap.Count(b => b);

        var lines = new List<string>
        {
            $"ATR Directory: {Path.GetFullPath(filePath)}",
            "Filesystem: SpartaDOS",
            "  #  Filename                   Start  Flags"
        };

        for (var i = 0; i < active.Count; i++)
        {
            var entry = active[i];
            var flags = new List<string>();
            if ((entry.Flags & 0x01) != 0) flags.Add("binary");
            if ((entry.Flags & 0x20) != 0) flags.Add("locked");
            lines.Add($"  {i,2}  {entry.FileName,-24} {entry.StartSector,6}  [{(flags.Count == 0 ? "" : string.Join(',', flags))}]");
        }

        lines.Add("");
        lines.Add($"{active.Count} files{(deleted.Count > 0 ? $" ({deleted.Count} deleted hidden)" : "")}, {freeCount} sectors free");

        return string.Join('\n', lines);
    }

    private static string FormatListDirectoryMyDos(byte[] bytes, string filePath)
    {
        var geo = AtrParser.ParseGeometry(bytes);
        var allEntries = AtrParser.ReadMyDosDirectory(bytes);
        var active = allEntries.Where(e => !e.IsDeleted).ToList();
        var deleted = allEntries.Where(e => e.IsDeleted).ToList();

        var lines = new List<string>
        {
            $"ATR Directory: {Path.GetFullPath(filePath)}",
            "Filesystem: MyDOS",
            "  #  Filename     Ext  Sectors  Start   Flags"
        };

        foreach (var entry in active)
        {
            var flags = new List<string>();
            if (entry.IsBinary) flags.Add("binary");
            if (entry.IsLocked) flags.Add("locked");
            if (entry.IsSubdirectory) flags.Add("subdir");
            lines.Add($"  {entry.Index,2}  {entry.FileName,-12} {entry.Extension,-3} {entry.SectorCount,7} {entry.StartSector,6}  [{(flags.Count == 0 ? "" : string.Join(',', flags))}]");
        }

        var free = AtrParser.GetMyDosFreeSectorCount(bytes, geo);
        var used = active.Sum(e => e.SectorCount);

        lines.Add("");
        lines.Add($"{active.Count} files{(deleted.Count > 0 ? $" ({deleted.Count} deleted hidden)" : "")}, {used} sectors used, {free} sectors free");

        return string.Join('\n', lines);
    }

    public static string AnalyzeBootSector(
        string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.GetFullPath(filePath));
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            var boot = AtrParser.ExtractBootSectors(bytes);
            var flag = boot[0];
            var sectorCount = boot[1];
            var loadAddr = (ushort)(boot[2] | (boot[3] << 8));
            var initAddr = (ushort)(boot[4] | (boot[5] << 8));

            var isDosBoot = initAddr is >= 0x0700 and <= 0x07FF;
            var bootType = isDosBoot ? "DOS boot" : "Custom loader";

            return string.Join('\n',
                $"Boot Sector Analysis: {Path.GetFullPath(filePath)}",
                $"  Boot flag:       ${flag:X2}  ({(flag == 0 ? "continue loading" : "stop / run")})",
                $"  Sectors to load: {sectorCount}",
                $"  Load address:    ${loadAddr:X4}",
                $"  Init address:    ${initAddr:X4}",
                $"  Entry point:     $0706  (first instruction after boot header)",
                $"  Header bytes:    {boot[0]:X2} {boot[1]:X2} {boot[2]:X2} {boot[3]:X2} {boot[4]:X2} {boot[5]:X2}",
                $"  DOS boot:        {(isDosBoot ? "Yes" : "No")}  ({bootType})"
            );
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string SectorDump(
        string filePath,
        string sector,
        int count = 1)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.GetFullPath(filePath));
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            var geo = AtrParser.ParseGeometry(bytes);
            var sectorNum = AddressParser.ParseAddress(sector);
            if (sectorNum < 1 || sectorNum > geo.SectorCount)
                return $"ERROR: Sector {sectorNum} is outside the image (1-{geo.SectorCount}).";

            count = Math.Max(1, Math.Min(count, geo.SectorCount - sectorNum + 1));

            // Build a contiguous byte buffer from the requested sectors
            using var ms = new MemoryStream();
            for (int i = 0; i < count; i++)
            {
                var sec = AtrParser.ReadSector(bytes, geo, sectorNum + i);
                ms.Write(sec, 0, sec.Length);
            }

            var combined = ms.ToArray();
            var fileOffset = AtrParser.SectorFileOffset(geo, sectorNum);

            var header = count == 1
                ? $"Sector {sectorNum} (file offset ${fileOffset:X}), {combined.Length} bytes:"
                : $"Sectors {sectorNum}-{sectorNum + count - 1} (file offset ${fileOffset:X}), {combined.Length} bytes:";

            var dump = HexDumpTool.GenerateHexDumpWithCustomLabels(combined, fileOffset, combined.Length,
                row => $"{(sectorNum + (row - fileOffset) / geo.SectorSize)}:${(row - fileOffset) % geo.SectorSize:X4}");

            return header + "\n" + dump;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string SearchBootSector(
        string[] filePaths,
        string? pattern = null,
        string compareMode = "pattern")
    {
        try
        {
            var isPatternMode = string.Equals(compareMode, "pattern", StringComparison.OrdinalIgnoreCase);
            var isDiffMode = string.Equals(compareMode, "diff", StringComparison.OrdinalIgnoreCase);

            if (!isPatternMode && !isDiffMode)
                return $"ERROR: Invalid compareMode '{compareMode}'. Use 'pattern' or 'diff'.";

            // Validate and extract boot sectors from each path
            var results = new List<(string Path, byte[] Boot, byte Flag, int SectorCount, ushort LoadAddr)>();
            foreach (var rawPath in filePaths)
            {
                var resolvedPath = Path.GetFullPath(rawPath);
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(resolvedPath);
                }
                catch (Exception)
                {
                    results.Add((resolvedPath, Array.Empty<byte>(), 0, 0, 0));
                    continue;
                }

                if (!AtrParser.IsAtr(bytes))
                {
                    results.Add((resolvedPath, Array.Empty<byte>(), 0, 0, 0));
                    continue;
                }

                var boot = AtrParser.ExtractBootSectors(bytes);
                results.Add((resolvedPath, boot, boot[0], boot[1], (ushort)(boot[2] | (boot[3] << 8))));
            }

            if (isPatternMode)
            {
                return SearchBootSectorPattern(results, pattern);
            }
            else
            {
                return SearchBootSectorDiff(results);
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string SearchBootSectorPattern(List<(string Path, byte[] Boot, byte Flag, int SectorCount, ushort LoadAddr)> results, string? pattern)
    {
        var patternLabel = string.IsNullOrWhiteSpace(pattern) ? "(all)" : $"\"{pattern}\"";
        var lines = new List<string>
        {
            $"Boot sector search: pattern {patternLabel}"
        };

        foreach (var (path, boot, flag, sectorCount, loadAddr) in results)
        {
            if (boot.Length == 0)
            {
                lines.Add($"  {path}  -  Not a valid ATR image");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pattern))
            {
                lines.Add($"  {path}  -  Boot flag ${flag:X2}, loads {sectorCount} sectors to ${loadAddr:X4}");
                continue;
            }

            // Parse the pattern into bytes with wildcards
            var patternBytes = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchOffset = FindPattern(boot, patternBytes);
            if (matchOffset >= 0)
            {
                lines.Add($"  {path}  -  Match at sector offset ${matchOffset:X4} (boot flag ${flag:X2}, loads {sectorCount} sectors to ${loadAddr:X4})");
            }
            else
            {
                lines.Add($"  {path}  -  No match");
            }
        }

        return string.Join('\n', lines);
    }

    private static string SearchBootSectorDiff(List<(string Path, byte[] Boot, byte Flag, int SectorCount, ushort LoadAddr)> results)
    {
        var valid = results.Where(r => r.Boot.Length > 0).ToList();
        var lines = new List<string>
        {
            "Boot sector comparison:"
        };

        for (var i = 0; i < valid.Count; i++)
        {
            for (var j = i + 1; j < valid.Count; j++)
            {
                var a = valid[i];
                var b = valid[j];
                var maxLen = Math.Max(a.Boot.Length, b.Boot.Length);
                var identical = 0;
                var minLen = Math.Min(a.Boot.Length, b.Boot.Length);
                for (var k = 0; k < minLen; k++)
                {
                    if (a.Boot[k] == b.Boot[k]) identical++;
                }

                var pct = (maxLen > 0) ? (identical * 100 / maxLen) : 100;
                var nameA = Path.GetFileName(a.Path);
                var nameB = Path.GetFileName(b.Path);
                lines.Add($"  {nameA} vs {nameB}  -  {identical} / {maxLen} bytes identical ({pct}%)");
            }
        }

        if (valid.Count <= 1)
        {
            lines.Add("  (need at least 2 valid ATR images for comparison)");
        }

        return string.Join('\n', lines);
    }

    /// <summary>Simple byte pattern search (like find_pattern but on a byte array with wildcards).</summary>
    private static int FindPattern(byte[] data, string[] patternBytes)
    {
        for (var offset = 0; offset <= data.Length - patternBytes.Length; offset++)
        {
            var match = true;
            for (var i = 0; i < patternBytes.Length; i++)
            {
                if (patternBytes[i] == "??") continue;
                if (!byte.TryParse(patternBytes[i], System.Globalization.NumberStyles.HexNumber, null, out var expected))
                {
                    match = false;
                    break;
                }

                if (data[offset + i] != expected)
                {
                    match = false;
                    break;
                }
            }

            if (match) return offset;
        }

        return -1;
    }

    // ─────────────────────────────────────────────────────────────
    // ATR File Operations (v3)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Perform comprehensive analysis of the disk layout, detecting the filesystem type,
    /// identifying boot method, and mapping all sectors.
    /// </summary>
    public static string AnalyzeLayout(string filePath)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(bytes);
            var lines = new List<string>
            {
                $"Analyzing: {resolvedPath}",
                $"Sectors   : {geometry.SectorCount} \u00d7 {geometry.SectorSize} bytes ({geometry.Density})",
                $"Data size : {geometry.SectorCount * geometry.SectorSize:N0} bytes"
            };

            // Detect filesystem type
            var hasDos = AtrParser.HasDosFilesystem(bytes);
            var hasSparta = AtrParser.HasSpartaDosFilesystem(bytes);
            string fsType;
            if (hasDos)
                fsType = "DOS 2.x";
            else if (hasSparta)
                fsType = "SpartaDOS";
            else
                fsType = "Custom / Non-DOS";

            lines.Add($"Filesystem: {fsType}");

            // Boot analysis
            var boot = AtrParser.ExtractBootSectors(bytes);
            var bootFlag = boot[0];
            var bootSectorCount = boot[1];
            var bootLoadAddr = (ushort)(boot[2] | (boot[3] << 8));
            var bootInitAddr = (ushort)(boot[4] | (boot[5] << 8));
            var isCustomBoot = bootInitAddr is < 0x0700 or > 0x07FF;
            var bootType = isCustomBoot ? "Custom loader" : "DOS boot";

            lines.Add(string.Empty);
            lines.Add("Boot sectors (3 sectors, 384 bytes):");
            lines.Add($"  Boot flag  : ${bootFlag:X2} ({(bootFlag == 0 ? "continue loading" : "stop / run")})");
            lines.Add($"  Load addr  : ${bootLoadAddr:X4}");
            lines.Add($"  Init addr  : ${bootInitAddr:X4}");
            lines.Add($"  Boot type  : {bootType}");

            if (hasDos)
            {
                // VTOC
                lines.Add(string.Empty);
                lines.Add("VTOC: Sector 360");

                // Directory
                var dirStart = 361;
                var dirEnd = Math.Min(368, geometry.SectorCount);
                lines.Add($"Directory: Sectors {dirStart}-{dirEnd} ({dirEnd - dirStart + 1} directory sectors)");

                // Files
                var directory = AtrParser.ReadDirectory(bytes).Where(e => !e.IsDeleted).ToList();
                lines.Add(string.Empty);
                lines.Add($"Files ({directory.Count}):");
                foreach (var entry in directory)
                {
                    try
                    {
                        var chain = AtrParser.GetSectorChain(bytes, geometry, entry.StartSector);
                        var extractedSize = chain.Count * (geometry.SectorSize - 3);
                        lines.Add($"  {entry.FileName,-8}.{entry.Extension,-3} {chain.Count,4} sectors, starts sector {entry.StartSector,3}, ~{extractedSize,5} bytes");
                    }
                    catch
                    {
                        lines.Add($"  {entry.FileName,-8}.{entry.Extension,-3} {"???",4} sectors, starts sector {entry.StartSector,3} [corrupt chain]");
                    }
                }

                // Free sectors
                var freeSectors = AtrParser.FreeSegmentCount(bytes, geometry);
                var usedSectors = directory.Sum(e => e.SectorCount) + 8; // 8 dir sectors + VTOC + boot
                lines.Add(string.Empty);
                lines.Add($"Free sectors: {freeSectors} free, {usedSectors} used");
            }
            else if (hasSparta)
            {
                // VTOC
                lines.Add(string.Empty);
                lines.Add("VTOC: Sector 4 (SpartaDOS volume bitmap)");

                // Directory
                var dirSectors = AtrParser.GetSpartaDirectorySectors(bytes, geometry);
                lines.Add($"Directory: Sectors {string.Join(", ", dirSectors)} ({dirSectors.Count} directory sectors)");

                // Files
                var spartaDir = AtrParser.ReadSpartaDirectory(bytes).Where(e => !e.IsDeleted).ToList();
                lines.Add(string.Empty);
                lines.Add($"Files ({spartaDir.Count}):");
                foreach (var entry in spartaDir)
                {
                    try
                    {
                        var chain = AtrParser.GetSectorChain(bytes, geometry, entry.StartSector);
                        var extractedSize = chain.Count * (geometry.SectorSize - 3);
                        lines.Add($"  {entry.FileName,-16} {chain.Count,4} sectors, starts sector {entry.StartSector,3}, ~{extractedSize,5} bytes");
                    }
                    catch
                    {
                        lines.Add($"  {entry.FileName,-16} {"???",4} sectors, starts sector {entry.StartSector,3} [corrupt chain]");
                    }
                }

                // Free sectors
                var bitmap = AtrParser.GetSpartaBitmap(bytes, geometry);
                var freeCount = bitmap.Count(b => b);
                lines.Add(string.Empty);
                lines.Add($"Free sectors: {freeCount} free");
            }
            else
            {
                lines.Add(string.Empty);
                lines.Add("No DOS 2.x or SpartaDOS filesystem detected.");
                lines.Add("This disk uses a custom/non-DOS layout.");
            }

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Disassemble a range of sectors from the disk as code, specifying the load address.
    /// </summary>
    public static string DisassembleSector(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string filePath,
        int startSector,
        int sectorCount,
        ushort? loadAddress = null,
        string format = "listing",
        bool analyze = false)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(bytes);
            if (startSector < 1 || startSector > geometry.SectorCount)
                return $"ERROR: Start sector {startSector} is outside the image (1-{geometry.SectorCount}).";

            sectorCount = Math.Max(1, Math.Min(sectorCount, geometry.SectorCount - startSector + 1));

            // Read the sectors into a contiguous buffer
            using var ms = new MemoryStream();
            for (var i = 0; i < sectorCount; i++)
            {
                var sec = AtrParser.ReadSector(bytes, geometry, startSector + i);
                ms.Write(sec, 0, sec.Length);
            }

            var combined = ms.ToArray();
            var address = loadAddress ?? 0x0700;

            // Load into session temporarily
            var syntheticPath = resolvedPath + $"/SECTORS-{startSector}-{startSector + sectorCount - 1}";
            session.Load(syntheticPath, combined);
            session.BaseAddress = address;

            var offsetStr = $"0";
            var numBytes = combined.Length;

            return DisassemblerTool.Disassemble(session, symbols, zeroPageMap, offsetStr, numBytes, $"${address:X4}", format, analyze);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Dump the raw binary contents of the ATR (excluding the 16-byte ATR header) to stdout or a file.
    /// </summary>
    public static string DumpAtrData(
        string filePath,
        int? startSector = null,
        int? endSector = null,
        string? outputFile = null)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(bytes);

            // Determine byte range
            int startOffset, endOffset;
            if (startSector.HasValue || endSector.HasValue)
            {
                var s = startSector ?? 1;
                var e = endSector ?? geometry.SectorCount;
                if (s < 1 || s > geometry.SectorCount)
                    return $"ERROR: Start sector {s} is outside the image (1-{geometry.SectorCount}).";
                if (e < 1 || e > geometry.SectorCount)
                    return $"ERROR: End sector {e} is outside the image (1-{geometry.SectorCount}).";
                if (s > e)
                    return $"ERROR: Start sector ({s}) is greater than end sector ({e}).";

                startOffset = AtrParser.SectorFileOffset(geometry, s);
                var lastSectorOffset = AtrParser.SectorFileOffset(geometry, e);
                var lastSectorLen = e <= 3 && geometry.SectorSize == 256 ? 128 : geometry.SectorSize;
                endOffset = lastSectorOffset + lastSectorLen;
            }
            else
            {
                startOffset = 16; // Skip ATR header
                endOffset = bytes.Length;
            }

            var dataLength = endOffset - startOffset;
            var dumpData = new byte[dataLength];
            Buffer.BlockCopy(bytes, startOffset, dumpData, 0, dataLength);

            // If output file specified, write to it
            if (!string.IsNullOrWhiteSpace(outputFile))
            {
                var parent = Path.GetDirectoryName(outputFile);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);
                File.WriteAllBytes(outputFile, dumpData);
                return $"Dumped {dataLength} bytes (sectors {startSector ?? 1}-{endSector ?? geometry.SectorCount}) \u2192 {outputFile}";
            }

            // Otherwise, return sector-aware hex dump
            var sectorLabel = startSector.HasValue
                ? $"Sectors {startSector}-{endSector}"
                : $"All data (excluding 16-byte header)";
            var header = $"ATR data dump: {Path.GetFileName(resolvedPath)} ({sectorLabel}, {dataLength} bytes)";
            var dump = HexDumpTool.GenerateHexDump(dumpData, 0, dumpData.Length, null, geometry);
            return header + "\n" + dump;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Write a 3-sector (384-byte) boot loader to an ATR image, properly handling
    /// the 6-byte boot header and sector alignment.
    /// </summary>
    public static string WriteBootSectors(
        string filePath,
        string bootFilePath,
        byte? bootFlag = null,
        ushort? loadAddress = null,
        ushort? initAddress = null)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var data = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(data))
                return "ERROR: Not a valid ATR image.";

            var bootFileBytes = File.ReadAllBytes(Path.GetFullPath(bootFilePath));
            if (bootFileBytes.Length > 384)
                return $"ERROR: Boot file ({bootFileBytes.Length} bytes) exceeds maximum boot sector size (384 bytes).";

            var geometry = AtrParser.ParseGeometry(data);
            var modifiedPath = AtrWriteTools.GetModifiedPath(resolvedPath);
            var modifiedData = (byte[])data.Clone();

            // Build the 3-sector boot image (384 bytes, padded with zeros)
            var bootImage = new byte[384];
            Array.Copy(bootFileBytes, 0, bootImage, 0, bootFileBytes.Length);

            // Write the 6-byte boot header
            bootImage[0] = bootFlag ?? 0x00;               // Boot flag
            bootImage[1] = (byte)(bootFileBytes.Length <= 128 ? 1 :
                bootFileBytes.Length <= 256 ? 2 : 3);      // Sector count
            bootImage[2] = (byte)((loadAddress ?? 0x0700) & 0xFF);        // Load address low
            bootImage[3] = (byte)(((loadAddress ?? 0x0700) >> 8) & 0xFF); // Load address high
            bootImage[4] = (byte)((initAddress ?? 0x0700) & 0xFF);        // Init address low
            bootImage[5] = (byte)(((initAddress ?? 0x0700) >> 8) & 0xFF); // Init address high

            // Write to sectors 1-3
            for (var sector = 1; sector <= 3; sector++)
            {
                var sectorLen = sector <= 3 && geometry.SectorSize == 256 ? 128 : geometry.SectorSize;
                var sectorData = new byte[sectorLen];
                var srcOffset = (sector - 1) * 128;
                var copyLen = Math.Min(128, bootImage.Length - srcOffset);
                Array.Copy(bootImage, srcOffset, sectorData, 0, copyLen);

                var offset = AtrParser.SectorFileOffset(geometry, sector);
                Array.Copy(sectorData, 0, modifiedData, offset, sectorData.Length);
            }

            File.WriteAllBytes(modifiedPath, modifiedData);

            var lines = new List<string>
            {
                $"Wrote boot sectors ({bootFileBytes.Length} bytes) \u2192 {modifiedPath}",
                $"  Boot flag:       ${(bootFlag ?? 0x00):X2}",
                $"  Load address:    ${(loadAddress ?? 0x0700):X4}",
                $"  Init address:    ${(initAddress ?? 0x0700):X4}",
                $"  Sector count:    {bootImage[1]}"
            };

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Show detailed information about one or more sectors, including whether
    /// they're part of the boot, VTOC, directory, a specific file, or free.
    /// </summary>
    public static string SectorInfo(
        string filePath,
        string sectorRange)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(bytes))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(bytes);

            // Parse sector range (e.g., "1", "1-5", "1,3,5", or hex)
            var sectors = ParseSectorRange(sectorRange, geometry.SectorCount);
            if (sectors.Count == 0)
                return "ERROR: No valid sectors specified. Use decimal or hex numbers, e.g. '1', '1-5', '1,3,5'.";

            // Build sector info using similar logic to AtrForensicTools
            var hasDos = AtrParser.HasDosFilesystem(bytes);
            var hasSparta = !hasDos && AtrParser.HasSpartaDosFilesystem(bytes);
            var directory = hasDos ? AtrParser.ReadDirectory(bytes) : null;
            List<SpartaDirEntry>? spartaDirectory = null;
            HashSet<int>? spartaDirSectors = null;

            if (hasSparta)
            {
                spartaDirectory = AtrParser.ReadSpartaDirectory(bytes);
                spartaDirSectors = new HashSet<int>(AtrParser.GetSpartaDirectorySectors(bytes, geometry));
            }

            var lines = new List<string>
            {
                $"Sector info for {Path.GetFileName(resolvedPath)}:"
            };

            foreach (var sector in sectors)
            {
                var sectorData = AtrParser.ReadSector(bytes, geometry, sector);
                var fileOffset = AtrParser.SectorFileOffset(geometry, sector);
                var description = DescribeSector(sector, hasDos, hasSparta, directory, spartaDirectory, spartaDirSectors, bytes, geometry);
                lines.Add($"  Sector {sector} (file offset ${fileOffset:X}): {description}");
            }

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Compare ATR images filesystem-aware, showing differences at the file level
    /// rather than just raw byte-level.
    /// </summary>
    public static string DiffAtrImages(
        string filePath1,
        string filePath2)
    {
        try
        {
            var resolved1 = Path.GetFullPath(filePath1);
            var resolved2 = Path.GetFullPath(filePath2);

            var bytes1 = File.ReadAllBytes(resolved1);
            var bytes2 = File.ReadAllBytes(resolved2);

            if (!AtrParser.IsAtr(bytes1))
                return $"ERROR: Not a valid ATR image: {filePath1}";
            if (!AtrParser.IsAtr(bytes2))
                return $"ERROR: Not a valid ATR image: {filePath2}";

            var geo1 = AtrParser.ParseGeometry(bytes1);
            var geo2 = AtrParser.ParseGeometry(bytes2);

            var lines = new List<string>
            {
                $"Comparing ATR images:",
                $"  {Path.GetFileName(resolved1)} ({geo1.SectorCount} \u00d7 {geo1.SectorSize})",
                $"  {Path.GetFileName(resolved2)} ({geo2.SectorCount} \u00d7 {geo2.SectorSize})",
                string.Empty
            };

            // Compare headers
            var headerMatch = bytes1.AsSpan(0, 16).SequenceEqual(bytes2.AsSpan(0, 16));
            lines.Add($"  ATR headers: {(headerMatch ? "match" : "differ")}");

            // Compare boot sectors
            var boot1 = AtrParser.ExtractBootSectors(bytes1);
            var boot2 = AtrParser.ExtractBootSectors(bytes2);
            var bootMatch = boot1.AsSpan().SequenceEqual(boot2.AsSpan());
            lines.Add($"  Boot sectors: {(bootMatch ? "match" : "differ")}");

            // Detect filesystems
            var hasDos1 = AtrParser.HasDosFilesystem(bytes1);
            var hasDos2 = AtrParser.HasDosFilesystem(bytes2);
            var hasSparta1 = !hasDos1 && AtrParser.HasSpartaDosFilesystem(bytes1);
            var hasSparta2 = !hasDos2 && AtrParser.HasSpartaDosFilesystem(bytes2);

            var fs1 = hasDos1 ? "DOS 2.x" : hasSparta1 ? "SpartaDOS" : "Custom";
            var fs2 = hasDos2 ? "DOS 2.x" : hasSparta2 ? "SpartaDOS" : "Custom";
            lines.Add($"  Filesystem: {fs1} vs {fs2}");

            if (hasDos1 && hasDos2)
            {
                DiffDosFilesystems(bytes1, bytes2, geo1, lines);
            }
            else if (hasSparta1 && hasSparta2)
            {
                DiffSpartaFilesystems(bytes1, bytes2, geo1, geo2, lines);
            }
            else
            {
                // Raw byte comparison
                var maxLen = Math.Max(bytes1.Length, bytes2.Length);
                var minLen = Math.Min(bytes1.Length, bytes2.Length);
                var diffCount = 0;
                for (var i = 16; i < minLen; i++) // Skip headers
                {
                    if (bytes1[i] != bytes2[i]) diffCount++;
                }

                lines.Add(string.Empty);
                lines.Add("  Filesystem types differ — showing raw byte comparison:");
                lines.Add($"  {diffCount} bytes differ (header-excluded) of {minLen - 16} comparable bytes");

                if (bytes1.Length != bytes2.Length)
                {
                    lines.Add($"  Size difference: {resolved1} has {bytes1.Length} bytes, {resolved2} has {bytes2.Length} bytes");
                }
            }

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Private helpers for ATR File Operations ─────────────────────────

    /// <summary>
    /// Parse a sector range string like "1", "1-5", "1,3,5" into a sorted list of sector numbers.
    /// Supports both decimal and hex (with $ or 0x prefix).
    /// </summary>
    private static List<int> ParseSectorRange(string range, int maxSector)
    {
        var result = new HashSet<int>();
        var parts = range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var rangeParts = part.Split('-', 2);
                if (rangeParts.Length == 2)
                {
                    var start = ParseSectorNumber(rangeParts[0], maxSector);
                    var end = ParseSectorNumber(rangeParts[1], maxSector);
                    if (start.HasValue && end.HasValue)
                    {
                        for (var s = start.Value; s <= end.Value; s++)
                            result.Add(s);
                    }
                }
            }
            else
            {
                var num = ParseSectorNumber(part, maxSector);
                if (num.HasValue)
                    result.Add(num.Value);
            }
        }

        return result.OrderBy(s => s).ToList();
    }

    private static int? ParseSectorNumber(string text, int maxSector)
    {
        int value;
        if (text.StartsWith("$") || text.StartsWith("0x"))
        {
            var hex = text.StartsWith("$") ? text[1..] : text[2..];
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value))
            {
                if (value >= 1 && value <= maxSector) return value;
            }
        }
        else
        {
            if (int.TryParse(text, out value))
            {
                if (value >= 1 && value <= maxSector) return value;
            }
        }
        return null;
    }

    private static string DescribeSector(
        int sector,
        bool hasDos,
        bool hasSparta,
        IReadOnlyList<AtrDirectoryEntry>? directory,
        List<SpartaDirEntry>? spartaDirectory,
        HashSet<int>? spartaDirSectors,
        byte[] data,
        AtrGeometry geometry)
    {
        if (sector <= 3)
        {
            return "Boot (part of boot loader, 3 sectors)";
        }

        if (hasDos)
        {
            if (sector == 360)
                return "VTOC (DOS 2.x volume table of contents)";
            if (sector >= 361 && sector <= 368)
                return $"Directory (DOS 2.x directory sector {sector - 360} of 8)";

            if (directory is not null)
            {
                foreach (var entry in directory)
                {
                    if (entry.IsDeleted) continue;
                    try
                    {
                        var chain = AtrParser.GetSectorChain(data, geometry, entry.StartSector);
                        var idx = chain.IndexOf(sector);
                        if (idx >= 0)
                        {
                            var fullName = string.IsNullOrWhiteSpace(entry.Extension)
                                ? entry.FileName
                                : $"{entry.FileName}.{entry.Extension}";
                            return $"File data ({fullName}, sector {idx + 1} of {chain.Count})";
                        }
                    }
                    catch { }
                }
            }

            return "Free (unused sector)";
        }

        if (hasSparta)
        {
            if (sector == 4)
                return "VTOC (SpartaDOS volume bitmap)";

            if (spartaDirSectors is not null && spartaDirSectors.Contains(sector))
            {
                var dirSectors = AtrParser.GetSpartaDirectorySectors(data, geometry);
                var idx = dirSectors.IndexOf(sector);
                return $"Directory (SpartaDOS directory sector {idx + 1} of {dirSectors.Count})";
            }

            if (spartaDirectory is not null)
            {
                foreach (var entry in spartaDirectory)
                {
                    if (entry.IsDeleted) continue;
                    try
                    {
                        var chain = AtrParser.GetSectorChain(data, geometry, entry.StartSector);
                        var idx = chain.IndexOf(sector);
                        if (idx >= 0)
                        {
                            return $"File data ({entry.FileName}, sector {idx + 1} of {chain.Count})";
                        }
                    }
                    catch { }
                }
            }

            return "Free (unused sector)";
        }

        return "Raw sector (no DOS filesystem detected)";
    }

    private static void DiffDosFilesystems(byte[] bytes1, byte[] bytes2, AtrGeometry geo1, List<string> lines)
    {
        var dir1 = AtrParser.ReadDirectory(bytes1).Where(e => !e.IsDeleted).ToList();
        var dir2 = AtrParser.ReadDirectory(bytes2).Where(e => !e.IsDeleted).ToList();

        var fileMap1 = dir1.ToDictionary(
            e => string.IsNullOrWhiteSpace(e.Extension) ? e.FileName : $"{e.FileName}.{e.Extension}",
            StringComparer.OrdinalIgnoreCase);
        var fileMap2 = dir2.ToDictionary(
            e => string.IsNullOrWhiteSpace(e.Extension) ? e.FileName : $"{e.FileName}.{e.Extension}",
            StringComparer.OrdinalIgnoreCase);

        var allNames = fileMap1.Keys.Union(fileMap2.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        var fileDiffCount = 0;

        lines.Add(string.Empty);
        lines.Add("  File differences:");

        foreach (var name in allNames)
        {
            var in1 = fileMap1.TryGetValue(name, out var entry1);
            var in2 = fileMap2.TryGetValue(name, out var entry2);

            if (!in1)
            {
                lines.Add($"    {name}: only in second image");
                fileDiffCount++;
                continue;
            }
            if (!in2)
            {
                lines.Add($"    {name}: only in first image");
                fileDiffCount++;
                continue;
            }

            // Both exist — compare contents
            try
            {
                var data1 = AtrParser.ExtractFile(bytes1, geo1, entry1!);
                var data2 = AtrParser.ExtractFile(bytes2, geo1, entry2!);

                if (data1.AsSpan().SequenceEqual(data2.AsSpan()))
                {
                    lines.Add($"    {name}: identical ({data1.Length} bytes)");
                }
                else
                {
                    var minLen = Math.Min(data1.Length, data2.Length);
                    var diffPositions = new List<int>();
                    for (var i = 0; i < minLen; i++)
                    {
                        if (data1[i] != data2[i])
                        {
                            diffPositions.Add(i);
                            if (diffPositions.Count >= 10) break; // Show first 10 diffs
                        }
                    }
                    var diffStr = string.Join(", ", diffPositions.Select(p => $"${p:X}"));
                    lines.Add($"    {name}: {minLen} comparable bytes, {diffPositions.Count} differ at offsets {diffStr}...");
                    fileDiffCount++;
                }
            }
            catch (Exception ex)
            {
                lines.Add($"    {name}: ERROR comparing \u2014 {ex.Message}");
                fileDiffCount++;
            }
        }

        if (fileDiffCount == 0)
            lines.Add("    (all files identical)");
    }

    private static void DiffSpartaFilesystems(byte[] bytes1, byte[] bytes2, AtrGeometry geo1, AtrGeometry geo2, List<string> lines)
    {
        var dir1 = AtrParser.ReadSpartaDirectory(bytes1).Where(e => !e.IsDeleted).ToList();
        var dir2 = AtrParser.ReadSpartaDirectory(bytes2).Where(e => !e.IsDeleted).ToList();

        var fileMap1 = dir1.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);
        var fileMap2 = dir2.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);

        var allNames = fileMap1.Keys.Union(fileMap2.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        var fileDiffCount = 0;

        lines.Add(string.Empty);
        lines.Add("  File differences:");

        foreach (var name in allNames)
        {
            var in1 = fileMap1.ContainsKey(name);
            var in2 = fileMap2.ContainsKey(name);

            if (!in1)
            {
                lines.Add($"    {name}: only in second image");
                fileDiffCount++;
                continue;
            }
            if (!in2)
            {
                lines.Add($"    {name}: only in first image");
                fileDiffCount++;
                continue;
            }

            try
            {
                var data1 = ExtractSpartaFile(bytes1, geo1, fileMap1[name]);
                var data2 = ExtractSpartaFile(bytes2, geo2, fileMap2[name]);

                if (data1.AsSpan().SequenceEqual(data2.AsSpan()))
                {
                    lines.Add($"    {name}: identical ({data1.Length} bytes)");
                }
                else
                {
                    var minLen = Math.Min(data1.Length, data2.Length);
                    var diffPositions = new List<int>();
                    for (var i = 0; i < minLen; i++)
                    {
                        if (data1[i] != data2[i])
                        {
                            diffPositions.Add(i);
                            if (diffPositions.Count >= 10) break;
                        }
                    }
                    var diffStr = string.Join(", ", diffPositions.Select(p => $"${p:X}"));
                    lines.Add($"    {name}: {minLen} comparable bytes, {diffPositions.Count} differ at offsets {diffStr}...");
                    fileDiffCount++;
                }
            }
            catch (Exception ex)
            {
                lines.Add($"    {name}: ERROR comparing \u2014 {ex.Message}");
                fileDiffCount++;
            }
        }

        if (fileDiffCount == 0)
            lines.Add("    (all files identical)");
    }

    // ─────────────────────────────────────────────────────────────
    // Batch operations (v2)
    // ─────────────────────────────────────────────────────────────

    public static string ExtractAll(string filePath, string? outputDir = null)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var data = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(data))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(data);
            var outputPath = outputDir ?? Path.Combine(Path.GetDirectoryName(resolvedPath) ?? ".", "extracted");
            Directory.CreateDirectory(outputPath);

            if (AtrParser.HasDosFilesystem(data))
            {
                return ExtractAllDos(data, geometry, resolvedPath, outputPath);
            }

            if (AtrParser.HasSpartaDosFilesystem(data))
            {
                return ExtractAllSparta(data, geometry, resolvedPath, outputPath);
            }

            if (AtrParser.HasMyDosFilesystem(data))
            {
                return ExtractAllMyDos(data, geometry, resolvedPath, outputPath);
            }

            return "ERROR: No DOS 2.x or SpartaDOS filesystem detected.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string ExtractAllDos(byte[] data, AtrGeometry geometry, string resolvedPath, string outputPath)
    {
        var directory = AtrParser.ReadDirectory(data);
        var activeFiles = directory.Where(e => !e.IsDeleted).ToList();

        var lines = new List<string>
        {
            $"Extracting all files from {Path.GetFileName(resolvedPath)}..."
        };

        var totalBytes = 0L;
        for (var i = 0; i < activeFiles.Count; i++)
        {
            var entry = activeFiles[i];
            try
            {
                var extracted = AtrParser.ExtractFile(data, geometry, entry);
                var fileName = string.IsNullOrWhiteSpace(entry.Extension)
                    ? entry.FileName
                    : $"{entry.FileName}.{entry.Extension}";
                var outputFile = Path.Combine(outputPath, fileName);
                File.WriteAllBytes(outputFile, extracted);
                totalBytes += extracted.Length;
                lines.Add($"  [{i + 1}/{activeFiles.Count}] {fileName} → {outputFile} ({extracted.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                var fileName = string.IsNullOrWhiteSpace(entry.Extension)
                    ? entry.FileName
                    : $"{entry.FileName}.{entry.Extension}";
                lines.Add($"  [{i + 1}/{activeFiles.Count}] {fileName} → ERROR: {ex.Message}");
            }
        }

        lines.Add($"  Complete: {activeFiles.Count} files extracted ({totalBytes:N0} bytes total)");
        return string.Join('\n', lines);
    }

    private static string ExtractAllSparta(byte[] data, AtrGeometry geometry, string resolvedPath, string outputPath)
    {
        var directory = AtrParser.ReadSpartaDirectory(data);
        var activeFiles = directory.Where(e => !e.IsDeleted).ToList();

        var lines = new List<string>
        {
            $"Extracting all files from {Path.GetFileName(resolvedPath)} (SpartaDOS)..."
        };

        var totalBytes = 0L;
        for (var i = 0; i < activeFiles.Count; i++)
        {
            var entry = activeFiles[i];
            try
            {
                var extracted = ExtractSpartaFile(data, geometry, entry);
                var outputFile = Path.Combine(outputPath, entry.FileName);
                File.WriteAllBytes(outputFile, extracted);
                totalBytes += extracted.Length;
                lines.Add($"  [{i + 1}/{activeFiles.Count}] {entry.FileName} → {outputFile} ({extracted.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                lines.Add($"  [{i + 1}/{activeFiles.Count}] {entry.FileName} → ERROR: {ex.Message}");
            }
        }

        lines.Add($"  Complete: {activeFiles.Count} files extracted ({totalBytes:N0} bytes total)");
        return string.Join('\n', lines);
    }

    private static string ExtractAllMyDos(byte[] data, AtrGeometry geometry, string resolvedPath, string outputPath)
    {
        var directory = AtrParser.ReadMyDosDirectory(data);
        var activeFiles = directory.Where(e => !e.IsDeleted).ToList();

        var lines = new List<string>
        {
            $"Extracting all files from {Path.GetFileName(resolvedPath)} (MyDOS)..."
        };

        var totalBytes = 0L;
        for (var i = 0; i < activeFiles.Count; i++)
        {
            var entry = activeFiles[i];
            try
            {
                var extracted = AtrParser.ExtractFile(data, geometry,
                    new AtrDirectoryEntry(entry.Index, entry.FileName, entry.Extension,
                        entry.StartSector, entry.SectorCount, entry.IsDeleted, entry.IsLocked, entry.IsBinary));
                var fileName = string.IsNullOrWhiteSpace(entry.Extension)
                    ? entry.FileName
                    : $"{entry.FileName}.{entry.Extension}";
                var outputFile = Path.Combine(outputPath, fileName);
                File.WriteAllBytes(outputFile, extracted);
                totalBytes += extracted.Length;
                lines.Add($"  [{i + 1}/{activeFiles.Count}] {fileName} → {outputFile} ({extracted.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                var fileName = string.IsNullOrWhiteSpace(entry.Extension)
                    ? entry.FileName
                    : $"{entry.FileName}.{entry.Extension}";
                lines.Add($"  [{i + 1}/{activeFiles.Count}] {fileName} → ERROR: {ex.Message}");
            }
        }

        lines.Add($"  Complete: {activeFiles.Count} files extracted ({totalBytes:N0} bytes total)");
        return string.Join('\n', lines);
    }

    public static string InjectAll(string filePath, string sourceDir, string? pattern = null, bool dryRun = false)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var data = File.ReadAllBytes(resolvedPath);
            if (!AtrParser.IsAtr(data))
                return "ERROR: Not a valid ATR image.";

            var geometry = AtrParser.ParseGeometry(data);

            var sourcePath = Path.GetFullPath(sourceDir);
            if (!Directory.Exists(sourcePath))
                return $"ERROR: Source directory not found: {sourcePath}";

            // Gather source files matching the pattern
            var searchPattern = pattern ?? "*.*";
            var sourceFiles = Directory.GetFiles(sourcePath, searchPattern)
                .Select(f => Path.GetFileName(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (AtrParser.HasDosFilesystem(data))
            {
                return InjectAllDos(data, geometry, resolvedPath, sourcePath, sourceFiles, dryRun);
            }

            if (AtrParser.HasSpartaDosFilesystem(data))
            {
                return InjectAllSparta(data, geometry, resolvedPath, sourcePath, sourceFiles, dryRun);
            }

            if (AtrParser.HasMyDosFilesystem(data))
            {
                return InjectAllDos(data, geometry, resolvedPath, sourcePath, sourceFiles, dryRun);
            }

            return "ERROR: No DOS 2.x or SpartaDOS filesystem detected.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string InjectAllDos(byte[] data, AtrGeometry geometry, string resolvedPath, string sourcePath, HashSet<string> sourceFiles, bool dryRun)
    {
        var directory = AtrParser.ReadDirectory(data);
        var activeFiles = directory.Where(e => !e.IsDeleted).ToList();

        var modifiedPath = AtrWriteTools.GetModifiedPath(resolvedPath);

        var lines = new List<string>
        {
            dryRun
                ? $"# DRY RUN: Would inject files into {Path.GetFileName(resolvedPath)}..."
                : $"Injecting files into {Path.GetFileName(resolvedPath)} (copy-on-write: {Path.GetFileName(modifiedPath)})..."
        };

        var totalBytes = 0L;
        var injected = 0;
        var skipped = 0;

        foreach (var entry in activeFiles)
        {
            var fileName = string.IsNullOrWhiteSpace(entry.Extension)
                ? entry.FileName
                : $"{entry.FileName}.{entry.Extension}";

            if (!sourceFiles.Contains(fileName))
            {
                skipped++;
                continue;
            }

            var inputFile = Path.Combine(sourcePath, fileName);
            if (!File.Exists(inputFile))
            {
                skipped++;
                continue;
            }

            var inputData = File.ReadAllBytes(inputFile);

            // Check capacity
            var fileCapacity = (geometry.SectorSize - 3) * entry.SectorCount;
            if (inputData.Length > fileCapacity)
            {
                lines.Add($"  [{injected + 1}/{activeFiles.Count}] {fileName} → SKIPPED (input {inputData.Length} bytes exceeds capacity {fileCapacity} bytes)");
                skipped++;
                continue;
            }

            if (dryRun)
            {
                lines.Add($"  [{injected + 1}/{activeFiles.Count}] {fileName} → {Path.GetFileName(resolvedPath)} ({inputData.Length:N0} bytes) [dry-run]");
            }
            else
            {
                // Inject the file using the existing chain
                var sector = entry.StartSector;
                var bytesWritten = 0;
                var remaining = inputData.Length;

                while (sector != 0 && remaining > 0)
                {
                    var sectorData = AtrParser.ReadSector(data, geometry, sector);
                    var dataCapacity = sectorData.Length - 3;
                    if (sectorData.Length < 3) break;

                    var chunkSize = Math.Min(remaining, dataCapacity);
                    Array.Copy(inputData, bytesWritten, sectorData, 0, chunkSize);
                    sectorData[^1] = (byte)chunkSize;

                    // Write sector back to the data buffer
                    var offset = AtrParser.SectorFileOffset(geometry, sector);
                    Array.Copy(sectorData, 0, data, offset, sectorData.Length);

                    bytesWritten += chunkSize;
                    remaining -= chunkSize;

                    var nextHi = sectorData[^3] & 0x03;
                    var nextLo = sectorData[^2];
                    sector = (nextHi << 8) | nextLo;
                }

                lines.Add($"  [{injected + 1}/{activeFiles.Count}] {fileName} → {Path.GetFileName(resolvedPath)} ({inputData.Length:N0} bytes) ✓");
            }

            injected++;
            totalBytes += inputData.Length;
        }

        if (!dryRun)
        {
            File.WriteAllBytes(modifiedPath, data);
        }

        var note = skipped > 0
            ? $"\n  Note: {skipped} file(s) in ATR had no matching source file in {sourcePath}"
            : string.Empty;

        lines.Add($"  Complete: {injected} files injected ({totalBytes:N0} bytes total){note}");

        return string.Join('\n', lines);
    }

    private static string InjectAllSparta(byte[] data, AtrGeometry geometry, string resolvedPath, string sourcePath, HashSet<string> sourceFiles, bool dryRun)
    {
        var directory = AtrParser.ReadSpartaDirectory(data);
        var activeFiles = directory.Where(e => !e.IsDeleted).ToList();

        var modifiedPath = AtrWriteTools.GetModifiedPath(resolvedPath);

        var lines = new List<string>
        {
            dryRun
                ? $"# DRY RUN: Would inject files into {Path.GetFileName(resolvedPath)} (SpartaDOS)..."
                : $"Injecting files into {Path.GetFileName(resolvedPath)} (SpartaDOS, copy-on-write: {Path.GetFileName(modifiedPath)})..."
        };

        var totalBytes = 0L;
        var injected = 0;
        var skipped = 0;

        foreach (var entry in activeFiles)
        {
            var fileName = entry.FileName;

            if (!sourceFiles.Contains(fileName))
            {
                skipped++;
                continue;
            }

            var inputFile = Path.Combine(sourcePath, fileName);
            if (!File.Exists(inputFile))
            {
                skipped++;
                continue;
            }

            var inputData = File.ReadAllBytes(inputFile);

            // Calculate capacity by following the chain
            var chain = AtrParser.GetSectorChain(data, geometry, entry.StartSector);
            var fileCapacity = chain.Count * (geometry.SectorSize - 3);
            if (inputData.Length > fileCapacity)
            {
                lines.Add($"  [{injected + 1}/{activeFiles.Count}] {fileName} → SKIPPED (input {inputData.Length} bytes exceeds capacity {fileCapacity} bytes)");
                skipped++;
                continue;
            }

            if (dryRun)
            {
                lines.Add($"  [{injected + 1}/{activeFiles.Count}] {fileName} → {Path.GetFileName(resolvedPath)} ({inputData.Length:N0} bytes) [dry-run]");
            }
            else
            {
                // Inject the file using the existing chain (same chain format as DOS 2.x)
                var sector = entry.StartSector;
                var bytesWritten = 0;
                var remaining = inputData.Length;

                while (sector != 0 && remaining > 0)
                {
                    var sectorData = AtrParser.ReadSector(data, geometry, sector);
                    var dataCapacity = sectorData.Length - 3;
                    if (sectorData.Length < 3) break;

                    var chunkSize = Math.Min(remaining, dataCapacity);
                    Array.Copy(inputData, bytesWritten, sectorData, 0, chunkSize);
                    sectorData[^1] = (byte)chunkSize;

                    var offset = AtrParser.SectorFileOffset(geometry, sector);
                    Array.Copy(sectorData, 0, data, offset, sectorData.Length);

                    bytesWritten += chunkSize;
                    remaining -= chunkSize;

                    var nextHi = sectorData[^3] & 0x03;
                    var nextLo = sectorData[^2];
                    sector = (nextHi << 8) | nextLo;
                }

                lines.Add($"  [{injected + 1}/{activeFiles.Count}] {fileName} → {Path.GetFileName(resolvedPath)} ({inputData.Length:N0} bytes) ✓");
            }

            injected++;
            totalBytes += inputData.Length;
        }

        if (!dryRun)
        {
            File.WriteAllBytes(modifiedPath, data);
        }

        var note = skipped > 0
            ? $"\n  Note: {skipped} file(s) in ATR had no matching source file in {sourcePath}"
            : string.Empty;

        lines.Add($"  Complete: {injected} files injected ({totalBytes:N0} bytes total){note}");

        return string.Join('\n', lines);
    }

    // ─────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────

    private static AtrDirectoryEntry? MatchEntry(IEnumerable<AtrDirectoryEntry> entries, string requestedName)
    {
        var normalized = requestedName.Trim().ToUpperInvariant();
        return entries.FirstOrDefault(entry =>
        {
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

            var nextHi = rawSector[^3] & 0x03;
            var nextLo = rawSector[^2];
            sector = (nextHi << 8) | nextLo;
        }

        return result.ToArray();
    }

    private static string BuildSyntheticPath(string atrPath, AtrDirectoryEntry entry)
    {
        var fileName = string.IsNullOrWhiteSpace(entry.Extension) ? entry.FileName : $"{entry.FileName}.{entry.Extension}";
        return atrPath + "/" + fileName;
    }

    private static string BuildSyntheticPath(string atrPath, string fileName)
    {
        return atrPath + "/" + fileName;
    }

    private static string DescribeDensity(AtrGeometry geometry) => geometry.Density switch
    {
        "SD" => "Single (SD)",
        "ED" => "Enhanced (ED)",
        "DD" => "Double (DD)",
        "Extended" => "Extended",
        _ => geometry.Density
    };
}
