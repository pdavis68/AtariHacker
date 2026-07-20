using AtariHackerMCP.Atari;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

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

            if (!AtrParser.HasDosFilesystem(bytes))
            {
                lines.Add("No DOS 2.x filesystem detected. This disk uses a custom/non-DOS layout.");
                lines.Add("Use load_atr_boot to inspect the boot loader or load_rom for raw binary access.");
                return string.Join('\n', lines);
            }

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
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string LoadAtrFile(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SessionPersistence persistence,
        string filePath,
        string fileName)
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
            var directory = AtrParser.ReadDirectory(bytes);
            var match = MatchEntry(directory, fileName);
            if (match is null)
            {
                return $"ERROR: File \"{fileName}\" not found in ATR directory.";
            }

            if (match.IsDeleted)
            {
                return $"ERROR: File \"{fileName}\" exists but is deleted.";
            }

            var extracted = AtrParser.ExtractFile(bytes, geometry, match);
            var syntheticPath = BuildSyntheticPath(resolvedPath, match);
            session.Load(syntheticPath, extracted);
            session.SourceAtrPath = resolvedPath;
            FileTools.PopulateMetadata(session, extracted);
            var sidecarLoaded = persistence.TryLoad(syntheticPath);
            return $"Extracted {match.FileName}{(string.IsNullOrWhiteSpace(match.Extension) ? string.Empty : "." + match.Extension)} from ATR.\n" + FileTools.BuildRomInfo(session, symbols, zeroPageMap, sidecarLoaded);
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

            if (!AtrParser.HasDosFilesystem(bytes))
                return "ERROR: No DOS 2.x filesystem detected on this disk image. " +
                       "This disk may use a custom/non-DOS layout. " +
                       "Use load_rom to load it as a raw binary, or load_atr_boot to inspect the boot loader.";

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
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
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

    private static string BuildSyntheticPath(string atrPath, AtrDirectoryEntry entry)
    {
        var fileName = string.IsNullOrWhiteSpace(entry.Extension) ? entry.FileName : $"{entry.FileName}.{entry.Extension}";
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
