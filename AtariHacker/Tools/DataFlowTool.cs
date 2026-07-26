using System.Text;
using AtariHacker.Analysis;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

/// <summary>
/// CLI tool for memory access tracing (trace-access command).
/// Statically traces data flow through memory by following read/write chains.
/// </summary>
public static class DataFlowTool
{
    /// <summary>
    /// Trace data flow for a target address.
    /// </summary>
    public static string TraceAccess(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string address,
        string direction = "forward",
        int depth = 10,
        int budget = DataFlowAnalyzer.DefaultInstructionBudget,
        string format = "text",
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
            var targetSymbol = SymbolResolver.Resolve(target, symbols, zeroPageMap);

            DataFlowResult result;
            if (direction.Equals("backward", StringComparison.OrdinalIgnoreCase)
                || direction.Equals("back", StringComparison.OrdinalIgnoreCase))
            {
                result = DataFlowAnalyzer.TraceBackward(session, target, depth, budget, procedures);
            }
            else
            {
                result = DataFlowAnalyzer.TraceForward(session, target, depth, budget, procedures);
            }

            // Resolve procedure/segment context for writes and reads
            var writesWithContext = result.Writes
                .Select(w => w with
                {
                    Procedure = ResolveProcedure(w.Address, procedures),
                    Segment = segmentManager?.GetSegmentName(w.Address)
                })
                .ToList();

            var readsWithContext = result.Reads
                .Select(r => r with
                {
                    Procedure = ResolveProcedure(r.Address, procedures),
                    Segment = segmentManager?.GetSegmentName(r.Address)
                })
                .ToList();

            return format.ToLowerInvariant() switch
            {
                "csv" => FormatTraceCsv(target, targetSymbol, writesWithContext, readsWithContext, result.Chain),
                "tsv" => FormatTraceTsv(target, targetSymbol, writesWithContext, readsWithContext, result.Chain),
                "kv" => FormatTraceKv(target, targetSymbol, writesWithContext, readsWithContext, result.Chain),
                _ => FormatTraceText(target, targetSymbol, writesWithContext, readsWithContext, result.Chain)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Text Format ──────────────────────────────────────────────────────

    private static string FormatTraceText(
        ushort target,
        string? targetSymbol,
        List<XRefEntry> writes,
        List<XRefEntry> reads,
        List<DataFlowLink> chain)
    {
        var sb = new StringBuilder();
        var header = targetSymbol is not null
            ? $"{Formatting.HexWord(target)} ({targetSymbol})"
            : Formatting.HexWord(target);
        sb.AppendLine($"Data flow for {header}:");

        // Writes section
        sb.AppendLine();
        sb.AppendLine("  Written by:");
        if (writes.Count == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var w in writes)
            {
                var ctx = FormatContext(w.Procedure, w.Segment);
                sb.AppendLine($"    {Formatting.HexWord(w.Address)}: [{AccessSymbol(w.Access)}] {w.Mnemonic} {w.Operand}{ctx}");
            }
        }

        // Reads section
        sb.AppendLine();
        sb.AppendLine("  Read by:");
        if (reads.Count == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var r in reads)
            {
                var ctx = FormatContext(r.Procedure, r.Segment);
                sb.AppendLine($"    {Formatting.HexWord(r.Address)}: [{AccessSymbol(r.Access)}] {r.Mnemonic} {r.Operand}{ctx}");
            }
        }

        // Data flow chain
        sb.AppendLine();
        sb.AppendLine("  Data flow chain:");
        if (chain.Count == 0)
        {
            sb.AppendLine("    (no direct data flow paths found)");
        }
        else
        {
            foreach (var link in chain)
            {
                sb.AppendLine($"    {Formatting.HexWord(link.FromAddress)} ({AccessSymbol(link.FromAccess)}) ──→ {Formatting.HexWord(link.ToAddress)} ({AccessSymbol(link.ToAccess)})  [{link.Path}, {link.InstructionCount} instr]");
            }
        }

        return sb.ToString();
    }

    // ─── Structured Formats ───────────────────────────────────────────────

    private static string FormatTraceCsv(
        ushort target, string? targetSymbol,
        List<XRefEntry> writes, List<XRefEntry> reads,
        List<DataFlowLink> chain)
    {
        var headers = new[] { "type", "address", "mnemonic", "operand", "access_type", "procedure", "segment" };
        var rows = new List<string[]>();

        foreach (var w in writes)
        {
            rows.Add(new[] { "write", Formatting.HexWord(w.Address), w.Mnemonic, w.Operand,
                w.Access.ToString().ToLowerInvariant(), w.Procedure ?? "", w.Segment ?? "" });
        }
        foreach (var r in reads)
        {
            rows.Add(new[] { "read", Formatting.HexWord(r.Address), r.Mnemonic, r.Operand,
                r.Access.ToString().ToLowerInvariant(), r.Procedure ?? "", r.Segment ?? "" });
        }

        return OutputFormatter.FormatCsv(headers, rows.ToArray());
    }

    private static string FormatTraceTsv(
        ushort target, string? targetSymbol,
        List<XRefEntry> writes, List<XRefEntry> reads,
        List<DataFlowLink> chain)
    {
        var headers = new[] { "type", "address", "mnemonic", "operand", "access_type", "procedure", "segment" };
        var rows = new List<string[]>();

        foreach (var w in writes)
        {
            rows.Add(new[] { "write", Formatting.HexWord(w.Address), w.Mnemonic, w.Operand,
                w.Access.ToString().ToLowerInvariant(), w.Procedure ?? "", w.Segment ?? "" });
        }
        foreach (var r in reads)
        {
            rows.Add(new[] { "read", Formatting.HexWord(r.Address), r.Mnemonic, r.Operand,
                r.Access.ToString().ToLowerInvariant(), r.Procedure ?? "", r.Segment ?? "" });
        }

        return OutputFormatter.FormatTsv(headers, rows.ToArray());
    }

    private static string FormatTraceKv(
        ushort target, string? targetSymbol,
        List<XRefEntry> writes, List<XRefEntry> reads,
        List<DataFlowLink> chain)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"target={Formatting.HexWord(target)}");
        if (targetSymbol is not null)
            sb.AppendLine($"target_symbol={targetSymbol}");
        sb.AppendLine($"write_count={writes.Count}");
        sb.AppendLine($"read_count={reads.Count}");
        sb.AppendLine($"chain_count={chain.Count}");

        for (var i = 0; i < writes.Count; i++)
        {
            var w = writes[i];
            sb.AppendLine($"write_{i}_address={Formatting.HexWord(w.Address)}");
            sb.AppendLine($"write_{i}_mnemonic={w.Mnemonic}");
            sb.AppendLine($"write_{i}_operand={w.Operand}");
        }

        for (var i = 0; i < reads.Count; i++)
        {
            var r = reads[i];
            sb.AppendLine($"read_{i}_address={Formatting.HexWord(r.Address)}");
            sb.AppendLine($"read_{i}_mnemonic={r.Mnemonic}");
            sb.AppendLine($"read_{i}_operand={r.Operand}");
        }

        return sb.ToString();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static string AccessSymbol(AccessType access) => access switch
    {
        AccessType.Read => "R",
        AccessType.Write => "W",
        AccessType.ReadWrite => "RW",
        AccessType.Execute => "X",
        _ => "?"
    };

    private static string FormatContext(string? procedure, string? segment)
    {
        var parts = new List<string>();
        if (procedure is not null)
            parts.Add($"in {procedure}");
        if (segment is not null)
            parts.Add($"segment: {segment}");
        return parts.Count > 0 ? $"  [{string.Join(", ", parts)}]" : string.Empty;
    }

    private static string? ResolveProcedure(ushort address, List<ProcedureInfo>? procedures)
    {
        if (procedures is null || procedures.Count == 0)
            return null;

        foreach (var proc in procedures)
        {
            if (proc.EstimatedEnd is not null)
            {
                if (address >= proc.EntryPoint && address <= proc.EstimatedEnd.Value)
                    return proc.Name;
            }
            else if (address == proc.EntryPoint)
            {
                return proc.Name;
            }
        }

        foreach (var proc in procedures)
        {
            if (address == proc.EntryPoint)
                return proc.Name;
        }

        return null;
    }
}