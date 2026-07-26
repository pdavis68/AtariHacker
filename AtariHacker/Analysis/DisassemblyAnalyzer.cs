using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Analysis;

/// <summary>
/// Represents the complete reference graph built during Pass 1 analysis.
/// </summary>
public sealed record ReferenceGraph(
    // Targets of JSR instructions → subroutine entry points
    HashSet<ushort> SubroutineEntries,
    // Targets of JMP absolute → jump targets
    HashSet<ushort> JumpTargets,
    // Targets of Bxx instructions → branch targets
    HashSet<ushort> BranchTargets,
    // Targets of JMP (indirect) → potential code entry points
    HashSet<ushort> IndirectJumpTargets,
    // Absolute addresses in LDA/STA/ADC/SBC/etc. operands → data references
    HashSet<ushort> AbsoluteDataReferences,
    // Zero-page addresses used with (zp),Y indirect mode → indirect data references
    HashSet<byte> IndirectDataReferences,
    // Addresses that are definitely code entry points
    HashSet<ushort> CodeEntryPoints,
    // Addresses that are definitely data
    HashSet<ushort> DataAddresses,
    // All instruction addresses (for code region detection)
    HashSet<ushort> InstructionAddresses)
{
    public static ReferenceGraph Empty { get; } = new(
        new HashSet<ushort>(),
        new HashSet<ushort>(),
        new HashSet<ushort>(),
        new HashSet<ushort>(),
        new HashSet<ushort>(),
        new HashSet<byte>(),
        new HashSet<ushort>(),
        new HashSet<ushort>(),
        new HashSet<ushort>());
}

/// <summary>
/// Information about a detected procedure/subroutine.
/// </summary>
public sealed record ProcedureInfo(
    ushort EntryPoint,
    string Name,
    string? Comment,
    ushort? EstimatedEnd,
    HashSet<ushort> Calls,
    HashSet<ushort> CalledBy,
    HashSet<ushort> ReadsFrom,
    HashSet<ushort> WritesTo);

/// <summary>
/// Maps addresses to labels and comments after Pass 3 analysis.
/// </summary>
public sealed record LabelMap(
    IReadOnlyDictionary<ushort, string> Labels,
    IReadOnlyDictionary<ushort, string> Comments);

/// <summary>
/// Multi-pass disassembly analyzer that builds a reference graph,
/// traces code regions, and generates meaningful labels.
/// </summary>
public static class DisassemblyAnalyzer
{
    // Mnemonics that indicate data references when used with absolute addressing
    private static readonly HashSet<string> DataRefMnemonics = new()
    {
        "LDA", "STA", "ADC", "SBC", "CMP", "AND", "ORA", "EOR",
        "LDX", "STX", "LDY", "STY",
        "INC", "DEC", "BIT",
        "ROL", "ROR", "ASL", "LSR",
        "CPX", "CPY"
    };

    /// <summary>
    /// Pass 1: Scan all instruction boundaries across the entire loaded ROM
    /// and build a reference graph.
    /// </summary>
    public static ReferenceGraph Analyze(
        byte[] data,
        IReadOnlyList<XexSegment>? segments,
        ushort? baseAddress)
    {
        if (data is null || data.Length == 0)
        {
            return ReferenceGraph.Empty;
        }

        var subroutineEntries = new HashSet<ushort>();
        var jumpTargets = new HashSet<ushort>();
        var branchTargets = new HashSet<ushort>();
        var indirectJumpTargets = new HashSet<ushort>();
        var absoluteDataReferences = new HashSet<ushort>();
        var indirectDataReferences = new HashSet<byte>();
        var codeEntryPoints = new HashSet<ushort>();
        var dataAddresses = new HashSet<ushort>();
        var instructionAddresses = new HashSet<ushort>();

        var position = 0;
        while (position < data.Length)
        {
            var opcode = data[position];
            if (!Opcodes6502.Table.TryGetValue(opcode, out var entry) || !entry.IsOfficial)
            {
                // Illegal opcode: advance by 1 byte, treated as potential data
                position++;
                continue;
            }

            if (position + entry.Bytes > data.Length)
            {
                break;
            }

            var memoryAddress = ResolveAddress(segments, baseAddress, position);
            if (memoryAddress is null)
            {
                position += entry.Bytes;
                continue;
            }

            var addr = memoryAddress.Value;
            instructionAddresses.Add(addr);

            switch (entry.Mnemonic)
            {
                case "JSR":
                {
                    var target = ReadWord(data, position + 1);
                    subroutineEntries.Add(target);
                    codeEntryPoints.Add(target);
                    break;
                }
                case "JMP" when entry.Mode == AddressingMode.Absolute:
                {
                    var target = ReadWord(data, position + 1);
                    jumpTargets.Add(target);
                    codeEntryPoints.Add(target);
                    break;
                }
                case "JMP" when entry.Mode == AddressingMode.Indirect:
                {
                    var target = ReadWord(data, position + 1);
                    indirectJumpTargets.Add(target);
                    codeEntryPoints.Add(target);
                    break;
                }
                case "BPL" or "BMI" or "BVC" or "BVS" or "BCC" or "BCS" or "BNE" or "BEQ":
                {
                    var target = (ushort)(addr + entry.Bytes + unchecked((sbyte)data[position + 1]));
                    branchTargets.Add(target);
                    codeEntryPoints.Add(target);
                    break;
                }
            }

            // Check for absolute data references
            if (entry.Mode is AddressingMode.Absolute or AddressingMode.AbsoluteX or AddressingMode.AbsoluteY
                && DataRefMnemonics.Contains(entry.Mnemonic))
            {
                var target = ReadWord(data, position + 1);
                absoluteDataReferences.Add(target);
            }

            // Check for indirect data references (zp),Y mode
            if (entry.Mode == AddressingMode.IndirectY
                && entry.Mnemonic is "LDA" or "STA")
            {
                var zp = data[position + 1];
                indirectDataReferences.Add(zp);
            }

            position += entry.Bytes;
        }

        // Detect Atari boot sector header (6 bytes at the start of the binary)
        // Pattern: [boot_flag] [sector_count] [load_addr_lo] [load_addr_hi] [init_addr_lo] [init_addr_hi]
        // Boot flag is typically $00 (continue) or $D0 (stop/run)
        if (data.Length >= 6 && (data[0] == 0x00 || data[0] == 0xD0))
        {
            for (var i = 0; i < 6; i++)
            {
                var hdrAddr = ResolveAddress(segments, baseAddress, i);
                if (hdrAddr is not null)
                {
                    absoluteDataReferences.Add(hdrAddr.Value);
                }
            }
        }

        return new ReferenceGraph(
            subroutineEntries,
            jumpTargets,
            branchTargets,
            indirectJumpTargets,
            absoluteDataReferences,
            indirectDataReferences,
            codeEntryPoints,
            dataAddresses,
            instructionAddresses);
    }

    /// <summary>
    /// Pass 2: Starting from each known code entry point, trace execution flow
    /// to determine which bytes are actually code and which are data.
    /// </summary>
    public static (HashSet<ushort> CodeRegions, HashSet<ushort> DataRegions) TraceCodeRegions(
        byte[] data,
        ReferenceGraph references,
        IReadOnlyList<XexSegment>? segments,
        ushort? baseAddress,
        int maxInstructions = 100_000)
    {
        var codeRegions = new HashSet<ushort>();
        var dataRegions = new HashSet<ushort>();
        var visited = new HashSet<ushort>();
        var worklist = new Queue<ushort>(references.CodeEntryPoints);
        var instructionBudget = maxInstructions;

        // Always treat absolute data references as data
        foreach (var addr in references.AbsoluteDataReferences)
        {
            dataRegions.Add(addr);
        }

        // Ensure the worklist has at least one non-data address to trace from.
        // This handles the case where all code entry points are in data regions
        // (e.g. boot sector headers where JMP/JSR/branch targets land inside the header).
        // Scan from the beginning to find the first non-data address.
        if (worklist.Count == 0 || worklist.All(a => references.AbsoluteDataReferences.Contains(a)))
        {
            var bootstrapFound = false;
            for (var i = 0; i < data.Length; i++)
            {
                var addr = ResolveAddress(segments, baseAddress, i);
                if (addr is not null && !references.AbsoluteDataReferences.Contains(addr.Value))
                {
                    if (!bootstrapFound)
                    {
                        // Clear the worklist and add only the first non-data address
                        worklist.Clear();
                        worklist.Enqueue(addr.Value);
                        bootstrapFound = true;
                        break;
                    }
                }
            }
        }

        while (worklist.Count > 0 && instructionBudget > 0)
        {
            var startAddr = worklist.Dequeue();

            // Skip addresses that are known absolute data references
            // (e.g. boot sector headers, identified data tables)
            if (references.AbsoluteDataReferences.Contains(startAddr))
            {
                continue;
            }

            if (!visited.Add(startAddr))
            {
                continue;
            }

            // Find the file offset for this address
            var fileOffset = ResolveFileOffset(segments, baseAddress, startAddr, data.Length);
            if (fileOffset is null)
            {
                continue;
            }

            var position = fileOffset.Value;
            while (position < data.Length && instructionBudget > 0)
            {
                var opcode = data[position];
                if (!Opcodes6502.Table.TryGetValue(opcode, out var entry) || !entry.IsOfficial)
                {
                    // Illegal opcode: stop this path
                    break;
                }

                if (position + entry.Bytes > data.Length)
                {
                    break;
                }

                var currentAddr = ResolveAddress(segments, baseAddress, position);
                if (currentAddr is null)
                {
                    break;
                }

                // Mark all bytes of this instruction as code
                for (var i = 0; i < entry.Bytes; i++)
                {
                    var byteAddr = ResolveAddress(segments, baseAddress, position + i);
                    if (byteAddr is not null)
                    {
                        codeRegions.Add(byteAddr.Value);
                        // If this byte was previously marked as data, it's actually code
                        dataRegions.Remove(byteAddr.Value);
                    }
                }

                instructionBudget--;

                switch (entry.Mnemonic)
                {
                    case "JSR":
                    {
                        var target = ReadWord(data, position + 1);
                        if (!visited.Contains(target))
                        {
                            worklist.Enqueue(target);
                        }
                        // Continue sequential execution after JSR
                        position += entry.Bytes;
                        break;
                    }
                    case "JMP" when entry.Mode == AddressingMode.Absolute:
                    {
                        var target = ReadWord(data, position + 1);
                        if (!visited.Contains(target))
                        {
                            worklist.Enqueue(target);
                        }
                        // Stop current sequential path (JMP is unconditional)
                        position = data.Length;
                        break;
                    }
                    case "JMP" when entry.Mode == AddressingMode.Indirect:
                    {
                        // Indirect jump: add target as potential entry, stop current path
                        var target = ReadWord(data, position + 1);
                        if (!visited.Contains(target))
                        {
                            worklist.Enqueue(target);
                        }
                        position = data.Length;
                        break;
                    }
                    case "BPL" or "BMI" or "BVC" or "BVS" or "BCC" or "BCS" or "BNE" or "BEQ":
                    {
                        var target = (ushort)(currentAddr.Value + entry.Bytes + unchecked((sbyte)data[position + 1]));
                        if (!visited.Contains(target))
                        {
                            worklist.Enqueue(target);
                        }
                        // Continue sequential execution (fall-through)
                        position += entry.Bytes;
                        break;
                    }
                    case "RTS" or "RTI" or "BRK":
                    {
                        // Stop current sequential path
                        position = data.Length;
                        break;
                    }
                    default:
                    {
                        position += entry.Bytes;
                        break;
                    }
                }
            }
        }

        // Mark unreachable bytes as data
        var totalBytes = data.Length;
        for (var i = 0; i < totalBytes; i++)
        {
            var addr = ResolveAddress(segments, baseAddress, i);
            if (addr is not null && !codeRegions.Contains(addr.Value))
            {
                dataRegions.Add(addr.Value);
            }
        }

        return (codeRegions, dataRegions);
    }

    /// <summary>
    /// Pass 3: Generate meaningful labels based on the reference graph
    /// and code/data regions.
    /// </summary>
    public static LabelMap GenerateLabels(
        ReferenceGraph references,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        HashSet<ushort> codeRegions)
    {
        var labels = new Dictionary<ushort, string>();
        var comments = new Dictionary<ushort, string>();

        // Helper to add a label with priority checking
        void AddLabel(ushort address, string label, string? comment = null)
        {
            // Check if a user-defined symbol already exists (highest priority)
            if (symbols.TryGetValue(address, out var existing) && existing.IsUserDefined)
            {
                labels[address] = existing.Label;
                if (!string.IsNullOrWhiteSpace(existing.Comment))
                {
                    comments[address] = existing.Comment!;
                }
                return;
            }

            // Don't overwrite an existing label with a lower-priority one
            if (labels.ContainsKey(address))
            {
                return;
            }

            labels[address] = label;
            if (comment is not null)
            {
                comments[address] = comment;
            }
        }

        // 1. User-defined symbols (already checked in AddLabel)
        //    These are handled by the priority check above.

        // 2. Subroutine entries → sub_XXXX
        foreach (var addr in references.SubroutineEntries)
        {
            AddLabel(addr, $"sub_{addr:X4}");
        }

        // 3. Jump targets → jmp_XXXX
        foreach (var addr in references.JumpTargets)
        {
            AddLabel(addr, $"jmp_{addr:X4}");
        }

        // 4. Data references → data_XXXX
        foreach (var addr in references.AbsoluteDataReferences)
        {
            // Only generate data labels for addresses that are in data regions
            // or are not in code regions
            if (!codeRegions.Contains(addr))
            {
                AddLabel(addr, $"data_{addr:X4}");
            }
        }

        // 5. Hardware register symbols (if group enabled)
        foreach (var kvp in symbols)
        {
            if (kvp.Value.IsHardware && symbols.IsSymbolEnabled(kvp.Key))
            {
                labels[kvp.Key] = kvp.Value.Label;
                if (!string.IsNullOrWhiteSpace(kvp.Value.Comment))
                {
                    comments[kvp.Key] = kvp.Value.Comment!;
                }
            }
        }

        // 6. Branch targets → L_XXXX (lowest priority)
        foreach (var addr in references.BranchTargets)
        {
            if (!labels.ContainsKey(addr))
            {
                labels[addr] = $"L_{addr:X4}";
            }
        }

        // Also add labels for indirect jump targets
        foreach (var addr in references.IndirectJumpTargets)
        {
            AddLabel(addr, $"ind_{addr:X4}");
        }

        return new LabelMap(labels, comments);
    }

    /// <summary>
    /// Detect procedure boundaries from the reference graph and code regions.
    /// </summary>
    public static List<ProcedureInfo> DetectProcedures(
        ReferenceGraph references,
        LabelMap labels,
        HashSet<ushort> codeRegions)
    {
        var procedures = new List<ProcedureInfo>();
        var sortedEntries = references.SubroutineEntries
            .Union(references.CodeEntryPoints)
            .OrderBy(a => a)
            .ToList();

        // Build CalledBy map
        var calledBy = new Dictionary<ushort, HashSet<ushort>>();
        foreach (var entry in sortedEntries)
        {
            calledBy[entry] = new HashSet<ushort>();
        }

        // Build Calls and data access maps from the reference graph
        var calls = new Dictionary<ushort, HashSet<ushort>>();
        var readsFrom = new Dictionary<ushort, HashSet<ushort>>();
        var writesTo = new Dictionary<ushort, HashSet<ushort>>();

        // We need to re-scan to build Calls/CalledBy relationships
        // For now, build CalledBy from the reverse of SubroutineEntries
        // (This is simplified - a full implementation would re-scan the ROM)

        foreach (var entry in sortedEntries)
        {
            var name = labels.Labels.TryGetValue(entry, out var label) ? label : $"sub_{entry:X4}";
            var comment = labels.Comments.TryGetValue(entry, out var cmt) ? cmt : null;

            // Estimate end as next entry point or next non-code address
            ushort? estimatedEnd = null;
            var nextIndex = sortedEntries.IndexOf(entry) + 1;
            if (nextIndex < sortedEntries.Count)
            {
                estimatedEnd = (ushort)(sortedEntries[nextIndex] - 1);
            }

            procedures.Add(new ProcedureInfo(
                entry,
                name,
                comment,
                estimatedEnd,
                calls.TryGetValue(entry, out var c) ? c : new HashSet<ushort>(),
                calledBy.TryGetValue(entry, out var cb) ? cb : new HashSet<ushort>(),
                readsFrom.TryGetValue(entry, out var r) ? r : new HashSet<ushort>(),
                writesTo.TryGetValue(entry, out var w) ? w : new HashSet<ushort>()));
        }

        return procedures;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static ushort? ResolveAddress(IReadOnlyList<XexSegment>? segments, ushort? baseAddress, int fileOffset)
    {
        if (segments is { Count: > 0 })
        {
            return XexParser.FileOffsetToMemoryAddress(segments, fileOffset);
        }
        if (baseAddress is not null)
        {
            return (ushort)(baseAddress.Value + fileOffset);
        }
        return fileOffset <= 0xFFFF ? (ushort)fileOffset : null;
    }

    private static int? ResolveFileOffset(IReadOnlyList<XexSegment>? segments, ushort? baseAddress, ushort memoryAddress, int dataLength)
    {
        if (segments is { Count: > 0 })
        {
            return XexParser.MemoryAddressToFileOffset(segments, memoryAddress);
        }
        if (baseAddress is not null)
        {
            var offset = memoryAddress - baseAddress.Value;
            return offset >= 0 && offset < dataLength ? offset : null;
        }
        return memoryAddress < dataLength ? memoryAddress : null;
    }

    internal static ushort ReadWord(byte[] data, int position)
    {
        return (ushort)(data[position] | (data[position + 1] << 8));
    }
}