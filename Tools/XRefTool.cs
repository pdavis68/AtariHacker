using AtariHackerMCP.Atari;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static class XRefTool
{
    public static string XRef(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string address)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var target = AddressParser.ParseAddress(address);
            var hits = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var start in GetScanStarts(session))
            {
                var position = start;
                var segmentEnd = GetScanEnd(session, start);
                while (position < segmentEnd && position < session.Length)
                {
                    var opcode = session.Data[position];
                    if (!DisassemblerTool.TryGetOfficialEntry(opcode, out var entry) || position + entry.Bytes > session.Length)
                    {
                        position++;
                        continue;
                    }

                    var memoryAddress = XexAddressResolver.ResolveFileOffset(session, position) ?? (ushort)Math.Min(position, 0xFFFF);
                    var operandAddress = DisassemblerTool.ResolveOperandAddress(entry, session.Data, position, memoryAddress);
                    var matches = operandAddress == target || (target <= 0xFF && operandAddress == (byte)target);
                    if (matches)
                    {
                        var operand = DisassemblerTool.FormatOperand(entry, session.Data, position, memoryAddress, symbols, zeroPageMap);
                        var line = $"  {Formatting.HexWord(memoryAddress)}  {entry.Mnemonic}{(string.IsNullOrWhiteSpace(operand) ? string.Empty : " " + operand)}";
                        if (!hits.TryGetValue(entry.Mnemonic, out var list))
                        {
                            list = [];
                            hits[entry.Mnemonic] = list;
                        }

                        list.Add(line);
                    }

                    position += entry.Bytes;
                }
            }

            if (hits.Count == 0)
            {
                return $"No cross-references to {Formatting.HexWord(target)} were found.";
            }

            var headerSymbol = SymbolResolver.Resolve(target, symbols, zeroPageMap);
            var lines = new List<string> { $"Cross-references to {Formatting.WithSymbol(Formatting.HexWord(target), headerSymbol)}:" };
            foreach (var group in hits)
            {
                lines.Add($"{group.Key}:");
                lines.AddRange(group.Value);
                lines.Add(string.Empty);
            }

            if (lines[^1] == string.Empty)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static IEnumerable<int> GetScanStarts(RomSession session)
    {
        if (session.Segments is { Count: > 0 })
        {
            foreach (var segment in session.Segments)
            {
                yield return segment.FileOffset;
            }

            yield break;
        }

        yield return 0;
    }

    private static int GetScanEnd(RomSession session, int start)
    {
        if (session.Segments is { Count: > 0 })
        {
            var segment = session.Segments.First(candidate => candidate.FileOffset == start);
            return segment.FileOffset + segment.Length;
        }

        return session.Length;
    }
}
