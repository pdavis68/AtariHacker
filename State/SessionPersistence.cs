using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtariHacker.Atari;
using AtariHacker.Helpers;

namespace AtariHacker.State;

public sealed class SessionPersistence(RomSession session, SymbolTable symbols, ZeroPageMap zeroPageMap, SegmentManager segmentManager)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void ResetToDefaults()
    {
        AtariHardwareMap.Populate(symbols);
        AtariHardwareMap.PopulateZeroPage(zeroPageMap);
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(session.FilePath))
        {
            return;
        }

        var sidecarPath = GetSidecarPath(session.FilePath);
        var parent = Path.GetDirectoryName(sidecarPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var payload = new SessionSidecarV4(
            "4.0",
            session.FilePath,
            ComputeHash(session.Data),
            symbols.OrderBy(pair => pair.Key).ToDictionary(
                pair => $"0x{pair.Key:X4}",
                pair => new PersistedSymbol(pair.Value.Label, pair.Value.Comment, pair.Value.IsHardware, pair.Value.IsUserDefined)),
            zeroPageMap.OrderBy(pair => pair.Key).ToDictionary(
                pair => $"0x{pair.Key:X2}",
                pair => new PersistedSymbol(pair.Value.Label, pair.Value.Comment, pair.Value.IsHardware, pair.Value.IsUserDefined)),
            segmentManager.Segments.Count > 0
                ? segmentManager.Segments.Select(s => new PersistedSegment(s.Name, s.Type.ToString().ToLowerInvariant(), $"0x{s.Start:X4}", $"0x{s.End:X4}", s.Comment)).ToList()
                : null,
            null);

        File.WriteAllText(sidecarPath, JsonSerializer.Serialize(payload, SerializerOptions));
    }

    public bool TryLoad(string romPath)
    {
        ResetToDefaults();

        var sidecarPath = GetSidecarPath(romPath);
        if (!File.Exists(sidecarPath))
        {
            return false;
        }

        var text = File.ReadAllText(sidecarPath);

        // Try v4 format first (has version field)
        SessionSidecarV4? sidecarV4 = null;
        try
        {
            sidecarV4 = JsonSerializer.Deserialize<SessionSidecarV4>(text, SerializerOptions);
        }
        catch
        {
            // Not v4 format, try v3
        }

        if (sidecarV4?.Version is not null)
        {
            // v4 format
            LoadSymbols(sidecarV4.Symbols);
            LoadZeroPage(sidecarV4.ZeroPage);
            LoadSegments(sidecarV4.Segments);
        }
        else
        {
            // v3 format (no version field)
            try
            {
                var sidecarV3 = JsonSerializer.Deserialize<SessionSidecarV3>(text, SerializerOptions);
                if (sidecarV3 is not null)
                {
                    LoadSymbols(sidecarV3.Symbols);
                    LoadZeroPage(sidecarV3.ZeroPage);
                }
            }
            catch
            {
                return false;
            }
        }

        // Re-apply hardware symbols (user symbols may override them)
        foreach (var pair in AtariHardwareMap.HardwareSymbols)
        {
            symbols.TryAdd(pair.Key, pair.Value);
        }

        foreach (var pair in AtariHardwareMap.ZeroPageSymbols)
        {
            zeroPageMap.TryAdd(pair.Key, pair.Value);
        }

        return true;
    }

    private void LoadSymbols(Dictionary<string, PersistedSymbol>? persisted)
    {
        if (persisted is null) return;
        foreach (var pair in persisted)
        {
            var address = AddressParser.ParseAddress(pair.Key);
            symbols[address] = new SymbolEntry(pair.Value.Label, pair.Value.Comment, pair.Value.IsHardware, pair.Value.IsUserDefined);
        }
    }

    private void LoadZeroPage(Dictionary<string, PersistedSymbol>? persisted)
    {
        if (persisted is null) return;
        foreach (var pair in persisted)
        {
            var address = AddressParser.ParseZeroPageAddress(pair.Key);
            zeroPageMap[address] = new SymbolEntry(pair.Value.Label, pair.Value.Comment, pair.Value.IsHardware, pair.Value.IsUserDefined);
        }
    }

    private void LoadSegments(List<PersistedSegment>? persisted)
    {
        if (persisted is null) return;
        segmentManager.Clear();
        foreach (var seg in persisted)
        {
            var type = seg.Type?.ToLowerInvariant() switch
            {
                "code" => SegmentType.Code,
                "data" => SegmentType.Data,
                "graphics" => SegmentType.Graphics,
                "text" => SegmentType.Text,
                "zero_page" => SegmentType.ZeroPage,
                _ => SegmentType.Data
            };
            var start = AddressParser.ParseAddress(seg.Start);
            var end = AddressParser.ParseAddress(seg.End);
            segmentManager.Define(new SegmentDefinition(seg.Name, type, start, end, seg.Comment));
        }
    }

    public static string GetSidecarPath(string romPath)
    {
        var candidateDirectory = Path.GetDirectoryName(romPath);
        if (!string.IsNullOrWhiteSpace(candidateDirectory) && Directory.Exists(candidateDirectory))
        {
            return romPath + ".atarihacker.json";
        }

        var fullPath = Path.GetFullPath(romPath);
        var current = fullPath;
        while (!string.IsNullOrWhiteSpace(current) && !Directory.Exists(current))
        {
            current = Path.GetDirectoryName(current);
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return fullPath.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_') + ".atarihacker.json";
        }

        var relative = Path.GetRelativePath(current, fullPath)
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
        return Path.Combine(current, relative + ".atarihacker.json");
    }

    private static string? ComputeHash(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    // ─── Sidecar format records ────────────────────────────────────────────

    // v4 format (current)
    private sealed record SessionSidecarV4(
        string Version,
        string? RomPath,
        string? RomHash,
        Dictionary<string, PersistedSymbol> Symbols,
        Dictionary<string, PersistedSymbol> ZeroPage,
        List<PersistedSegment>? Segments,
        PersistedFilesystem? Filesystem);

    // v3 format (backward compatibility)
    private sealed record SessionSidecarV3(
        string? RomPath,
        Dictionary<string, PersistedSymbol> Symbols,
        Dictionary<string, PersistedSymbol> ZeroPage);

    private sealed record PersistedSymbol(
        string Label,
        string? Comment,
        bool IsHardware,
        bool IsUserDefined);

    private sealed record PersistedSegment(
        string Name,
        string Type,
        string Start,
        string End,
        string? Comment);

    private sealed record PersistedFilesystem(
        string Type,
        string? DirectoryOffset,
        int EntrySize,
        int FilenameLength,
        int ExtensionLength,
        int StartSectorOffset,
        int SectorCountOffset);
}
