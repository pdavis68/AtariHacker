using System.Text;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Analysis;

/// <summary>
/// Call graph construction and formatting.
/// </summary>
public static class CallGraph
{
    /// <summary>
    /// Build a call graph from the reference graph, starting from the given entry point(s).
    /// </summary>
    public static Dictionary<ushort, HashSet<ushort>> BuildCallGraph(
        ReferenceGraph references,
        ushort? startAddress = null,
        int maxDepth = 10)
    {
        var graph = new Dictionary<ushort, HashSet<ushort>>();
        var worklist = new Queue<(ushort addr, int depth)>();
        var visited = new HashSet<ushort>();

        // Seed with entry points
        if (startAddress is not null)
        {
            worklist.Enqueue((startAddress.Value, 0));
        }
        else
        {
            // Use all subroutine entries as roots
            foreach (var entry in references.SubroutineEntries)
            {
                worklist.Enqueue((entry, 0));
                break; // Start with just the first one
            }

            // If no subroutines, use code entry points
            if (worklist.Count == 0)
            {
                foreach (var entry in references.CodeEntryPoints)
                {
                    worklist.Enqueue((entry, 0));
                    break;
                }
            }
        }

        while (worklist.Count > 0)
        {
            var (current, depth) = worklist.Dequeue();
            if (!visited.Add(current) || depth > maxDepth)
            {
                continue;
            }

            if (!graph.ContainsKey(current))
            {
                graph[current] = new HashSet<ushort>();
            }

            // Find all JSR targets from this address's range
            // (Simplified: we mark targets from the reference graph)
            // In a full implementation, we'd trace the code at this address
        }

        return graph;
    }

    /// <summary>
    /// Build a more detailed call graph by re-scanning the data for JSR instructions
    /// within the address range of each subroutine.
    /// </summary>
    public static Dictionary<ushort, HashSet<ushort>> BuildCallGraphFromData(
        byte[] data,
        ReferenceGraph references,
        IReadOnlyList<Atari.XexSegment>? segments,
        ushort? baseAddress,
        ushort? startAddress = null,
        int maxDepth = 10)
    {
        var graph = new Dictionary<ushort, HashSet<ushort>>();

        // For each subroutine entry, find JSR targets within its estimated range
        var entries = references.SubroutineEntries.OrderBy(a => a).ToList();
        if (entries.Count == 0)
        {
            // Fall back to code entry points
            entries = references.CodeEntryPoints.OrderBy(a => a).ToList();
        }

        // If no entries and we have a start address, use that
        if (entries.Count == 0 && startAddress is not null)
        {
            entries = new List<ushort> { startAddress.Value };
        }

        foreach (var entry in entries)
        {
            var calls = new HashSet<ushort>();

            // Find file offset for this address
            var fileOffset = ResolveFileOffset(segments, baseAddress, entry, data.Length);
            if (fileOffset is null) continue;

            // Estimate end: next entry point or end of data
            var nextEntry = entries.SkipWhile(e => e <= entry).SkipWhile(e => e == entry).FirstOrDefault();
            int endOffset;
            if (nextEntry > entry)
            {
                endOffset = ResolveFileOffset(segments, baseAddress, nextEntry, data.Length)
                    ?? data.Length;
            }
            else
            {
                endOffset = data.Length;
            }

            endOffset = Math.Min(endOffset, data.Length);

            // Scan for JSR instructions in this range
            var pos = fileOffset.Value;
            while (pos < endOffset)
            {
                var opcode = data[pos];
                if (!Atari.Opcodes6502.Table.TryGetValue(opcode, out var opEntry) || !opEntry.IsOfficial)
                {
                    pos++;
                    continue;
                }

                if (pos + opEntry.Bytes > data.Length) break;

                if (opEntry.Mnemonic == "JSR" && opEntry.Mode == Atari.AddressingMode.Absolute)
                {
                    var target = (ushort)(data[pos + 1] | (data[pos + 2] << 8));
                    calls.Add(target);
                }

                pos += opEntry.Bytes;
            }

            graph[entry] = calls;
        }

        return graph;
    }

    /// <summary>
    /// Format the call graph as a Mermaid flowchart.
    /// </summary>
    public static string FormatMermaid(
        Dictionary<ushort, HashSet<ushort>> graph,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        foreach (var kvp in graph)
        {
            var callerName = GetLabel(kvp.Key, symbols, zeroPageMap);

            if (kvp.Value.Count == 0)
            {
                sb.AppendLine($"    {callerName}([\"{callerName}\"])");
            }
            else
            {
                sb.AppendLine($"    {callerName}[\"{callerName}\"]");
                foreach (var callee in kvp.Value)
                {
                    var calleeName = GetLabel(callee, symbols, zeroPageMap);
                    sb.AppendLine($"    {callerName} --> {calleeName}");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format the call graph as an indented text tree.
    /// </summary>
    public static string FormatText(
        Dictionary<ushort, HashSet<ushort>> graph,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var sb = new StringBuilder();
        var visited = new HashSet<ushort>();

        void PrintNode(ushort address, int indent)
        {
            if (!visited.Add(address)) return;
            var name = GetLabel(address, symbols, zeroPageMap);
            sb.AppendLine($"{new string(' ', indent * 2)}{name}");

            if (graph.TryGetValue(address, out var calls))
            {
                foreach (var callee in calls)
                {
                    PrintNode(callee, indent + 1);
                }
            }
        }

        foreach (var root in graph.Keys)
        {
            if (!visited.Contains(root))
            {
                PrintNode(root, 0);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format the call graph as CSV rows (caller, callee, depth, address).
    /// </summary>
    public static string FormatCsv(
        Dictionary<ushort, HashSet<ushort>> graph,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var rows = new List<string[]>();
        foreach (var kvp in graph)
        {
            var callerLabel = GetLabel(kvp.Key, symbols, zeroPageMap);
            if (kvp.Value.Count == 0)
            {
                rows.Add(new[] { callerLabel, "", "0", Formatting.HexWord(kvp.Key) });
            }
            else
            {
                foreach (var callee in kvp.Value)
                {
                    var calleeLabel = GetLabel(callee, symbols, zeroPageMap);
                    rows.Add(new[] { callerLabel, calleeLabel, "1", Formatting.HexWord(kvp.Key) });
                }
            }
        }

        var headers = new[] { "caller", "callee", "depth", "address" };
        return OutputFormatter.FormatCsv(headers, rows.ToArray());
    }

    /// <summary>
    /// Format the call graph as TSV rows.
    /// </summary>
    public static string FormatTsv(
        Dictionary<ushort, HashSet<ushort>> graph,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var rows = new List<string[]>();
        foreach (var kvp in graph)
        {
            var callerLabel = GetLabel(kvp.Key, symbols, zeroPageMap);
            if (kvp.Value.Count == 0)
            {
                rows.Add(new[] { callerLabel, "", "0", Formatting.HexWord(kvp.Key) });
            }
            else
            {
                foreach (var callee in kvp.Value)
                {
                    var calleeLabel = GetLabel(callee, symbols, zeroPageMap);
                    rows.Add(new[] { callerLabel, calleeLabel, "1", Formatting.HexWord(kvp.Key) });
                }
            }
        }

        var headers = new[] { "caller", "callee", "depth", "address" };
        return OutputFormatter.FormatTsv(headers, rows.ToArray());
    }

    /// <summary>
    /// Format the call graph as key=value pairs.
    /// </summary>
    public static string FormatKv(
        Dictionary<ushort, HashSet<ushort>> graph,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var rows = new List<string[]>();
        foreach (var kvp in graph)
        {
            var callerLabel = GetLabel(kvp.Key, symbols, zeroPageMap);
            if (kvp.Value.Count == 0)
            {
                rows.Add(new[] { callerLabel, "", "0", Formatting.HexWord(kvp.Key) });
            }
            else
            {
                foreach (var callee in kvp.Value)
                {
                    var calleeLabel = GetLabel(callee, symbols, zeroPageMap);
                    rows.Add(new[] { callerLabel, calleeLabel, "1", Formatting.HexWord(kvp.Key) });
                }
            }
        }

        var keys = new[] { "caller", "callee", "depth", "address" };
        return OutputFormatter.FormatKv(keys, rows.ToArray());
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static string GetLabel(ushort address, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        var resolved = SymbolResolver.Resolve(address, symbols, zeroPageMap);
        if (resolved is not null)
        {
            return resolved;
        }

        // Check for subroutine label pattern
        if (symbols.ContainsKey(address))
        {
            return symbols[address].Label;
        }

        return $"{address:X4}";
    }

    private static int? ResolveFileOffset(IReadOnlyList<Atari.XexSegment>? segments, ushort? baseAddress, ushort memoryAddress, int dataLength)
    {
        if (segments is { Count: > 0 })
        {
            return Atari.XexParser.MemoryAddressToFileOffset(segments, memoryAddress);
        }
        if (baseAddress is not null)
        {
            var offset = memoryAddress - baseAddress.Value;
            return offset >= 0 && offset < dataLength ? offset : null;
        }
        return memoryAddress < dataLength ? memoryAddress : null;
    }
}