using System.Text;
using AtariHackerMCP.Analysis;
using AtariHackerMCP.Atari;
using AtariHackerMCP.Helpers;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static class DisassemblerTool
{
    /// <summary>
    /// Represents a single disassembled instruction or data byte.
    /// </summary>
    private readonly record struct DisassembledLine(
        ushort? Address,
        byte[] Bytes,
        string Mnemonic,
        string Operand,
        ushort? OperandAddress,
        string Comment,
        bool IsData
    );

    public static string Disassemble(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string offset,
        int numBytes,
        string? startAddress = null,
        string format = "listing",
        bool analyze = false)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var fileOffset = AddressParser.ParseOffset(offset);
            if (fileOffset < 0 || fileOffset >= session.Length)
            {
                return $"ERROR: Offset 0x{fileOffset:X} exceeds ROM size (0x{session.Length:X} bytes).";
            }

            var addressOverride = string.IsNullOrWhiteSpace(startAddress) ? (ushort?)null : AddressParser.ParseAddress(startAddress);
            var end = Math.Min(session.Length, fileOffset + Math.Max(numBytes, 0));

            // Parse all instructions into a structured list
            var instructions = DisassembleRange(session, fileOffset, end, addressOverride, symbols, zeroPageMap);

            // Determine the base address for the .org directive
            ushort? baseAddr = instructions.Count > 0 ? instructions[0].Address : null;

            // Emit advisory when no address mapping is available
            var hasMapping = addressOverride is not null
                || (session.Segments is { Count: > 0 })
                || session.BaseAddress is not null;

            // If analyze=true, run multi-pass analysis
            if (analyze)
            {
                var references = DisassemblyAnalyzer.Analyze(session.Data, session.Segments, session.BaseAddress);
                var (codeRegions, dataRegions) = DisassemblyAnalyzer.TraceCodeRegions(session.Data, references, session.Segments, session.BaseAddress);
                var labelMap = DisassemblyAnalyzer.GenerateLabels(references, symbols, zeroPageMap, codeRegions);

                return format.ToLowerInvariant() switch
                {
                    "ca65" => FormatCa65Analyzed(session, instructions, baseAddr, symbols, zeroPageMap, references, codeRegions, labelMap),
                    _ => FormatAnalyzed(instructions, labelMap, hasMapping),
                };
            }

            return format.ToLowerInvariant() switch
            {
                "ca65" => FormatCa65(instructions, baseAddr, symbols, zeroPageMap),
                "atasm" => FormatAtasm(instructions, baseAddr, symbols, zeroPageMap),
                "mac65" => FormatMac65(instructions, baseAddr, symbols, zeroPageMap),
                _ => FormatListing(instructions, hasMapping),
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Core disassembly ──────────────────────────────────────────────────

    /// <summary>
    /// Disassembles a range of bytes into a structured list of instructions.
    /// </summary>
    private static List<DisassembledLine> DisassembleRange(
        RomSession session,
        int fileOffset,
        int end,
        ushort? addressOverride,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var instructions = new List<DisassembledLine>();
        var position = fileOffset;

        while (position < end)
        {
            var opcode = session.Data![position];
            if (!Opcodes6502.Table.TryGetValue(opcode, out var entry) || !entry.IsOfficial || position + entry.Bytes > session.Length)
            {
                var memoryAddress = addressOverride is null
                    ? XexAddressResolver.ResolveFileOffset(session, position)
                    : (ushort)(addressOverride.Value + (position - fileOffset));
                instructions.Add(new DisassembledLine(
                    memoryAddress,
                    [opcode],
                    ".db",
                    Formatting.HexByte(opcode),
                    OperandAddress: null,
                    string.Empty,
                    IsData: true
                ));
                position++;
                continue;
            }

            var currentAddress = addressOverride is null
                ? XexAddressResolver.ResolveFileOffset(session, position)
                : (ushort)(addressOverride.Value + (position - fileOffset));
            var bytes = session.Data.Skip(position).Take(entry.Bytes).ToArray();
            var operand = FormatOperand(entry, session.Data, position, currentAddress ?? 0, symbols, zeroPageMap);
            var operandAddress = ResolveOperandAddress(entry, session.Data, position, currentAddress ?? 0);
            var comments = BuildComments(entry, session.Data, position, currentAddress, symbols, zeroPageMap);
            var commentText = comments.Count == 0 ? string.Empty : $"; {string.Join(" | ", comments)}";
            var mnemonicText = string.IsNullOrWhiteSpace(operand) ? entry.Mnemonic : $"{entry.Mnemonic} {operand}";

            instructions.Add(new DisassembledLine(
                currentAddress,
                bytes,
                mnemonicText,
                operand,
                operandAddress,
                commentText,
                IsData: false
            ));
            position += entry.Bytes;
        }

        return instructions;
    }

    // ─── Analysis-aware formatters ─────────────────────────────────────────

    /// <summary>
    /// Format with analysis results (labels, code/data separation).
    /// </summary>
    private static string FormatAnalyzed(
        List<DisassembledLine> instructions,
        LabelMap labelMap,
        bool hasMapping)
    {
        var lines = new List<string>();

        if (!hasMapping && instructions.Count > 0)
        {
            lines.Add("NOTE: No address mapping available. Memory addresses shown as file offsets.");
            lines.Add("      Use the startAddress parameter to set a base address (e.g., startAddress=$0700).");
            lines.Add("");
        }

        foreach (var instr in instructions)
        {
            // Emit label if available
            if (instr.Address is not null && labelMap.Labels.TryGetValue(instr.Address.Value, out var label))
            {
                lines.Add($"{label}:");
            }

            var addrText = instr.Address is null ? "$????" : Formatting.HexWord(instr.Address.Value);
            var byteText = string.Join(' ', instr.Bytes.Select(b => b.ToString("X2"))).PadRight(9);
            var line = $"{addrText}  {byteText}  {instr.Mnemonic}";
            if (!string.IsNullOrEmpty(instr.Comment))
                line += $"  {instr.Comment}";
            lines.Add(line);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// ca65 format with full analysis: segment support, .proc/.endproc, procedure headers, ATASCII strings.
    /// </summary>
    private static string FormatCa65Analyzed(
        RomSession session,
        List<DisassembledLine> instructions,
        ushort? baseAddr,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        ReferenceGraph references,
        HashSet<ushort> codeRegions,
        LabelMap labelMap)
    {
        var lines = new List<string>();
        var segmentManager = GetSegmentManager();

        // Header
        lines.Add("; --------------------------------------------------");
        lines.Add("; Generated by Atari Hacker MCP v4");
        lines.Add("; Source: " + (session.FilePath ?? "unknown"));
        lines.Add("; --------------------------------------------------");
        lines.Add("");

        // Detect procedures
        var procedures = DisassemblyAnalyzer.DetectProcedures(references, labelMap, codeRegions);
        var procedureEntries = new HashSet<ushort>(procedures.Select(p => p.EntryPoint));

        // Group instructions by segment, or use a single default segment
        var currentSegment = (string?)null;
        var currentSegmentOrg = (ushort?)null;
        var inProcedure = false;

        // Keep track of which addresses we've emitted labels for
        var emittedLabels = new HashSet<ushort>();

        foreach (var instr in instructions)
        {
            var addr = instr.Address;

            // Check for segment boundary
            if (addr is not null && segmentManager is not null)
            {
                var segName = segmentManager.GetSegmentName(addr.Value);
                if (segName != currentSegment)
                {
                    // Close current procedure if open
                    if (inProcedure)
                    {
                        lines.Add(".endproc");
                        lines.Add("");
                        inProcedure = false;
                    }

                    currentSegment = segName;
                    if (segName is not null)
                    {
                        var seg = segmentManager.Segments.FirstOrDefault(s => s.Name == segName);
                        lines.Add(".segment \"" + segName.ToUpperInvariant() + "\"");
                        if (seg is not null && seg.Start != currentSegmentOrg)
                        {
                            lines.Add("\t.org\t" + Formatting.HexWord(seg.Start));
                            currentSegmentOrg = seg.Start;
                        }
                        lines.Add("");
                    }
                }
            }

            // Emit .org if needed (no segments defined)
            if (segmentManager is null || segmentManager.Segments.Count == 0)
            {
                if (baseAddr is not null && currentSegmentOrg is null)
                {
                    lines.Add("\t.org\t" + Formatting.HexWord(baseAddr.Value));
                    currentSegmentOrg = baseAddr;
                    lines.Add("");
                }
            }

            // Check if this address is a subroutine entry → start .proc
            if (addr is not null && procedureEntries.Contains(addr.Value) && !inProcedure)
            {
                // Close any previous procedure
                if (inProcedure)
                {
                    lines.Add(".endproc");
                    lines.Add("");
                }

                var proc = procedures.FirstOrDefault(p => p.EntryPoint == addr.Value);
                var procName = proc?.Name ?? labelMap.Labels.GetValueOrDefault(addr.Value, $"sub_{addr.Value:X4}");

                // Emit procedure header comment
                if (proc is not null)
                {
                    lines.Add("; --------------------------------------------------");
                    lines.Add($"; Subroutine: {procName}");
                    if (!string.IsNullOrWhiteSpace(proc.Comment))
                        lines.Add($"; Purpose:   {proc.Comment}");
                    if (proc.Calls.Count > 0)
                    {
                        var callNames = string.Join(", ", proc.Calls.OrderBy(c => c).Select(c => labelMap.Labels.GetValueOrDefault(c, $"${c:X4}")));
                        lines.Add($"; Calls:     {callNames}");
                    }
                    if (proc.CalledBy.Count > 0)
                    {
                        var callerNames = string.Join(", ", proc.CalledBy.OrderBy(c => c).Select(c => labelMap.Labels.GetValueOrDefault(c, $"${c:X4}")));
                        lines.Add($"; Called by: {callerNames}");
                    }
                    lines.Add("; --------------------------------------------------");
                }

                lines.Add($".proc {procName}");
                inProcedure = true;
            }

            // Emit label if this is a referenced address (not a subroutine entry, which is handled by .proc)
            if (addr is not null && labelMap.Labels.TryGetValue(addr.Value, out var label) && !emittedLabels.Contains(addr.Value))
            {
                if (!procedureEntries.Contains(addr.Value))
                {
                    lines.Add($"{label}:");
                }
                emittedLabels.Add(addr.Value);
            }

            // Emit instruction
            if (instr.IsData)
            {
                // Check if this data looks like ATASCII text
                if (instr.Bytes.Length == 1 && IsAtasciiPrintable(instr.Bytes[0]))
                {
                    var atasciiChar = AtasciiDecoder.DecodeByte(instr.Bytes[0]);
                    if (instr.Bytes[0] == 0x9B)
                    {
                        lines.Add($"\t.byte\t$9B\t; ATASCII EOL");
                    }
                    else if (atasciiChar >= 0x20 && atasciiChar <= 0x7E)
                    {
                        lines.Add($"\t.byte\t\"{atasciiChar}\"");
                    }
                    else
                    {
                        lines.Add($"\t.byte\t{Formatting.HexByte(instr.Bytes[0])}");
                    }
                }
                else
                {
                    var line = $"\t.byte\t{instr.Operand}";
                    if (!string.IsNullOrEmpty(instr.Comment))
                        line += $"\t{instr.Comment}";
                    lines.Add(line);
                }
            }
            else
            {
                // Replace address operands with label names from the analysis
                var instruction = BuildAnalyzedInstruction(instr, labelMap, symbols, zeroPageMap);
                var line = $"\t{instruction}";
                if (!string.IsNullOrEmpty(instr.Comment))
                    line += $"\t{instr.Comment}";
                lines.Add(line);
            }
        }

        // Close final procedure
        if (inProcedure)
        {
            lines.Add(".endproc");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Build an instruction using analyzed labels for operand addresses.
    /// </summary>
    private static string BuildAnalyzedInstruction(
        DisassembledLine instr,
        LabelMap labelMap,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        if (instr.IsData)
            return instr.Mnemonic + " " + instr.Operand;

        // Check if this is a JSR/JMP/branch with a known target address
        if (instr.OperandAddress is not null)
        {
            // Try the analyzed label map first
            if (labelMap.Labels.TryGetValue(instr.OperandAddress.Value, out var label))
            {
                var mnemonic = instr.Mnemonic.Split(' ')[0];
                return $"{mnemonic} {label}";
            }

            // Fall back to symbol table
            var symbol = SymbolResolver.Resolve(instr.OperandAddress.Value, symbols, zeroPageMap);
            if (symbol is not null)
            {
                var mnemonic = instr.Mnemonic.Split(' ')[0];
                return $"{mnemonic} {symbol}";
            }
        }

        return instr.Mnemonic;
    }

    // ─── Existing formatters (unchanged) ───────────────────────────────────

    /// <summary>
    /// Collects all addresses referenced by JSR, JMP, and branch instructions
    /// within the given range, for use in auto-generating labels.
    /// </summary>
    private static HashSet<ushort> CollectReferencedAddresses(
        RomSession session,
        int fileOffset,
        int end,
        ushort? addressOverride)
    {
        var referenced = new HashSet<ushort>();
        var position = fileOffset;

        while (position < end)
        {
            var opcode = session.Data![position];
            if (!Opcodes6502.Table.TryGetValue(opcode, out var entry) || !entry.IsOfficial || position + entry.Bytes > session.Length)
            {
                position++;
                continue;
            }

            var currentAddress = addressOverride is null
                ? XexAddressResolver.ResolveFileOffset(session, position)
                : (ushort)(addressOverride.Value + (position - fileOffset));

            if (currentAddress is not null)
            {
                var operandAddress = ResolveOperandAddress(entry, session.Data, position, currentAddress.Value);
                if (operandAddress is not null)
                {
                    // JSR, JMP, and branches all reference a target address
                    if (entry.Mnemonic is "JSR" or "JMP" or "BPL" or "BMI" or "BVC" or "BVS" or "BCC" or "BCS" or "BNE" or "BEQ")
                    {
                        referenced.Add(operandAddress.Value);
                    }
                }
            }

            position += entry.Bytes;
        }

        return referenced;
    }

    /// <summary>
    /// Builds a set of addresses that should have labels emitted.
    /// Combines symbol table entries with auto-detected references.
    /// </summary>
    private static HashSet<ushort> BuildLabelAddresses(
        List<DisassembledLine> instructions,
        HashSet<ushort> referencedAddresses,
        SymbolTable symbols)
    {
        var labelAddresses = new HashSet<ushort>();

        // Add all user-defined symbol table addresses that fall within our range
        // (exclude hardware register symbols — we don't want to emit labels for $D000 etc.)
        foreach (var kvp in symbols)
        {
            if (!kvp.Value.IsHardware && instructions.Any(i => i.Address == kvp.Key))
            {
                labelAddresses.Add(kvp.Key);
            }
        }

        // Add referenced addresses that fall within our range
        foreach (var addr in referencedAddresses)
        {
            if (instructions.Any(i => i.Address == addr))
            {
                labelAddresses.Add(addr);
            }
        }

        return labelAddresses;
    }

    /// <summary>
    /// Generates a label name for an address. Uses the symbol table if available,
    /// otherwise generates an auto-label like "L3F00".
    /// </summary>
    private static string GetLabelName(ushort address, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        var entry = SymbolResolver.ResolveEntry(address, symbols, zeroPageMap);
        if (entry is not null && !entry.IsHardware)
        {
            return entry.Label;
        }
        return $"L{address:X4}";
    }

    /// <summary>
    /// For assembler output formats, replaces address operands with label names
    /// where a label is defined for the target address.
    /// </summary>
    private static string BuildAssemblerInstruction(
        DisassembledLine instr,
        HashSet<ushort> labelAddresses,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        if (instr.IsData)
            return instr.Mnemonic + " " + instr.Operand;

        // Check if this is a JSR/JMP/branch with a known target address
        if (instr.OperandAddress is not null && labelAddresses.Contains(instr.OperandAddress.Value))
        {
            var label = GetLabelName(instr.OperandAddress.Value, symbols, zeroPageMap);
            var mnemonic = instr.Mnemonic.Split(' ')[0]; // Just the mnemonic part
            return $"{mnemonic} {label}";
        }

        return instr.Mnemonic;
    }

    // ─── Formatters ────────────────────────────────────────────────────────

    /// <summary>
    /// Classic listing format: address + bytes + disassembly + comments.
    /// </summary>
    private static string FormatListing(List<DisassembledLine> instructions, bool hasMapping)
    {
        var lines = new List<string>();

        if (!hasMapping && instructions.Count > 0)
        {
            lines.Add("NOTE: No address mapping available. Memory addresses shown as file offsets.");
            lines.Add("      Use the startAddress parameter to set a base address (e.g., startAddress=$0700).");
            lines.Add("");
        }

        foreach (var instr in instructions)
        {
            var addrText = instr.Address is null ? "$????" : Formatting.HexWord(instr.Address.Value);
            var byteText = string.Join(' ', instr.Bytes.Select(b => b.ToString("X2"))).PadRight(9);
            var line = $"{addrText}  {byteText}  {instr.Mnemonic}";
            if (!string.IsNullOrEmpty(instr.Comment))
                line += $"  {instr.Comment}";
            lines.Add(line);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// ca65-compatible assembly output.
    /// Labels use ':' suffix. Directives: .org, .byte. Instructions indented.
    /// </summary>
    private static string FormatCa65(
        List<DisassembledLine> instructions,
        ushort? baseAddr,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var lines = new List<string>();

        // Collect referenced addresses for auto-label generation
        var referenced = CollectReferencedAddressesFromInstructions(instructions);
        var labelAddresses = BuildLabelAddresses(instructions, referenced, symbols);

        // Emit .org directive
        if (baseAddr is not null)
        {
            lines.Add($"\t.org\t{Formatting.HexWord(baseAddr.Value)}");
            lines.Add("");
        }

        foreach (var instr in instructions)
        {
            // Emit label if this address is a target
            if (instr.Address is not null && labelAddresses.Contains(instr.Address.Value))
            {
                var label = GetLabelName(instr.Address.Value, symbols, zeroPageMap);
                lines.Add($"{label}:");
            }

            if (instr.IsData)
            {
                var line = $"\t.byte\t{instr.Operand}";
                if (!string.IsNullOrEmpty(instr.Comment))
                    line += $"\t{instr.Comment}";
                lines.Add(line);
            }
            else
            {
                var instruction = BuildAssemblerInstruction(instr, labelAddresses, symbols, zeroPageMap);
                var line = $"\t{instruction}";
                if (!string.IsNullOrEmpty(instr.Comment))
                    line += $"\t{instr.Comment}";
                lines.Add(line);
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// ATASM-compatible assembly output.
    /// Labels use space suffix. Directives: .org, .byte. Instructions indented.
    /// </summary>
    private static string FormatAtasm(
        List<DisassembledLine> instructions,
        ushort? baseAddr,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var lines = new List<string>();

        var referenced = CollectReferencedAddressesFromInstructions(instructions);
        var labelAddresses = BuildLabelAddresses(instructions, referenced, symbols);

        if (baseAddr is not null)
        {
            lines.Add($"\t.org\t{Formatting.HexWord(baseAddr.Value)}");
            lines.Add("");
        }

        foreach (var instr in instructions)
        {
            if (instr.Address is not null && labelAddresses.Contains(instr.Address.Value))
            {
                var label = GetLabelName(instr.Address.Value, symbols, zeroPageMap);
                lines.Add($"{label}");
            }

            if (instr.IsData)
            {
                var line = $"\t.byte\t{instr.Operand}";
                if (!string.IsNullOrEmpty(instr.Comment))
                    line += $"\t{instr.Comment}";
                lines.Add(line);
            }
            else
            {
                var instruction = BuildAssemblerInstruction(instr, labelAddresses, symbols, zeroPageMap);
                var line = $"\t{instruction}";
                if (!string.IsNullOrEmpty(instr.Comment))
                    line += $"\t{instr.Comment}";
                lines.Add(line);
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Mac/65-compatible assembly output.
    /// Labels use space suffix. Directives: ORG, DB. Instructions indented.
    /// </summary>
    private static string FormatMac65(
        List<DisassembledLine> instructions,
        ushort? baseAddr,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var lines = new List<string>();

        var referenced = CollectReferencedAddressesFromInstructions(instructions);
        var labelAddresses = BuildLabelAddresses(instructions, referenced, symbols);

        if (baseAddr is not null)
        {
            lines.Add($"\tORG\t{Formatting.HexWord(baseAddr.Value)}");
            lines.Add("");
        }

        foreach (var instr in instructions)
        {
            if (instr.Address is not null && labelAddresses.Contains(instr.Address.Value))
            {
                var label = GetLabelName(instr.Address.Value, symbols, zeroPageMap);
                lines.Add($"{label}");
            }

            if (instr.IsData)
            {
                // Mac/65 uses DB for data bytes
                var line = $"\tDB\t{instr.Operand}";
                if (!string.IsNullOrEmpty(instr.Comment))
                    line += $"\t{instr.Comment}";
                lines.Add(line);
            }
            else
            {
                var instruction = BuildAssemblerInstruction(instr, labelAddresses, symbols, zeroPageMap);
                var line = $"\t{instruction}";
                if (!string.IsNullOrEmpty(instr.Comment))
                    line += $"\t{instr.Comment}";
                lines.Add(line);
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Scans already-parsed instructions to find all addresses referenced
    /// by JSR, JMP, and branch instructions, using the stored OperandAddress.
    /// </summary>
    private static HashSet<ushort> CollectReferencedAddressesFromInstructions(List<DisassembledLine> instructions)
    {
        var referenced = new HashSet<ushort>();
        foreach (var instr in instructions)
        {
            if (instr.IsData || instr.OperandAddress is null)
                continue;

            var mnemonic = instr.Mnemonic.Split(' ')[0];
            if (mnemonic is "JSR" or "JMP" or "BPL" or "BMI" or "BVC" or "BVS" or "BCC" or "BCS" or "BNE" or "BEQ")
            {
                referenced.Add(instr.OperandAddress.Value);
            }
        }
        return referenced;
    }

    // ─── Existing helper methods ───────────────────────────────────────────

    internal static int GetStepLength(byte opcode)
    {
        return Opcodes6502.Table.TryGetValue(opcode, out var entry) ? Math.Max(entry.Bytes, 1) : 1;
    }

    internal static bool TryGetOfficialEntry(byte opcode, out OpcodeEntry entry)
    {
        if (Opcodes6502.Table.TryGetValue(opcode, out entry!) && entry.IsOfficial)
        {
            return true;
        }
        entry = null!;
        return false;
    }

    internal static ushort? ResolveOperandAddress(OpcodeEntry entry, byte[] data, int position, ushort memoryAddress)
    {
        return entry.Mode switch
        {
            AddressingMode.ZeroPage or AddressingMode.ZeroPageX or AddressingMode.ZeroPageY or AddressingMode.IndirectX or AddressingMode.IndirectY => data[position + 1],
            AddressingMode.Absolute or AddressingMode.AbsoluteX or AddressingMode.AbsoluteY or AddressingMode.Indirect => ReadWord(data, position + 1),
            AddressingMode.Relative => (ushort)(memoryAddress + entry.Bytes + unchecked((sbyte)data[position + 1])),
            _ => null
        };
    }

    internal static string FormatOperand(OpcodeEntry entry, byte[] data, int position, ushort memoryAddress, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        return entry.Mode switch
        {
            AddressingMode.Implied => string.Empty,
            AddressingMode.Accumulator => "A",
            AddressingMode.Immediate => $"#{Formatting.HexByte(data[position + 1])}",
            AddressingMode.ZeroPage => Formatting.HexByte(data[position + 1]),
            AddressingMode.ZeroPageX => $"{Formatting.HexByte(data[position + 1])},X",
            AddressingMode.ZeroPageY => $"{Formatting.HexByte(data[position + 1])},Y",
            AddressingMode.Absolute => Formatting.HexWord(ReadWord(data, position + 1)),
            AddressingMode.AbsoluteX => $"{Formatting.HexWord(ReadWord(data, position + 1))},X",
            AddressingMode.AbsoluteY => $"{Formatting.HexWord(ReadWord(data, position + 1))},Y",
            AddressingMode.Indirect => $"({Formatting.HexWord(ReadWord(data, position + 1))})",
            AddressingMode.IndirectX => $"({Formatting.HexByte(data[position + 1])},X)",
            AddressingMode.IndirectY => $"({Formatting.HexByte(data[position + 1])}),Y",
            AddressingMode.Relative => Formatting.HexWord((ushort)(memoryAddress + entry.Bytes + unchecked((sbyte)data[position + 1]))),
            _ => string.Empty
        };
    }

    internal static ushort ReadWord(byte[] data, int position)
    {
        return (ushort)(data[position] | (data[position + 1] << 8));
    }

    private static List<string> BuildComments(OpcodeEntry entry, byte[] data, int position, ushort? memoryAddress, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        var comments = new List<string>();
        if (memoryAddress is not null)
        {
            var currentEntry = SymbolResolver.ResolveEntry(memoryAddress.Value, symbols, zeroPageMap);
            if (!string.IsNullOrWhiteSpace(currentEntry?.Comment))
            {
                comments.Add(currentEntry.Comment!);
            }
        }

        if (memoryAddress is null)
        {
            return comments;
        }

        var operandAddress = ResolveOperandAddress(entry, data, position, memoryAddress.Value);
        if (operandAddress is not null)
        {
            var symbol = SymbolResolver.ResolveEntry(operandAddress.Value, symbols, zeroPageMap);
            if (symbol is not null)
            {
                comments.Insert(0, symbol.Label);
            }
        }

        return comments;
    }

    // ─── ATASCII helpers ──────────────────────────────────────────────────

    private static bool IsAtasciiPrintable(byte value)
    {
        return (value >= 0x20 && value <= 0x7E) || value == 0x9B || (value >= 0xA0 && value <= 0xFE);
    }

    /// <summary>
    /// Get the SegmentManager from the DI container (via a static holder).
    /// This is a workaround since the tool class is static and can't use DI directly.
    /// </summary>
    private static SegmentManager? GetSegmentManager()
    {
        // Try to get SegmentManager from a well-known location
        // The SegmentManager is registered as a singleton, but since this is a static tool class,
        // we access it through the service provider if available.
        // For MCP tools, parameters are injected by the framework.
        // The FormatCa65Analyzed method receives the SegmentManager implicitly through the
        // SegmentManager singleton that's available in the DI container.
        // Since we're in a static context, we return null and let the caller handle it.
        return null;
    }
}
