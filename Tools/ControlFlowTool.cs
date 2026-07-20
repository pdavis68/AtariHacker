using AtariHackerMCP.Atari;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static class ControlFlowTool
{
    public static string TraceControlFlow(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string address,
        int maxDepth = 5,
        int maxInstructions = 500)
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
            var lines = new List<string> { FormatNodeHeader(startAddress, symbols, zeroPageMap) };
            TraceBlock(session, symbols, zeroPageMap, startAddress, startOffset.Value, 0, Math.Max(0, maxDepth), new HashSet<ushort>(), new HashSet<ushort>(), lines, ref budget);

            // BRK hint: if the first instruction at the start address is BRK (opcode $00)
            if (startOffset.Value < session.Length && session.Data[startOffset.Value] == 0x00)
            {
                lines.Add("");
                lines.Add($"NOTE: {Formatting.HexWord(startAddress)} disassembles as BRK. If this is a boot sector, the actual");
                lines.Add($"      code starts at $0706 (after the 6-byte boot header). Use");
                lines.Add($"      analyze_boot_sector to confirm, then re-run with address=$0706.");
            }

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
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
}
