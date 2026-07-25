using System.Text;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class StringSearchTool
{
    public static string FindStrings(
        RomSession session,
        int minLength = 4,
        string encoding = "ascii",
        string? filter = null,
        int maxResults = 50,
        VerboseContext? verbose = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            minLength = Math.Max(1, minLength);
            var useAtascii = string.Equals(encoding, "atascii", StringComparison.OrdinalIgnoreCase);
            var results = new List<string>();
            var start = -1;
            var buffer = new StringBuilder();

            if (verbose is not null) verbose.BytesProcessed = session.Data.Length;
            for (var i = 0; i < session.Data.Length; i++)
            {
                if (TryDecode(session.Data[i], useAtascii, out var decoded))
                {
                    if (start < 0)
                    {
                        start = i;
                    }

                    buffer.Append(decoded);
                }
                else
                {
                    FlushRun(session, minLength, filter, results, ref start, buffer, maxResults);
                }
            }

            FlushRun(session, minLength, filter, results, ref start, buffer, maxResults);

            if (results.Count == 0)
            {
                return $"Strings found ({encoding}, minLen={minLength}):\n\n  <none>";
            }

            var summary = results.Count >= maxResults
                ? $" (showing first {maxResults}; result set truncated)"
                : string.Empty;

            return $"Strings found ({encoding}, minLen={minLength}){summary}:\n\n" + string.Join('\n', results);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private const int MaxDisplayedStringLength = 80;

    private static void FlushRun(RomSession session, int minLength, string? filter, List<string> results, ref int start, StringBuilder buffer, int maxResults = 50)
    {
        if (start >= 0 && buffer.Length >= minLength && results.Count < maxResults)
        {
            var text = buffer.ToString();
            if (string.IsNullOrWhiteSpace(filter) || text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                var address = XexAddressResolver.ResolveFileOffset(session, start);
                var displayText = text.Length <= MaxDisplayedStringLength
                    ? text
                    : text[..MaxDisplayedStringLength] + "...";
                results.Add($"  ${start:X4} / {(address is null ? "(unknown)" : Formatting.HexWord(address.Value))}  [{buffer.Length} bytes] \"{displayText}\"");
            }
        }

        start = -1;
        buffer.Clear();
    }

    private static bool TryDecode(byte value, bool atascii, out string decoded)
    {
        if (!atascii)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                decoded = ((char)value).ToString();
                return true;
            }

            decoded = string.Empty;
            return false;
        }

        var ch = AtasciiDecoder.DecodeByte(value);
        if (ch != '.')
        {
            // AtasciiDecoder.DecodeByte uses char >= 128 as inverse marker
            if (ch >= 128)
            {
                decoded = "~" + (char)(ch - 128);
            }
            else
            {
                decoded = ch.ToString();
            }
            return true;
        }

        decoded = string.Empty;
        return false;
    }
}
