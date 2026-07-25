using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class XRefTool
{
    public static string XRef(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string address,
        string format = "text")
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var target = AddressParser.ParseAddress(address);
            // Collect hits as structured data regardless of format
            var rows = new List<(ushort Address, string Mnemonic, string Operand, int FileOffset)>();

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
                        rows.Add((memoryAddress, entry.Mnemonic, operand, position));
                    }

                    position += entry.Bytes;
                }
            }

            if (rows.Count == 0)
            {
                return format.ToLowerInvariant() switch
                {
                    "csv" => OutputFormatter.FormatCsv(
                        new[] { "address", "mnemonic", "operand", "file_offset" },
                        Array.Empty<string[]>()),
                    "tsv" => OutputFormatter.FormatTsv(
                        new[] { "address", "mnemonic", "operand", "file_offset" },
                        Array.Empty<string[]>()),
                    "kv" => "",
                    _ => $"No cross-references to {Formatting.HexWord(target)} were found."
                };
            }

            return format.ToLowerInvariant() switch
            {
                "csv" => FormatXRefCsv(rows),
                "tsv" => FormatXRefTsv(rows),
                "kv" => FormatXRefKv(rows),
                _ => FormatXRefText(rows, target, symbols, zeroPageMap)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string FormatXRefText(
        List<(ushort Address, string Mnemonic, string Operand, int FileOffset)> rows,
        ushort target,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        // Group by mnemonic for text output
        var grouped = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (addr, mnemonic, operand, _) in rows)
        {
            var line = $"  {Formatting.HexWord(addr)}  {mnemonic}{(string.IsNullOrWhiteSpace(operand) ? string.Empty : " " + operand)}";
            if (!grouped.TryGetValue(mnemonic, out var list))
            {
                list = [];
                grouped[mnemonic] = list;
            }
            list.Add(line);
        }

        var headerSymbol = SymbolResolver.Resolve(target, symbols, zeroPageMap);
        var lines = new List<string> { $"Cross-references to {Formatting.WithSymbol(Formatting.HexWord(target), headerSymbol)}:" };
        foreach (var group in grouped)
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

    private static string FormatXRefCsv(List<(ushort Address, string Mnemonic, string Operand, int FileOffset)> rows)
    {
        var headers = new[] { "address", "mnemonic", "operand", "file_offset" };
        var data = rows.Select(r => new[]
        {
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            Formatting.HexOffset(r.FileOffset)
        }).ToArray();
        return OutputFormatter.FormatCsv(headers, data);
    }

    private static string FormatXRefTsv(List<(ushort Address, string Mnemonic, string Operand, int FileOffset)> rows)
    {
        var headers = new[] { "address", "mnemonic", "operand", "file_offset" };
        var data = rows.Select(r => new[]
        {
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            Formatting.HexOffset(r.FileOffset)
        }).ToArray();
        return OutputFormatter.FormatTsv(headers, data);
    }

    private static string FormatXRefKv(List<(ushort Address, string Mnemonic, string Operand, int FileOffset)> rows)
    {
        var keys = new[] { "address", "mnemonic", "operand", "file_offset" };
        var data = rows.Select(r => new[]
        {
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            Formatting.HexOffset(r.FileOffset)
        }).ToArray();
        return OutputFormatter.FormatKv(keys, data);
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
