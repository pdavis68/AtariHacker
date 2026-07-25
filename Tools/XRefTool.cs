using AtariHacker.Analysis;
using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class XRefTool
{
    /// <summary>
    /// Enhanced cross-reference scan with access type classification, filtering, and context.
    /// </summary>
    public static string XRef(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string address,
        string format = "text",
        string? typeFilter = null,
        List<ProcedureInfo>? procedures = null,
        SegmentManager? segmentManager = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var target = AddressParser.ParseAddress(address);
            var rows = new List<XRefEntry>();

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
                        var access = ClassifyAccess(entry.Mnemonic);
                        var procedure = ResolveProcedure(memoryAddress, procedures);
                        var segment = segmentManager?.GetSegmentName(memoryAddress);

                        // Apply type filter if specified
                        if (typeFilter is not null)
                        {
                            var filter = typeFilter.ToLowerInvariant() switch
                            {
                                "read" => AccessType.Read,
                                "write" => AccessType.Write,
                                "read-write" or "readwrite" or "modify" => AccessType.ReadWrite,
                                "execute" or "exec" or "call" => AccessType.Execute,
                                _ => (AccessType?)null
                            };

                            if (filter is not null && access != filter)
                            {
                                position += entry.Bytes;
                                continue;
                            }
                        }

                        rows.Add(new XRefEntry(
                            memoryAddress,
                            entry.Mnemonic,
                            operand,
                            access,
                            procedure,
                            segment));
                    }

                    position += entry.Bytes;
                }
            }

            // Sort deterministically: by address (asc), then mnemonic (alpha)
            var sortedRows = rows
                .OrderBy(r => r.Address)
                .ThenBy(r => r.Mnemonic, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sortedRows.Count == 0)
            {
                var typeSuffix = typeFilter is not null ? $" (type: {typeFilter})" : string.Empty;
                return format.ToLowerInvariant() switch
                {
                    "csv" => OutputFormatter.FormatCsv(
                        XRefCsvHeaders,
                        Array.Empty<string[]>()),
                    "tsv" => OutputFormatter.FormatTsv(
                        XRefCsvHeaders,
                        Array.Empty<string[]>()),
                    "kv" => string.Empty,
                    _ => $"No cross-references to {Formatting.HexWord(target)}{typeSuffix} were found."
                };
            }

            return format.ToLowerInvariant() switch
            {
                "csv" => FormatXRefCsv(sortedRows),
                "tsv" => FormatXRefTsv(sortedRows),
                "kv" => FormatXRefKv(sortedRows),
                _ => FormatXRefText(sortedRows, target, symbols, zeroPageMap)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Access Type Classification ───────────────────────────────────────

    /// <summary>
    /// Classify a mnemonic into an access type.
    /// </summary>
    public static AccessType ClassifyAccess(string mnemonic)
    {
        // Store instructions → write
        if (mnemonic is "STA" or "STX" or "STY")
            return AccessType.Write;

        // Modify instructions → read-write
        if (mnemonic is "INC" or "DEC" or "ASL" or "LSR" or "ROL" or "ROR")
            return AccessType.ReadWrite;

        // Jump/call instructions → execute
        if (mnemonic is "JSR" or "JMP")
            return AccessType.Execute;

        // All other register/accumulator instructions that reference memory → read
        // This includes LDA, LDX, LDY, ADC, SBC, CMP, BIT, AND, ORA, EOR, CPX, CPY
        // Also includes push/pull that indirectly reference stack
        return AccessType.Read;
    }

    // ─── Procedure Context Resolution ─────────────────────────────────────

    private static string? ResolveProcedure(ushort address, List<ProcedureInfo>? procedures)
    {
        if (procedures is null || procedures.Count == 0)
            return null;

        // Check if the address falls within any procedure's estimated range
        foreach (var proc in procedures)
        {
            if (proc.EstimatedEnd is not null)
            {
                if (address >= proc.EntryPoint && address <= proc.EstimatedEnd.Value)
                {
                    return proc.Name;
                }
            }
            else if (address == proc.EntryPoint)
            {
                return proc.Name;
            }
        }

        // Also check exact procedure entry points
        foreach (var proc in procedures)
        {
            if (address == proc.EntryPoint)
            {
                return proc.Name;
            }
        }

        return null;
    }

    // ─── Formatting ───────────────────────────────────────────────────────

    private static readonly string[] XRefCsvHeaders =
        ["address", "mnemonic", "operand", "access_type", "procedure", "segment"];

    private static string FormatXRefText(
        List<XRefEntry> rows,
        ushort target,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var headerSymbol = SymbolResolver.Resolve(target, symbols, zeroPageMap);
        var lines = new List<string>
        {
            $"Cross-references to {Formatting.WithSymbol(Formatting.HexWord(target), headerSymbol)}:"
        };
        lines.Add(string.Empty);

        foreach (var entry in rows)
        {
            var accessSymbol = entry.Access switch
            {
                AccessType.Read => "R",
                AccessType.Write => "W",
                AccessType.ReadWrite => "RW",
                AccessType.Execute => "X",
                _ => "?"
            };

            var context = new List<string>();
            if (entry.Procedure is not null)
                context.Add($"in {entry.Procedure}");
            if (entry.Segment is not null)
                context.Add($"segment: {entry.Segment}");

            var contextStr = context.Count > 0 ? $"  [{string.Join(", ", context)}]" : string.Empty;
            var line = $"  {Formatting.HexWord(entry.Address)}  [{accessSymbol}] {entry.Mnemonic}{(string.IsNullOrWhiteSpace(entry.Operand) ? string.Empty : " " + entry.Operand)}{contextStr}";
            lines.Add(line);
        }

        return string.Join('\n', lines);
    }

    private static string FormatXRefCsv(List<XRefEntry> rows)
    {
        var data = rows.Select(r => new[]
        {
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            r.Access.ToString().ToLowerInvariant(),
            r.Procedure ?? string.Empty,
            r.Segment ?? string.Empty
        }).ToArray();
        return OutputFormatter.FormatCsv(XRefCsvHeaders, data);
    }

    private static string FormatXRefTsv(List<XRefEntry> rows)
    {
        var data = rows.Select(r => new[]
        {
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            r.Access.ToString().ToLowerInvariant(),
            r.Procedure ?? string.Empty,
            r.Segment ?? string.Empty
        }).ToArray();
        return OutputFormatter.FormatTsv(XRefCsvHeaders, data);
    }

    private static string FormatXRefKv(List<XRefEntry> rows)
    {
        var keys = XRefCsvHeaders;
        var data = rows.Select(r => new[]
        {
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            r.Access.ToString().ToLowerInvariant(),
            r.Procedure ?? string.Empty,
            r.Segment ?? string.Empty
        }).ToArray();
        return OutputFormatter.FormatKv(keys, data);
    }

    // ─── Scan Utilities ───────────────────────────────────────────────────

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
