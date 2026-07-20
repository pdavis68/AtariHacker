using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static class FindPatternTool
{
    public static string FindPattern(
        RomSession session,
        string pattern,
        int maxResults = 50)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                return "ERROR: Pattern cannot be empty.";
            }

            var parsed = tokens.Select(ParseToken).ToArray();
            var matches = new List<string>();
            for (var offset = 0; offset <= session.Length - parsed.Length && matches.Count < Math.Max(maxResults, 1); offset++)
            {
                var isMatch = true;
                for (var i = 0; i < parsed.Length; i++)
                {
                    if (!parsed[i].IsWildcard && session.Data[offset + i] != parsed[i].Value)
                    {
                        isMatch = false;
                        break;
                    }
                }

                if (!isMatch)
                {
                    continue;
                }

                var address = XexAddressResolver.ResolveFileOffset(session, offset);
                var bytes = string.Join(' ', session.Data.Skip(offset).Take(parsed.Length).Select(value => value.ToString("X2")));
                matches.Add($"  File offset ${offset:X4}  ->  Memory {(address is null ? "(unknown)" : Formatting.HexWord(address.Value))}  :  {bytes}");
            }

            if (matches.Count == 0)
            {
                return $"Pattern: {pattern}\nFound 0 match(es).";
            }

            return $"Pattern: {pattern}\nFound {matches.Count} match(es):\n\n" + string.Join('\n', matches);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static (byte Value, bool IsWildcard) ParseToken(string token)
    {
        if (token == "??")
        {
            return (0, true);
        }

        if (!byte.TryParse(token, System.Globalization.NumberStyles.AllowHexSpecifier, null, out var value))
        {
            throw new FormatException($"Pattern parse failed at token '{token}'. Use hex bytes or '??' for wildcard.");
        }

        return (value, false);
    }
}
