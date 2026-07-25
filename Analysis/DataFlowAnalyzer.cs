using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Analysis;

/// <summary>
/// Static analysis engine for tracing data flow through memory.
/// Given a target address, it can trace forward (from writes to reads)
/// or backward (from reads to writes) through the instruction stream.
/// </summary>
public static class DataFlowAnalyzer
{
    /// <summary>
    /// Maximum number of instructions to scan during any single trace operation.
    /// </summary>
    public const int DefaultInstructionBudget = 1000;

    /// <summary>
    /// Maximum call depth for tracing through JSR instructions.
    /// </summary>
    public const int DefaultCallDepth = 5;

    // ─── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Trace forward from instructions that write to the target address,
    /// following execution flow to find instructions that read from it.
    /// </summary>
    public static DataFlowResult TraceForward(
        RomSession session,
        ushort targetAddress,
        int maxDepth = 10,
        int instructionBudget = DefaultInstructionBudget,
        List<ProcedureInfo>? procedures = null)
    {
        var data = session.Data;
        if (data is null || data.Length == 0)
            return EmptyResult(targetAddress);

        // 1. Find all references to the target address
        var allRefs = FindAllReferences(session, targetAddress);

        // 2. Separate into writes and reads
        var writes = allRefs.Where(r => r.Access is AccessType.Write or AccessType.ReadWrite).ToList();
        var reads = allRefs.Where(r => r.Access is AccessType.Read or AccessType.ReadWrite).ToList();

        // 3. For each write, walk forward and find which reads are reachable
        var chain = new List<DataFlowLink>();
        var visitedPaths = new HashSet<(ushort, ushort)>(); // (from, to)

        foreach (var write in writes)
        {
            var startFileOffset = FindFileOffset(session, write.Address);
            if (startFileOffset is null)
                continue;

            // Walk forward from the instruction AFTER the write
            var nextOffset = startFileOffset.Value + GetInstructionLength(data, startFileOffset.Value);
            if (nextOffset >= data.Length)
                continue;

            var walker = new ForwardWalker(
                data, session, targetAddress, reads, maxDepth, instructionBudget, procedures);
            var reachableReads = walker.Walk(nextOffset, write.Address, 0, new HashSet<ushort>());

            foreach (var result in reachableReads)
            {
                var key = (write.Address, result.Address);
                if (visitedPaths.Add(key))
                {
                    chain.Add(new DataFlowLink(
                        write.Address,
                        write.Access,
                        result.Address,
                        AccessType.Read,
                        result.Path,
                        result.Count));
                }
            }
        }

        return new DataFlowResult(targetAddress, writes, reads, chain);
    }

    /// <summary>
    /// Trace backward from instructions that read from the target address,
    /// scanning backward through the instruction stream to find writes.
    /// </summary>
    public static DataFlowResult TraceBackward(
        RomSession session,
        ushort targetAddress,
        int maxDepth = 10,
        int instructionBudget = DefaultInstructionBudget,
        List<ProcedureInfo>? procedures = null)
    {
        var data = session.Data;
        if (data is null || data.Length == 0)
            return EmptyResult(targetAddress);

        // 1. Find all references to the target address
        var allRefs = FindAllReferences(session, targetAddress);

        // 2. Separate into writes and reads
        var writes = allRefs.Where(r => r.Access is AccessType.Write or AccessType.ReadWrite).ToList();
        var reads = allRefs.Where(r => r.Access is AccessType.Read or AccessType.ReadWrite).ToList();

        // 3. For backward trace, we scan from the start of each segment
        //    up to each read instruction, tracking instruction boundaries,
        //    then check which writes are reachable from the read going backward.
        var chain = new List<DataFlowLink>();
        var visitedPaths = new HashSet<(ushort, ushort)>(); // (to, from)

        // Build a map of file offsets to instruction boundaries for the whole ROM
        var instructionMap = BuildInstructionMap(data, session.Segments);

        foreach (var read in reads)
        {
            var readFileOffset = FindFileOffset(session, read.Address);
            if (readFileOffset is null)
                continue;

            // Get all instruction boundaries before this read
            var precedingInstructions = instructionMap
                .Where(kvp => kvp.Value < readFileOffset.Value)
                .OrderByDescending(kvp => kvp.Value)
                .ToList();

            var budget = instructionBudget;
            foreach (var (instrAddr, instrOffset) in precedingInstructions)
            {
                if (budget <= 0)
                    break;

                // Check if this instruction writes to the target
                var entry = GetOpcodeEntry(data, instrOffset);
                if (entry is null)
                    continue;

                var memoryAddress = XexAddressResolver.ResolveFileOffset(session, instrOffset)
                    ?? (ushort)Math.Min(instrOffset, 0xFFFF);
                var operandAddress = DisassemblerTool.ResolveOperandAddress(
                    entry, data, instrOffset, memoryAddress);

                var isWrite = entry.Mnemonic is "STA" or "STX" or "STY"
                    or "INC" or "DEC" or "ASL" or "LSR" or "ROL" or "ROR";
                var matchesTarget = operandAddress == targetAddress
                    || (targetAddress <= 0xFF && operandAddress == (byte)targetAddress);

                if (isWrite && matchesTarget)
                {
                    var key = (instrAddr, read.Address);
                    if (visitedPaths.Add(key))
                    {
                        var instrCount = CountInstructionsBetween(
                            instructionMap, instrAddr, read.Address);
                        chain.Add(new DataFlowLink(
                            instrAddr,
                            XRefTool.ClassifyAccess(entry.Mnemonic),
                            read.Address,
                            read.Access,
                            $"backward from {Formatting.HexWord(read.Address)}",
                            instrCount));
                    }
                }

                budget--;
            }
        }

        return new DataFlowResult(targetAddress, writes, reads, chain);
    }

    /// <summary>
    /// Find all references to a target address across the entire ROM.
    /// Returns structured XRefEntry records with access type classification.
    /// </summary>
    public static List<XRefEntry> FindAllReferences(
        RomSession session, ushort targetAddress)
    {
        var rows = new List<XRefEntry>();
        var data = session.Data;
        if (data is null)
            return rows;

        foreach (var start in GetScanStarts(session))
        {
            var position = start;
            var segmentEnd = GetScanEnd(session, start);
            while (position < segmentEnd && position < session.Length)
            {
                var opcode = data[position];
                if (!DisassemblerTool.TryGetOfficialEntry(opcode, out var entry)
                    || position + entry.Bytes > data.Length)
                {
                    position++;
                    continue;
                }

                var memoryAddress = XexAddressResolver.ResolveFileOffset(session, position)
                    ?? (ushort)Math.Min(position, 0xFFFF);
                var operandAddress = DisassemblerTool.ResolveOperandAddress(
                    entry, data, position, memoryAddress);
                var matches = operandAddress == targetAddress
                    || (targetAddress <= 0xFF && operandAddress == (byte)targetAddress);

                if (matches)
                {
                    var operand = FormatOperandSimple(entry, data, position, memoryAddress);
                    var access = XRefTool.ClassifyAccess(entry.Mnemonic);
                    rows.Add(new XRefEntry(
                        memoryAddress,
                        entry.Mnemonic,
                        operand,
                        access,
                        null,  // procedure — resolved externally
                        null)); // segment — resolved externally
                }

                position += entry.Bytes;
            }
        }

        return rows;
    }

    // ─── Forward Walker ───────────────────────────────────────────────────

    /// <summary>
    /// Recursive forward walker that follows execution flow from a starting
    /// file offset, collecting all read instructions that reference the target.
    /// </summary>
    private sealed class ForwardWalker
    {
        private readonly byte[] _data;
        private readonly RomSession _session;
        private readonly ushort _targetAddress;
        private readonly List<XRefEntry> _reads;
        private readonly int _maxDepth;
        private readonly int _instructionBudget;
        private readonly List<ProcedureInfo>? _procedures;
        private int _budgetRemaining;

        public ForwardWalker(
            byte[] data,
            RomSession session,
            ushort targetAddress,
            List<XRefEntry> reads,
            int maxDepth,
            int instructionBudget,
            List<ProcedureInfo>? procedures = null)
        {
            _data = data;
            _session = session;
            _targetAddress = targetAddress;
            _reads = reads;
            _maxDepth = maxDepth;
            _instructionBudget = instructionBudget;
            _procedures = procedures;
            _budgetRemaining = instructionBudget;
        }

        /// <summary>
        /// Check if an address is at a procedure entry point (boundary).
        /// When procedures are available, we stop tracing at procedure boundaries
        /// to keep the trace focused on the current context.
        /// </summary>
        private bool IsProcedureBoundary(ushort address)
        {
            if (_procedures is null || _procedures.Count == 0)
                return false;

            return _procedures.Any(p => p.EntryPoint == address);
        }

        public List<(ushort Address, string Path, int Count)> Walk(
            int fileOffset, ushort fromAddress, int depth, HashSet<ushort> visited)
        {
            var results = new List<(ushort, string, int)>();
            var position = fileOffset;
            var instrCount = 0;
            var path = Formatting.HexWord(fromAddress);

            while (position < _data.Length && _budgetRemaining > 0)
            {
                var opcode = _data[position];
                if (!DisassemblerTool.TryGetOfficialEntry(opcode, out var entry)
                    || position + entry.Bytes > _data.Length)
                {
                    break;
                }

                _budgetRemaining--;
                instrCount++;

                var currentAddress = XexAddressResolver.ResolveFileOffset(_session, position)
                    ?? (ushort)Math.Min(position, 0xFFFF);

                // Check if this instruction is a read of the target
                var operandAddress = DisassemblerTool.ResolveOperandAddress(
                    entry, _data, position, currentAddress);
                var isRead = entry.Mnemonic is "LDA" or "LDX" or "LDY" or "BIT"
                    or "CMP" or "ADC" or "SBC" or "AND" or "ORA" or "EOR"
                    or "CPX" or "CPY";
                var matchesTarget = operandAddress == _targetAddress
                    || (_targetAddress <= 0xFF && operandAddress == (byte)_targetAddress);

                if (isRead && matchesTarget)
                {
                    results.Add((currentAddress, $"{path} → {Formatting.HexWord(currentAddress)}", instrCount));
                }

                // Handle control flow instructions
                switch (entry.Mnemonic)
                {
                    case "RTS" or "RTI" or "BRK":
                        return results; // Stop at return/break

                    // Also stop at procedure boundaries when procedures are available
                    default:
                        if (IsProcedureBoundary(currentAddress))
                        {
                            return results;
                        }
                        break;

                    case "JMP":
                    {
                        if (entry.Mode == AddressingMode.Absolute)
                        {
                            var target = DisassemblerTool.ReadWord(_data, position + 1);
                            var targetOffset = FindFileOffset(_session, target);
                            if (targetOffset is not null && !visited.Contains(target))
                            {
                                visited.Add(target);
                                var subWalker = new ForwardWalker(
                                    _data, _session, _targetAddress, _reads,
                                    _maxDepth, _budgetRemaining, _procedures);
                                var subResults = subWalker.Walk(
                                    targetOffset.Value, currentAddress, depth, visited);
                                results.AddRange(subResults);
                                _budgetRemaining = subWalker._budgetRemaining;
                            }
                        }
                        // For indirect JMP, we can't statically trace
                        return results;
                    }

                    case "JSR":
                    {
                        if (depth < _maxDepth)
                        {
                            var target = DisassemblerTool.ReadWord(_data, position + 1);
                            var targetOffset = FindFileOffset(_session, target);
                            if (targetOffset is not null && !visited.Contains(target))
                            {
                                visited.Add(target);
                                var subWalker = new ForwardWalker(
                                    _data, _session, _targetAddress, _reads,
                                    _maxDepth, _budgetRemaining, _procedures);
                                var subResults = subWalker.Walk(
                                    targetOffset.Value, currentAddress, depth + 1, visited);
                                results.AddRange(subResults);
                                _budgetRemaining = subWalker._budgetRemaining;
                            }
                        }
                        // Continue after JSR (fall-through)
                        position += entry.Bytes;
                        continue;
                    }

                    // Conditional branches: follow both taken and not-taken paths
                    case "BPL" or "BMI" or "BVC" or "BVS" or "BCC" or "BCS" or "BNE" or "BEQ":
                    {
                        // Follow the taken branch
                        if (entry.Bytes >= 2)
                        {
                            var branchTarget = (ushort)(currentAddress + entry.Bytes
                                + unchecked((sbyte)_data[position + 1]));
                            var branchOffset = FindFileOffset(_session, branchTarget);
                            if (branchOffset is not null && !visited.Contains(branchTarget))
                            {
                                visited.Add(branchTarget);
                                var branchWalker = new ForwardWalker(
                                    _data, _session, _targetAddress, _reads,
                                    _maxDepth, _budgetRemaining);
                                var branchResults = branchWalker.Walk(
                                    branchOffset.Value, currentAddress, depth, visited);
                                results.AddRange(branchResults);
                                _budgetRemaining = branchWalker._budgetRemaining;
                            }
                        }
                        // Continue fall-through (not taken)
                        position += entry.Bytes;
                        continue;
                    }
                }

                position += entry.Bytes;
            }

            return results;
        }
    }

    // ─── Instruction Map ──────────────────────────────────────────────────

    /// <summary>
    /// Build a map of memory address → file offset for all instruction boundaries.
    /// </summary>
    private static Dictionary<ushort, int> BuildInstructionMap(
        byte[] data, IReadOnlyList<XexSegment>? segments)
    {
        var map = new Dictionary<ushort, int>();

        var position = 0;
        while (position < data.Length)
        {
            var opcode = data[position];
            if (!DisassemblerTool.TryGetOfficialEntry(opcode, out var entry)
                || position + entry.Bytes > data.Length)
            {
                position++;
                continue;
            }

            // Resolve the memory address for this instruction start
            var memoryAddress = segments is { Count: > 0 }
                ? XexParser.FileOffsetToMemoryAddress(segments, position)
                : (ushort?)null;
            if (memoryAddress is null)
            {
                position += entry.Bytes;
                continue;
            }

            map[memoryAddress.Value] = position;
            position += entry.Bytes;
        }

        return map;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static int GetInstructionLength(byte[] data, int offset)
    {
        if (offset >= data.Length)
            return 1;

        if (!DisassemblerTool.TryGetOfficialEntry(data[offset], out var entry))
            return 1;

        return entry.Bytes;
    }

    private static OpcodeEntry? GetOpcodeEntry(byte[] data, int offset)
    {
        if (offset >= data.Length)
            return null;

        if (!DisassemblerTool.TryGetOfficialEntry(data[offset], out var entry))
            return null;

        return entry;
    }

    private static int? FindFileOffset(RomSession session, ushort memoryAddress)
    {
        if (session.Segments is { Count: > 0 })
        {
            return XexParser.MemoryAddressToFileOffset(session.Segments, memoryAddress);
        }

        if (session.BaseAddress is not null)
        {
            var offset = memoryAddress - session.BaseAddress.Value;
            return offset >= 0 && offset < session.Length ? offset : null;
        }

        return memoryAddress < session.Length ? memoryAddress : null;
    }

    private static int CountInstructionsBetween(
        Dictionary<ushort, int> instructionMap, ushort fromAddr, ushort toAddr)
    {
        var count = 0;
        foreach (var addr in instructionMap.Keys)
        {
            if (addr > fromAddr && addr < toAddr)
                count++;
        }
        return count;
    }

    private static DataFlowResult EmptyResult(ushort targetAddress)
    {
        return new DataFlowResult(
            targetAddress,
            new List<XRefEntry>(),
            new List<XRefEntry>(),
            new List<DataFlowLink>());
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

    /// <summary>
    /// Format an operand without requiring symbol table context.
    /// Used internally by FindAllReferences when symbol tables aren't available.
    /// </summary>
    private static string FormatOperandSimple(OpcodeEntry entry, byte[] data, int position, ushort memoryAddress)
    {
        if (entry.Bytes <= 1)
            return string.Empty;

        switch (entry.Mode)
        {
            case AddressingMode.Immediate:
                return $"#${data[position + 1]:X2}";

            case AddressingMode.ZeroPage:
                return $"${data[position + 1]:X2}";

            case AddressingMode.ZeroPageX:
                return $"${data[position + 1]:X2},X";

            case AddressingMode.ZeroPageY:
                return $"${data[position + 1]:X2},Y";

            case AddressingMode.Absolute:
            {
                var value = (ushort)(data[position + 1] | (data[position + 2] << 8));
                return $"${value:X4}";
            }

            case AddressingMode.AbsoluteX:
            {
                var value = (ushort)(data[position + 1] | (data[position + 2] << 8));
                return $"${value:X4},X";
            }

            case AddressingMode.AbsoluteY:
            {
                var value = (ushort)(data[position + 1] | (data[position + 2] << 8));
                return $"${value:X4},Y";
            }

            case AddressingMode.Indirect:
            {
                var value = (ushort)(data[position + 1] | (data[position + 2] << 8));
                return $"(${value:X4})";
            }

            case AddressingMode.IndirectX:
                return $"(${data[position + 1]:X2},X)";

            case AddressingMode.IndirectY:
                return $"(${data[position + 1]:X2}),Y";

            case AddressingMode.Relative:
            {
                var offset = unchecked((sbyte)data[position + 1]);
                var target = (ushort)(memoryAddress + entry.Bytes + offset);
                return $"${target:X4}";
            }

            case AddressingMode.Accumulator:
                return "A";

            default:
                return string.Empty;
        }
    }
}