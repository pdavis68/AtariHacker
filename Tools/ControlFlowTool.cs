using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class ControlFlowTool
{
    public static string TraceControlFlow(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string address,
        int maxDepth = 5,
        int maxInstructions = 500,
        string format = "text")
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var startAddress = AddressParser.ParseAddress(address);
            var startOffset = XexAddressResolver.ResolveMemoryAddress(session, startAddress);
            if (startOffset is null)
            {
                return $"ERROR: Address {Formatting.HexWord(startAddress)} is not covered by the loaded ROM.";
            }

            var budget = Math.Max(1, maxInstructions);
            var traceRows = new List<(int Depth, ushort Address, string Mnemonic, string Operand, string Type)>();
            TraceBlockStructured(session, symbols, zeroPageMap, startAddress, startOffset.Value, 0, Math.Max(0, maxDepth), new HashSet<ushort>(), new HashSet<ushort>(), traceRows, ref budget);

            if (traceRows.Count == 0)
            {
                return format.ToLowerInvariant() switch
                {
                    "csv" => OutputFormatter.FormatCsv(
                        new[] { "depth", "address", "mnemonic", "operand", "type" },
                        Array.Empty<string[]>()),
                    "tsv" => OutputFormatter.FormatTsv(
                        new[] { "depth", "address", "mnemonic", "operand", "type" },
                        Array.Empty<string[]>()),
                    "kv" => "",
                    _ => "No trace results."
                };
            }

            return format.ToLowerInvariant() switch
            {
                "csv" => FormatTraceCsv(traceRows),
                "tsv" => FormatTraceTsv(traceRows),
                "kv" => FormatTraceKv(traceRows),
                _ => FormatTraceText(traceRows, session, startAddress, startOffset.Value, symbols, zeroPageMap)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string FormatTraceText(
        List<(int Depth, ushort Address, string Mnemonic, string Operand, string Type)> traceRows,
        RomSession session,
        ushort startAddress,
        int startOffset,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var lines = new List<string> { FormatNodeHeader(startAddress, symbols, zeroPageMap) };
        foreach (var (depth, addr, mnemonic, operand, type) in traceRows)
        {
            var indent = new string(' ', (depth + 1) * 2);
            if (type == "header")
            {
                lines.Add($"{indent}{Formatting.HexWord(addr)} ({operand})");
            }
            else if (type == "loop")
            {
                lines.Add($"{indent}{Formatting.HexWord(addr)} [loop]");
            }
            else if (type == "note")
            {
                lines.Add($"{indent}{operand}");
            }
            else
            {
                lines.Add($"{indent}{Formatting.HexWord(addr)}  {mnemonic}{(string.IsNullOrWhiteSpace(operand) ? string.Empty : " -> " + operand)}");
            }
        }

        // BRK hint
        if (session.Data is not null && startOffset < session.Length && session.Data[startOffset] == 0x00)
        {
            lines.Add("");
            lines.Add($"NOTE: {Formatting.HexWord(startAddress)} disassembles as BRK. If this is a boot sector, the actual");
            lines.Add($"      code starts at $0706 (after the 6-byte boot header). Use");
            lines.Add($"      analyze_boot_sector to confirm, then re-run with address=$0706.");
        }

        return string.Join('\n', lines);
    }

    private static string FormatTraceCsv(List<(int Depth, ushort Address, string Mnemonic, string Operand, string Type)> traceRows)
    {
        var headers = new[] { "depth", "address", "mnemonic", "operand", "type" };
        var data = traceRows.Select(r => new[]
        {
            r.Depth.ToString(),
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            r.Type
        }).ToArray();
        return OutputFormatter.FormatCsv(headers, data);
    }

    private static string FormatTraceTsv(List<(int Depth, ushort Address, string Mnemonic, string Operand, string Type)> traceRows)
    {
        var headers = new[] { "depth", "address", "mnemonic", "operand", "type" };
        var data = traceRows.Select(r => new[]
        {
            r.Depth.ToString(),
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            r.Type
        }).ToArray();
        return OutputFormatter.FormatTsv(headers, data);
    }

    private static string FormatTraceKv(List<(int Depth, ushort Address, string Mnemonic, string Operand, string Type)> traceRows)
    {
        var keys = new[] { "depth", "address", "mnemonic", "operand", "type" };
        var data = traceRows.Select(r => new[]
        {
            r.Depth.ToString(),
            Formatting.HexWord(r.Address),
            r.Mnemonic,
            r.Operand,
            r.Type
        }).ToArray();
        return OutputFormatter.FormatKv(keys, data);
    }

    private static void TraceBlock(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        ushort address,
        int offset,
        int depth,
        int maxDepth,
        HashSet<ushort> activePath,
        HashSet<ushort> visited,
        List<string> lines,
        ref int budget)
    {
        if (budget <= 0)
        {
            lines.Add($"{Indent(depth + 1)}[instruction budget exhausted]");
            return;
        }

        if (!activePath.Add(address))
        {
            lines.Add($"{Indent(depth + 1)}{Formatting.HexWord(address)} [loop]");
            return;
        }

        visited.Add(address);
        var position = offset;
        while (budget > 0 && position < session.Length)
        {
            budget--;
            var opcode = session.Data![position];
            if (!DisassemblerTool.TryGetOfficialEntry(opcode, out var entry) || position + entry.Bytes > session.Length)
            {
                lines.Add($"{Indent(depth + 1)}{Formatting.HexWord(XexAddressResolver.ResolveFileOffset(session, position) ?? (ushort)position)}  .db ${opcode:X2}");
                position++;
                continue;
            }

            var currentAddress = XexAddressResolver.ResolveFileOffset(session, position) ?? (ushort)position;
            var operand = DisassemblerTool.FormatOperand(entry, session.Data, position, currentAddress, symbols, zeroPageMap);
            var operandAddress = DisassemblerTool.ResolveOperandAddress(entry, session.Data, position, currentAddress);
            var line = $"{Indent(depth + 1)}{Formatting.HexWord(currentAddress)}  {entry.Mnemonic}{(string.IsNullOrWhiteSpace(operand) ? string.Empty : " -> " + operand)}";
            lines.Add(line);

            if (entry.Mnemonic is "RTS" or "RTI")
            {
                break;
            }

            if (entry.Mnemonic == "BRK")
            {
                lines.Add($"{Indent(depth + 2)}[BRK]");
                break;
            }

            if (entry.Mnemonic == "JSR" && operandAddress is not null)
            {
                if (depth >= maxDepth)
                {
                    lines.Add($"{Indent(depth + 2)}[max depth reached]");
                }
                else if (activePath.Contains(operandAddress.Value))
                {
                    lines.Add($"{Indent(depth + 2)}{Formatting.HexWord(operandAddress.Value)} [loop]");
                }
                else
                {
                    var targetOffset = XexAddressResolver.ResolveMemoryAddress(session, operandAddress.Value);
                    if (targetOffset is not null)
                    {
                        lines.Add($"{Indent(depth + 2)}{FormatNodeHeader(operandAddress.Value, symbols, zeroPageMap)}");
                        TraceBlock(session, symbols, zeroPageMap, operandAddress.Value, targetOffset.Value, depth + 1, maxDepth, new HashSet<ushort>(activePath), visited, lines, ref budget);
                    }
                }

                position += entry.Bytes;
                continue;
            }

            if (entry.Mnemonic == "JMP")
            {
                if (entry.Mode == AddressingMode.Indirect)
                {
                    lines.Add($"{Indent(depth + 2)}[indirect jump, cannot trace statically]");
                }
                else if (operandAddress is not null)
                {
                    if (activePath.Contains(operandAddress.Value))
                    {
                        lines.Add($"{Indent(depth + 2)}{Formatting.HexWord(operandAddress.Value)} [loop]");
                    }
                    else
                    {
                        var targetOffset = XexAddressResolver.ResolveMemoryAddress(session, operandAddress.Value);
                        if (targetOffset is not null)
                        {
                            TraceBlock(session, symbols, zeroPageMap, operandAddress.Value, targetOffset.Value, depth, maxDepth, new HashSet<ushort>(activePath), visited, lines, ref budget);
                        }
                    }
                }

                break;
            }

            if (entry.Mode == AddressingMode.Relative && operandAddress is not null)
            {
                if (activePath.Contains(operandAddress.Value))
                {
                    lines.Add($"{Indent(depth + 2)}{entry.Mnemonic} target {Formatting.HexWord(operandAddress.Value)} [loop]");
                }
                else
                {
                    var targetOffset = XexAddressResolver.ResolveMemoryAddress(session, operandAddress.Value);
                    if (targetOffset is not null)
                    {
                        lines.Add($"{Indent(depth + 2)}Branch target {Formatting.HexWord(operandAddress.Value)}");
                        TraceBlock(session, symbols, zeroPageMap, operandAddress.Value, targetOffset.Value, depth + 1, maxDepth, new HashSet<ushort>(activePath), visited, lines, ref budget);
                    }
                }
            }

            position += entry.Bytes;
        }
    }

    private static string FormatNodeHeader(ushort address, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        var label = SymbolResolver.Resolve(address, symbols, zeroPageMap);
        return label is null ? Formatting.HexWord(address) : $"{Formatting.HexWord(address)} ({label})";
    }

    private static string Indent(int depth) => new(' ', depth * 2);

    /// <summary>
    /// Structured version of TraceBlock that collects rows instead of formatting text.
    /// </summary>
    private static void TraceBlockStructured(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        ushort address,
        int offset,
        int depth,
        int maxDepth,
        HashSet<ushort> activePath,
        HashSet<ushort> visited,
        List<(int Depth, ushort Address, string Mnemonic, string Operand, string Type)> rows,
        ref int budget)
    {
        if (budget <= 0)
        {
            rows.Add((depth + 1, address, "", "instruction budget exhausted", "note"));
            return;
        }

        if (!activePath.Add(address))
        {
            rows.Add((depth + 1, address, "", "", "loop"));
            return;
        }

        visited.Add(address);
        var position = offset;
        while (budget > 0 && position < session.Length)
        {
            budget--;
            var opcode = session.Data![position];
            if (!DisassemblerTool.TryGetOfficialEntry(opcode, out var entry) || position + entry.Bytes > session.Length)
            {
                var currentAddr = XexAddressResolver.ResolveFileOffset(session, position) ?? (ushort)position;
                rows.Add((depth + 1, currentAddr, ".db", $"${opcode:X2}", "data"));
                position++;
                continue;
            }

            var currentAddress = XexAddressResolver.ResolveFileOffset(session, position) ?? (ushort)position;
            var operand = DisassemblerTool.FormatOperand(entry, session.Data, position, currentAddress, symbols, zeroPageMap);
            var operandAddress = DisassemblerTool.ResolveOperandAddress(entry, session.Data, position, currentAddress);
            rows.Add((depth + 1, currentAddress, entry.Mnemonic, operand, "instruction"));

            if (entry.Mnemonic is "RTS" or "RTI")
            {
                break;
            }

            if (entry.Mnemonic == "BRK")
            {
                rows.Add((depth + 2, 0, "", "[BRK]", "note"));
                break;
            }

            if (entry.Mnemonic == "JSR" && operandAddress is not null)
            {
                if (depth >= maxDepth)
                {
                    rows.Add((depth + 2, 0, "", "[max depth reached]", "note"));
                }
                else if (activePath.Contains(operandAddress.Value))
                {
                    rows.Add((depth + 2, operandAddress.Value, "", "", "loop"));
                }
                else
                {
                    var targetOffset = XexAddressResolver.ResolveMemoryAddress(session, operandAddress.Value);
                    if (targetOffset is not null)
                    {
                        var label = SymbolResolver.Resolve(operandAddress.Value, symbols, zeroPageMap) ?? $"{operandAddress.Value:X4}";
                        rows.Add((depth + 2, operandAddress.Value, "", label, "header"));
                        TraceBlockStructured(session, symbols, zeroPageMap, operandAddress.Value, targetOffset.Value, depth + 1, maxDepth, new HashSet<ushort>(activePath), visited, rows, ref budget);
                    }
                }

                position += entry.Bytes;
                continue;
            }

            if (entry.Mnemonic == "JMP")
            {
                if (entry.Mode == AddressingMode.Indirect)
                {
                    rows.Add((depth + 2, 0, "", "[indirect jump, cannot trace statically]", "note"));
                }
                else if (operandAddress is not null)
                {
                    if (activePath.Contains(operandAddress.Value))
                    {
                        rows.Add((depth + 2, operandAddress.Value, "", "", "loop"));
                    }
                    else
                    {
                        var targetOffset = XexAddressResolver.ResolveMemoryAddress(session, operandAddress.Value);
                        if (targetOffset is not null)
                        {
                            TraceBlockStructured(session, symbols, zeroPageMap, operandAddress.Value, targetOffset.Value, depth, maxDepth, new HashSet<ushort>(activePath), visited, rows, ref budget);
                        }
                    }
                }

                break;
            }

            if (entry.Mode == AddressingMode.Relative && operandAddress is not null)
            {
                if (activePath.Contains(operandAddress.Value))
                {
                    rows.Add((depth + 2, operandAddress.Value, "", "", "loop"));
                }
                else
                {
                    var targetOffset = XexAddressResolver.ResolveMemoryAddress(session, operandAddress.Value);
                    if (targetOffset is not null)
                    {
                        rows.Add((depth + 2, operandAddress.Value, "", $"Branch target {Formatting.HexWord(operandAddress.Value)}", "note"));
                        TraceBlockStructured(session, symbols, zeroPageMap, operandAddress.Value, targetOffset.Value, depth + 1, maxDepth, new HashSet<ushort>(activePath), visited, rows, ref budget);
                    }
                }
            }

            position += entry.Bytes;
        }
    }
}
