using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class FileTools
{
    public static string LoadRom(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SessionPersistence persistence,
        string filePath)
    {
        try
        {
            var resolvedPath = Path.GetFullPath(filePath);
            var bytes = File.ReadAllBytes(resolvedPath);
            session.Load(resolvedPath, bytes);
            PopulateMetadata(session, bytes);
            var sidecarLoaded = persistence.TryLoad(resolvedPath);
            return $"Loaded ROM: {resolvedPath}\n" + BuildRomInfo(session, symbols, zeroPageMap, sidecarLoaded);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string RomInfo(RomSession session, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        try
        {
            if (!session.IsLoaded)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            return BuildRomInfo(session, symbols, zeroPageMap, sidecarLoaded: File.Exists(SessionPersistence.GetSidecarPath(session.FilePath!)));
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    internal static void PopulateMetadata(RomSession session, byte[] bytes)
    {
        session.ClearMetadata();
        if (!XexParser.IsXex(bytes))
        {
            return;
        }

        var metadata = XexParser.ParseMetadata(bytes);
        session.Segments = metadata.Segments;
        session.RunAddress = metadata.RunAddress;
        session.InitAddress = metadata.InitAddress;
    }

    internal static string BuildRomInfo(RomSession session, SymbolTable symbols, ZeroPageMap zeroPageMap, bool sidecarLoaded)
    {
        var lines = new List<string>
        {
            $"File path : {session.FilePath}",
            $"File size : {session.Length} bytes ({Formatting.HexWord((ushort)Math.Min(session.Length, 0xFFFF))})",
            $"Format    : {GetFormat(session)}"
        };

        if (session.Segments is { Count: > 0 })
        {
            for (var i = 0; i < session.Segments.Count; i++)
            {
                var segment = session.Segments[i];
                lines.Add($"Segment {i + 1}: {Formatting.HexWord(segment.LoadAddress)} - {Formatting.HexWord(segment.EndAddress)}  ({segment.Length} bytes, file offset ${segment.FileOffset:X4})");
            }

            lines.Add($"Run address : {FormatAddressWithSymbol(session.RunAddress, symbols, zeroPageMap)}");
            lines.Add($"Init address: {FormatAddressWithSymbol(session.InitAddress, symbols, zeroPageMap)}");
        }
        else if (session.BaseAddress is not null)
        {
            lines.Add($"Base address: {Formatting.HexWord(session.BaseAddress.Value)}");
        }

        lines.Add($"Sidecar   : {(sidecarLoaded ? "loaded" : "not found")}");
        return string.Join('\n', lines);
    }

    private static string GetFormat(RomSession session)
    {
        if (session.Segments is { Count: > 0 })
        {
            return "XEX";
        }

        return session.BaseAddress is not null ? "Raw binary (base address set)" : "Raw binary";
    }

    private static string FormatAddressWithSymbol(ushort? address, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        if (address is null)
        {
            return "--";
        }

        var symbol = SymbolResolver.Resolve(address.Value, symbols, zeroPageMap);
        return symbol is null ? Formatting.HexWord(address.Value) : $"{Formatting.HexWord(address.Value)} ({symbol})";
    }
}
