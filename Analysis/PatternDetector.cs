using System.Text;
using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Analysis;

/// <summary>
/// Data structures for detected control flow patterns.
/// </summary>
public abstract record ControlFlowPattern
{
    public string Type { get; init; } = "";
    public ushort Address { get; init; }
    public double Confidence { get; init; }
}

public sealed record StateMachinePattern : ControlFlowPattern
{
    public ushort StateVariable { get; init; }
    public ushort JumpTable { get; init; }
    public int EntryCount { get; init; }
    public List<ushort> Handlers { get; init; } = [];
}

public sealed record JumpTablePattern : ControlFlowPattern
{
    public ushort TableAddress { get; init; }
    public string JumpType { get; init; } = ""; // "absolute_indexed" or "absolute_indirect_indexed"
    public int EntryCount { get; init; }
    public List<ushort> Targets { get; init; } = [];
}

public sealed record CoroutinePattern : ControlFlowPattern
{
    public List<ushort> Chain { get; init; } = [];
    public bool IsCircular { get; init; }
}

public sealed record InterruptPattern : ControlFlowPattern
{
    public string VectorName { get; init; } = ""; // NMI, RESET, IRQ
    public ushort VectorAddress { get; init; }
}

/// <summary>
/// Scans analyzed code for known control flow patterns:
/// state machines, jump tables, coroutines, and interrupt handlers.
/// </summary>
public static class PatternDetector
{
    // ─── State Machine Detection ───────────────────────────────────────────

    /// <summary>
    /// Detect state machine dispatch loops.
    /// Pattern: LDA state_var → ASL/scale → TAX → JMP (table,X) or JMP table,X
    /// </summary>
    public static List<StateMachinePattern> DetectStateMachines(byte[] data, ReferenceGraph graph)
    {
        var results = new List<StateMachinePattern>();
        if (data is null || data.Length == 0) return results;

        // Scan for JMP instructions that could be dispatch points
        var visited = new HashSet<ushort>();

        for (var pos = 0; pos < data.Length; pos++)
        {
            if (!Opcodes6502.Table.TryGetValue(data[pos], out var entry) || !entry.IsOfficial)
                continue;
            if (pos + entry.Bytes > data.Length) break;

            // Look for JMP (table,X) — absolute indirect indexed
            // or JMP table,X — absolute indexed (not a standard 6502 mode, but checked)
            if (entry.Mnemonic != "JMP") { pos += entry.Bytes - 1; continue; }

            var memoryAddr = ResolveFlatAddress(data, pos);
            if (memoryAddr is null) { pos += entry.Bytes - 1; continue; }
            if (!visited.Add(memoryAddr.Value)) { pos += entry.Bytes - 1; continue; }

            // Check up to 6 instructions before JMP for the state machine pattern
            // LDA state_var, ASL/scale, TAX
            var scanStart = Math.Max(0, pos - 8);
            var scanBytes = new byte[pos - scanStart];
            Array.Copy(data, scanStart, scanBytes, 0, scanBytes.Length);

            ushort? stateVar = null;
            var foundPattern = false;
            var scanPos = scanBytes.Length - 1;

            // Walk backwards from the JMP
            while (scanPos >= 0)
            {
                if (!Opcodes6502.Table.TryGetValue(scanBytes[scanPos], out var scanEntry) || !scanEntry.IsOfficial)
                { scanPos--; continue; }
                if (scanPos + scanEntry.Bytes > scanBytes.Length)
                { scanPos--; continue; }

                var scanMemAddr = ResolveFlatAddress(data, scanStart + scanPos);
                if (scanMemAddr is null) { scanPos--; continue; }

                // Looking for TAX or ASL/ROL/etc before TAX
                if (scanEntry.Mnemonic == "TAX" || scanEntry.Mnemonic == "TXA")
                {
                    // Check preceding instruction for LDA state_var
                    var prevPos = scanPos - 1;
                    while (prevPos >= 0)
                    {
                        if (!Opcodes6502.Table.TryGetValue(scanBytes[prevPos], out var prevEntry) || !prevEntry.IsOfficial)
                        { prevPos--; continue; }
                        if (prevPos + prevEntry.Bytes > scanBytes.Length)
                        { prevPos--; continue; }

                        if (prevEntry.Mnemonic == "LDA")
                        {
                            if (prevEntry.Mode is AddressingMode.ZeroPage or AddressingMode.Absolute)
                            {
                                var lowByte = data[scanStart + prevPos + 1];
                                stateVar = lowByte;
                                if (prevEntry.Mode == AddressingMode.Absolute)
                                    stateVar = (ushort)(lowByte | (data[scanStart + prevPos + 2] << 8));
                                foundPattern = true;
                            }
                            break;
                        }

                        // Also check LDY which is sometimes used instead
                        if (prevEntry.Mnemonic == "LDY")
                        {
                            if (prevEntry.Mode is AddressingMode.ZeroPage or AddressingMode.Absolute)
                            {
                                var lowByte = data[scanStart + prevPos + 1];
                                stateVar = lowByte;
                                if (prevEntry.Mode == AddressingMode.Absolute)
                                    stateVar = (ushort)(lowByte | (data[scanStart + prevPos + 2] << 8));
                                foundPattern = true;
                            }
                            break;
                        }

                        prevPos--;
                    }
                    break;
                }

                // Skip scaling instructions (ASL, ROL, etc.) and NOPs
                if (scanEntry.Mnemonic is "ASL" or "ROL" or "NOP" or "CLC")
                { scanPos--; continue; }

                scanPos--;
            }

            if (foundPattern && stateVar is not null)
            {
                // Try to determine jump table address and enumerate entries
                ushort tableAddr;
                if (entry.Mode == AddressingMode.Indirect)
                {
                    // JMP (table,X) — the operand is the address of the pointer table
                    tableAddr = (ushort)(data[pos + 1] | (data[pos + 2] << 8));
                }
                else if (entry.Mode == AddressingMode.Absolute)
                {
                    tableAddr = (ushort)(data[pos + 1] | (data[pos + 2] << 8));
                }
                else
                {
                    pos += entry.Bytes - 1;
                    continue;
                }

                // Enumerate entries by reading the table
                var handlers = TryEnumerateTable(data, tableAddr, entry.Mode == AddressingMode.Indirect);
                results.Add(new StateMachinePattern
                {
                    Type = "state-machine",
                    Address = memoryAddr.Value,
                    Confidence = 0.85,
                    StateVariable = stateVar.Value,
                    JumpTable = tableAddr,
                    EntryCount = handlers.Count,
                    Handlers = handlers
                });
            }

            pos += entry.Bytes - 1;
        }

        return results;
    }

    // ─── Jump Table Detection ──────────────────────────────────────────────

    /// <summary>
    /// Detect indexed jump patterns and enumerate all targets.
    /// Pattern: JMP (table,X) or any indirect jump with a known table.
    /// </summary>
    public static List<JumpTablePattern> DetectJumpTables(byte[] data, ReferenceGraph graph)
    {
        var results = new List<JumpTablePattern>();
        if (data is null || data.Length == 0) return results;

        var visited = new HashSet<ushort>();

        for (var pos = 0; pos < data.Length; pos++)
        {
            if (!Opcodes6502.Table.TryGetValue(data[pos], out var entry) || !entry.IsOfficial)
                continue;
            if (pos + entry.Bytes > data.Length) break;

            // Look for JMP indirect (0x6C) — JMP (table,X) 
            // Also look for JMP absolute (0x4C) that might be indexed
            if (entry.Mnemonic != "JMP")
            { pos += entry.Bytes - 1; continue; }

            var memoryAddr = ResolveFlatAddress(data, pos);
            if (memoryAddr is null) { pos += entry.Bytes - 1; continue; }
            if (!visited.Add(memoryAddr.Value)) { pos += entry.Bytes - 1; continue; }

            ushort tableAddr;
            string jumpType;
            bool isIndirect;

            if (entry.Mode == AddressingMode.Indirect)
            {
                tableAddr = (ushort)(data[pos + 1] | (data[pos + 2] << 8));
                jumpType = "absolute_indirect_indexed";
                isIndirect = true;
            }
            else if (entry.Mode == AddressingMode.Absolute)
            {
                tableAddr = (ushort)(data[pos + 1] | (data[pos + 2] << 8));
                jumpType = "absolute_indexed";
                isIndirect = false;
            }
            else
            {
                pos += entry.Bytes - 1;
                continue;
            }

            // Check if the preceding instructions index X (state machine pattern)
            // or this is a standalone jump table
            var targets = TryEnumerateTable(data, tableAddr, isIndirect);

            if (targets.Count >= 2)
            {
                results.Add(new JumpTablePattern
                {
                    Type = "jump-table",
                    Address = memoryAddr.Value,
                    Confidence = 0.80,
                    TableAddress = tableAddr,
                    JumpType = jumpType,
                    EntryCount = targets.Count,
                    Targets = targets
                });
            }

            pos += entry.Bytes - 1;
        }

        return results;
    }

    // ─── Coroutine Detection ───────────────────────────────────────────────

    /// <summary>
    /// Detect coroutine patterns where routines chain via JMP instead of JSR/RTS.
    /// </summary>
    public static List<CoroutinePattern> DetectCoroutines(ReferenceGraph graph)
    {
        var results = new List<CoroutinePattern>();
        if (graph is null) return results;

        // Look for JMP chains in the graph
        // A coroutine chain is a sequence of JMP targets where each routine
        // ends with a JMP to the next routine instead of RTS
        var visited = new HashSet<ushort>();

        foreach (var entry in graph.CodeEntryPoints)
        {
            if (visited.Contains(entry)) continue;

            var chain = new List<ushort>();
            var current = entry;
            var isCircular = false;

            while (current != 0 && !visited.Contains(current))
            {
                chain.Add(current);
                visited.Add(current);

                // Check if this address is a JMP target with no RTS in the path
                // For simplicity, check if it's in the jump targets and has a
                // corresponding JMP from another entry
                if (graph.JumpTargets.Contains(current))
                {
                    // Check if it's also a subroutine entry (JSR target)
                    // If it's both, it's likely a regular subroutine, not a coroutine
                    if (graph.SubroutineEntries.Contains(current))
                        break;

                    // Follow the chain — we need to find the JMP source
                    // For now, stop the chain here
                    break;
                }

                break;
            }

            if (chain.Count >= 2)
            {
                // Check for circular dependency
                if (chain.Count > 1 && graph.JumpTargets.Contains(chain[0]) && chain[0] == chain[^1])
                {
                    isCircular = true;
                }

                results.Add(new CoroutinePattern
                {
                    Type = "coroutine",
                    Address = chain[0],
                    Confidence = 0.70,
                    Chain = new List<ushort>(chain),
                    IsCircular = isCircular
                });
            }
        }

        return results;
    }

    // ─── Interrupt Handler Detection ───────────────────────────────────────

    /// <summary>
    /// Detect code reachable only from hardware vectors ($FFFA-$FFFF).
    /// </summary>
    public static List<InterruptPattern> DetectInterruptHandlers(RomSession session)
    {
        var results = new List<InterruptPattern>();
        if (session is null || session.Data is null || session.Data.Length < 6)
            return results;

        var data = session.Data;
        var dataLen = data.Length;

        // Hardware vectors are at the end of the 6502 address space: $FFFA-$FFFF
        // NMI: $FFFA- $FFFB (word)
        // RESET: $FFFC-$FFFD (word)
        // IRQ/BRK: $FFFE-$FFFF (word)
        //
        // We need to find where these are in our data.
        // The vectors are typically at the end of a ROM image.

        // Try to find the vector table by looking at the last 6 bytes
        // or searching for the pattern near the end of the data

        // If the data is loaded at a base address, we can calculate
        var baseOffset = session.BaseAddress is not null
            ? 0
            : (dataLen >= 0x10000 ? 0 : 0x10000 - dataLen);

        // Check if the last 6 bytes contain valid vector addresses
        if (dataLen >= 6)
        {
            // Try the last 6 bytes as the vector table
            TryAddVector(results, data, dataLen - 6, "NMI", 0xFFFA, session);
            TryAddVector(results, data, dataLen - 4, "RESET", 0xFFFC, session);
            TryAddVector(results, data, dataLen - 2, "IRQ", 0xFFFE, session);
        }

        // If we found vectors, also check for the VBI (Vertical Blank Interrupt)
        // handler which is typically at $E000 or similar
        // VBI handlers are often set via SETVBV ($E45C) or directly in the OS ROM

        return results;
    }

    // ─── Combined Detection ─────────────────────────────────────────────────

    /// <summary>
    /// Run all pattern detectors and return formatted results.
    /// </summary>
    public static string DetectAllPatterns(RomSession session, string? typeFilter = null)
    {
        if (session is null || session.Data is null || !session.IsLoaded)
            return "ERROR: No ROM is currently loaded.";

        var data = session.Data;
        var graph = DisassemblyAnalyzer.Analyze(data, session.Segments, session.BaseAddress);

        var sb = new StringBuilder();
        var anyFound = false;

        // 1. State machines
        if (typeFilter is null || typeFilter == "state-machine")
        {
            var stateMachines = DetectStateMachines(data, graph);
            if (stateMachines.Count > 0)
            {
                anyFound = true;
                sb.AppendLine("=== State Machines ===");
                foreach (var sm in stateMachines)
                {
                    sb.AppendLine($"State machine detected at {Formatting.HexWord(sm.Address)}:");
                    sb.AppendLine($"  State variable: {Formatting.HexWord(sm.StateVariable)}");
                    sb.AppendLine($"  Jump table: {Formatting.HexWord(sm.JumpTable)}");
                    sb.AppendLine($"  Entries: {sm.EntryCount}");
                    if (sm.Handlers.Count > 0)
                    {
                        var handlerStrs = sm.Handlers.Take(8).Select(h => Formatting.HexWord(h));
                        sb.AppendLine($"  Handlers: {string.Join(", ", handlerStrs)}{(sm.Handlers.Count > 8 ? ", ..." : "")}");
                    }
                    sb.AppendLine($"  Confidence: {sm.Confidence:P0}");
                    sb.AppendLine();
                }
            }
        }

        // 2. Jump tables
        if (typeFilter is null || typeFilter == "jump-table")
        {
            var jumpTables = DetectJumpTables(data, graph);
            if (jumpTables.Count > 0)
            {
                anyFound = true;
                sb.AppendLine("=== Jump Tables ===");
                foreach (var jt in jumpTables)
                {
                    sb.AppendLine($"Jump table detected at {Formatting.HexWord(jt.Address)}:");
                    sb.AppendLine($"  Type: {jt.JumpType}");
                    sb.AppendLine($"  Table address: {Formatting.HexWord(jt.TableAddress)}");
                    sb.AppendLine($"  Entries: {jt.EntryCount}");
                    if (jt.Targets.Count > 0)
                    {
                        var targetStrs = jt.Targets.Take(12).Select(t => Formatting.HexWord(t));
                        sb.AppendLine($"  Targets: {string.Join(", ", targetStrs)}{(jt.Targets.Count > 12 ? ", ..." : "")}");
                    }
                    sb.AppendLine($"  Confidence: {jt.Confidence:P0}");
                    sb.AppendLine();
                }
            }
        }

        // 3. Coroutines
        if (typeFilter is null || typeFilter == "coroutine")
        {
            var coroutines = DetectCoroutines(graph);
            if (coroutines.Count > 0)
            {
                anyFound = true;
                sb.AppendLine("=== Coroutine Chains ===");
                foreach (var co in coroutines)
                {
                    sb.AppendLine($"Coroutine chain detected:");
                    var chainStrs = co.Chain.Select(c => Formatting.HexWord(c));
                    foreach (var c in co.Chain)
                    {
                        sb.AppendLine($"  {Formatting.HexWord(c)}: JMP ...");
                    }
                    if (co.IsCircular)
                    {
                        sb.AppendLine("  (circular dependency detected)");
                    }
                    sb.AppendLine($"  Confidence: {co.Confidence:P0}");
                    sb.AppendLine();
                }
            }
        }

        // 4. Interrupt handlers
        if (typeFilter is null || typeFilter == "interrupt")
        {
            var handlers = DetectInterruptHandlers(session);
            if (handlers.Count > 0)
            {
                anyFound = true;
                sb.AppendLine("=== Interrupt Handlers ===");
                foreach (var ih in handlers)
                {
                    sb.AppendLine($"Interrupt handler detected at {Formatting.HexWord(ih.Address)}:");
                    sb.AppendLine($"  Type: {ih.VectorName} (from {Formatting.HexWord(ih.VectorAddress)})");
                    sb.AppendLine($"  Confidence: {ih.Confidence:P0}");
                    sb.AppendLine();
                }
            }
        }

        if (!anyFound)
        {
            sb.AppendLine("No control flow patterns detected.");
        }

        return sb.ToString().TrimEnd();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Try to enumerate all entries in a jump table by reading from the table address.
    /// </summary>
    private static List<ushort> TryEnumerateTable(byte[] data, ushort tableAddr, bool isIndirect)
    {
        var targets = new List<ushort>();

        // For indirect tables: JMP (table,X) — table contains pointers (words)
        // For absolute tables: JMP table,X — table contains addresses (words)
        // We need to find where this table is in our data
        // Since we don't know the exact mapping, we try to read from the data
        // assuming it's loaded at the base or at offset 0

        // Try to find the table in the data
        // If the table address is within the data range, read it directly
        // Otherwise, try to find it by searching

        var tableOffset = tableAddr;
        // If the data is smaller than 64KB, the table might be at a negative offset
        // or we need to search for it

        if (tableOffset < data.Length)
        {
            // Read as many entries as we can (up to 256)
            var maxEntries = Math.Min(256, (data.Length - tableOffset) / 2);
            for (var i = 0; i < maxEntries; i++)
            {
                var entryOffset = tableOffset + (i * 2);
                if (entryOffset + 2 > data.Length) break;

                var target = (ushort)(data[entryOffset] | (data[entryOffset + 1] << 8));

                // Sanity check: target should be a reasonable address
                // (not zero, not in zero page, not in hardware registers)
                if (target == 0 || target < 0x0100 || (target >= 0xD000 && target <= 0xD7FF))
                    break;

                // Check for run of consecutive reasonable addresses
                targets.Add(target);
            }
        }
        else
        {
            // The table address is outside the data range
            // Try to find the table by searching for patterns
            // This is a simplified approach
            return targets;
        }

        // Deduplicate
        if (targets.Count > 0)
        {
            targets = targets.Distinct().ToList();
        }

        return targets;
    }

    /// <summary>
    /// Resolve a flat address from a file offset (no segment info).
    /// </summary>
    private static ushort? ResolveFlatAddress(byte[] data, int fileOffset)
    {
        if (fileOffset < 0 || fileOffset >= data.Length)
            return null;
        return fileOffset <= 0xFFFF ? (ushort)fileOffset : null;
    }

    /// <summary>
    /// Try to add a vector entry if the vector points to a valid address.
    /// </summary>
    private static void TryAddVector(
        List<InterruptPattern> results,
        byte[] data,
        int vectorOffset,
        string name,
        ushort vectorAddress,
        RomSession session)
    {
        if (vectorOffset < 0 || vectorOffset + 2 > data.Length)
            return;

        var handlerAddr = (ushort)(data[vectorOffset] | (data[vectorOffset + 1] << 8));

        // Sanity check: handler should be a reasonable address
        if (handlerAddr < 0x0100 || handlerAddr >= 0xFFFA)
            return;

        // Calculate the confidence based on whether the handler address
        // falls within the loaded data range
        var confidence = 0.70;
        var baseAddr = session.BaseAddress ?? 0;
        var maxAddr = baseAddr + data.Length;

        if (handlerAddr >= baseAddr && handlerAddr < maxAddr)
        {
            confidence = 0.90;
        }

        results.Add(new InterruptPattern
        {
            Type = "interrupt-handler",
            Address = handlerAddr,
            Confidence = confidence,
            VectorName = name,
            VectorAddress = vectorAddress
        });
    }
}