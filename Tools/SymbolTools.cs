using System.Text.RegularExpressions;
using AtariHackerMCP.Atari;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static partial class SymbolTools
{
    public static string DefineSymbol(
        RomSession session,
        SymbolTable symbols,
        SessionPersistence persistence,
        string address,
        string label,
        string? comment = null)
    {
        try
        {
            if (!session.IsLoaded)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            if (!LabelRegex().IsMatch(label))
            {
                return $"ERROR: Invalid label '{label}'. Use identifier characters only.";
            }

            var parsedAddress = AddressParser.ParseAddress(address);
            symbols[parsedAddress] = new SymbolEntry(label, comment, false, true);
            persistence.Save();
            return $"Defined symbol {label} at {Formatting.HexWord(parsedAddress)}.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string RemoveSymbol(
        RomSession session,
        SymbolTable symbols,
        SessionPersistence persistence,
        string address)
    {
        try
        {
            if (!session.IsLoaded)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var parsedAddress = AddressParser.ParseAddress(address);
            if (!symbols.TryGetValue(parsedAddress, out var existing))
            {
                return $"ERROR: No symbol defined at {Formatting.HexWord(parsedAddress)}.";
            }

            if (existing.IsHardware && !existing.IsUserDefined)
            {
                return $"ERROR: Cannot remove hardware symbol at {Formatting.HexWord(parsedAddress)}.";
            }

            symbols.Remove(parsedAddress);
            if (AtariHardwareMap.TryGetHardwareSymbol(parsedAddress, out var hardware))
            {
                symbols[parsedAddress] = hardware;
            }

            persistence.Save();
            return $"Removed symbol at {Formatting.HexWord(parsedAddress)}.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string LookupSymbol(RomSession session, SymbolTable symbols, string address)
    {
        try
        {
            if (!session.IsLoaded)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var parsedAddress = AddressParser.ParseAddress(address);
            if (!symbols.TryGetValue(parsedAddress, out var symbol))
            {
                return $"No symbol defined at {Formatting.HexWord(parsedAddress)}.";
            }

            return string.Join('\n',
                $"Address      : {Formatting.HexWord(parsedAddress)}",
                $"Label        : {symbol.Label}",
                $"Comment      : {symbol.Comment ?? "--"}",
                $"Hardware     : {symbol.IsHardware}",
                $"User-defined : {symbol.IsUserDefined}");
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string ListSymbols(
        RomSession session,
        SymbolTable symbols,
        bool includeHardware = false,
        string? filter = null)
    {
        try
        {
            if (!session.IsLoaded)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var query = symbols
                .Where(pair => includeHardware || pair.Value.IsUserDefined)
                .Where(pair => string.IsNullOrWhiteSpace(filter) || pair.Value.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key)
                .ToList();

            if (query.Count == 0)
            {
                return "No symbols matched the current filter.";
            }

            return string.Join('\n', query.Select(pair =>
            {
                var comment = string.IsNullOrWhiteSpace(pair.Value.Comment) ? string.Empty : $"  ; {pair.Value.Comment}";
                return $"{Formatting.HexWord(pair.Key)}  {pair.Value.Label}{comment}";
            }));
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string SetSymbols(
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SessionPersistence persistence,
        bool? hardware = null,
        bool? osVariables = null,
        bool? osRom = null,
        bool? userLabels = null)
    {
        try
        {
            var groups = symbols.EnabledGroups;

            if (hardware.HasValue)
                groups = hardware.Value ? groups | SymbolGroup.Hardware : groups & ~SymbolGroup.Hardware;
            if (osVariables.HasValue)
                groups = osVariables.Value ? groups | SymbolGroup.OsVariables : groups & ~SymbolGroup.OsVariables;
            if (osRom.HasValue)
                groups = osRom.Value ? groups | SymbolGroup.OsRom : groups & ~SymbolGroup.OsRom;
            if (userLabels.HasValue)
                groups = userLabels.Value ? groups | SymbolGroup.UserLabels : groups & ~SymbolGroup.UserLabels;

            symbols.EnabledGroups = groups;
            persistence.Save();

            var lines = new List<string> { "Symbol groups updated:" };
            lines.Add($"  Hardware:    {(groups.HasFlag(SymbolGroup.Hardware) ? "ON" : "OFF")}");
            lines.Add($"  OS vars:     {(groups.HasFlag(SymbolGroup.OsVariables) ? "ON" : "OFF")}");
            lines.Add($"  OS ROM:      {(groups.HasFlag(SymbolGroup.OsRom) ? "ON" : "OFF")}");
            lines.Add($"  User labels: {(groups.HasFlag(SymbolGroup.UserLabels) ? "ON" : "OFF")}");

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string LoadLabels(
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SegmentManager segmentManager,
        string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"ERROR: Sidecar file not found: {filePath}";

            var json = File.ReadAllText(filePath);

            // Try to parse as v4 sidecar with segments
            var sidecar = System.Text.Json.JsonSerializer.Deserialize<LoadLabelsSidecar>(json, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            if (sidecar is null)
                return "ERROR: Invalid sidecar format.";

            var symbolCount = 0;
            var zpCount = 0;
            var segmentCount = 0;

            if (sidecar.Symbols is not null)
            {
                foreach (var kvp in sidecar.Symbols)
                {
                    var address = AddressParser.ParseAddress(kvp.Key);
                    symbols[address] = new SymbolEntry(kvp.Value.Label, kvp.Value.Comment, kvp.Value.IsHardware, kvp.Value.IsUserDefined);
                    symbolCount++;
                }
            }

            if (sidecar.ZeroPage is not null)
            {
                foreach (var kvp in sidecar.ZeroPage)
                {
                    var address = AddressParser.ParseZeroPageAddress(kvp.Key);
                    zeroPageMap[address] = new SymbolEntry(kvp.Value.Label, kvp.Value.Comment, kvp.Value.IsHardware, kvp.Value.IsUserDefined);
                    zpCount++;
                }
            }

            if (sidecar.Segments is not null)
            {
                segmentManager.Clear();
                foreach (var seg in sidecar.Segments)
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
                    segmentCount++;
                }
            }

            return $"Loaded {symbolCount} symbols, {zpCount} zero-page annotations, and {segmentCount} segments from {filePath}.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string SaveLabels(
        SessionPersistence persistence,
        string? filePath = null)
    {
        try
        {
            if (filePath is not null)
            {
                var parent = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);
            }

            persistence.Save();
            var savedPath = filePath ?? "(default sidecar path)";
            return $"Saved labels and segments to {savedPath}.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // Sidecar format for LoadLabels
    private sealed record LoadLabelsSidecar(
        string? Version,
        string? RomPath,
        string? RomHash,
        Dictionary<string, PersistedSymbol>? Symbols,
        Dictionary<string, PersistedSymbol>? ZeroPage,
        List<PersistedSegment>? Segments,
        PersistedFilesystem? Filesystem);

    private sealed record PersistedSymbol(string Label, string? Comment, bool IsHardware, bool IsUserDefined);
    private sealed record PersistedSegment(string Name, string Type, string Start, string End, string? Comment);
    private sealed record PersistedFilesystem(string Type, string? DirectoryOffset, int EntrySize, int FilenameLength, int ExtensionLength, int StartSectorOffset, int SectorCountOffset);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex LabelRegex();
}
