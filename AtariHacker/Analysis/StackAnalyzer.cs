using System.Text;
using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.Tools;

namespace AtariHacker.Analysis;

/// <summary>
/// Represents a single stack operation during analysis.
/// </summary>
public sealed record StackFrame(
    ushort Address,
    string Mnemonic,
    string Operand,
    int DepthBefore,
    int DepthAfter,
    string? Warning
);

/// <summary>
/// The result of analyzing stack usage at a given address.
/// </summary>
public sealed record StackAnalysisResult(
    ushort EntryPoint,
    int EntryDepth,
    int MaxDepth,
    int MinDepth,
    int ExitDepth,
    bool IsBalanced,
    List<StackFrame> Operations,
    List<string> Warnings
);

/// <summary>
/// Analyzes stack usage through instruction streams for 6502 code.
/// Tracks virtual stack pointer, handles conditional branches,
/// and detects imbalances.
/// </summary>
public static class StackAnalyzer
{
    // Stack effect of each instruction
    private static readonly Dictionary<string, int> StackEffects = new()
    {
        ["JSR"] = +2,  // Push return address (2 bytes)
        ["RTS"] = -2,  // Pop return address
        ["PHA"] = +1,  // Push A
        ["PLA"] = -1,  // Pop A
        ["PHP"] = +1,  // Push status
        ["PLP"] = -1,  // Pop status
        ["RTI"] = -4,  // Pop status + return address
        ["BRK"] = +4,  // Push return + status (on real hardware)
        ["JMP"] = 0,   // No stack effect (but ends execution path)
    };

    // Conditional branch mnemonics
    private static readonly HashSet<string> BranchMnemonics =
    [
        "BPL", "BMI", "BVC", "BVS", "BCC", "BCS", "BNE", "BEQ"
    ];

    // Instructions that end an execution path
    private static readonly HashSet<string> TerminatingMnemonics =
    [
        "RTS", "RTI", "BRK"
    ];

    /// <summary>
    /// Analyze stack usage starting from a given address.
    /// </summary>
    public static StackAnalysisResult AnalyzeStack(
        byte[] data,
        ushort startAddress,
        int maxInstructions = 500)
    {
        if (data is null || data.Length == 0)
        {
            return new StackAnalysisResult(
                startAddress, 0, 0, 0, 0, true,
                [], ["ERROR: No data to analyze."]);
        }

        // Find the file offset for the start address
        var startOffset = startAddress;
        if (startOffset >= data.Length)
        {
            return new StackAnalysisResult(
                startAddress, 0, 0, 0, 0, true,
                [], [$"ERROR: Address {Formatting.HexWord(startAddress)} is beyond data length."]);
        }

        var operations = new List<StackFrame>();
        var warnings = new List<string>();
        var visited = new HashSet<ushort>();

        // Track the primary path
        var primaryResult = TracePath(
            data, startOffset, startAddress, 2, // Entry depth: 2 (return address on stack)
            maxInstructions, visited, operations, warnings, 0);

        var entryDepth = 2; // Standard entry with return address
        var exitDepth = primaryResult.ExitDepth;
        var maxDepth = primaryResult.MaxDepth;
        var minDepth = primaryResult.MinDepth;
        var isBalanced = entryDepth == exitDepth;

        if (!isBalanced)
        {
            warnings.Add($"Stack depth at exit ({exitDepth}) differs from entry ({entryDepth}): unbalanced");
        }

        return new StackAnalysisResult(
            startAddress,
            entryDepth,
            maxDepth,
            minDepth,
            exitDepth,
            isBalanced,
            operations,
            warnings
        );
    }

    /// <summary>
    /// Trace a single execution path, tracking stack depth.
    /// </summary>
    private static (int ExitDepth, int MaxDepth, int MinDepth) TracePath(
        byte[] data,
        int startOffset,
        ushort startAddress,
        int initialDepth,
        int budget,
        HashSet<ushort> visited,
        List<StackFrame> operations,
        List<string> warnings,
        int recursionDepth)
    {
        var depth = initialDepth;
        var maxDepth = depth;
        var minDepth = depth;
        var position = startOffset;
        var currentAddress = startAddress;
        var budgetRemaining = budget;

        // Track the path to detect loops
        var path = new HashSet<ushort>();

        while (budgetRemaining > 0 && position < data.Length)
        {
            budgetRemaining--;

            var opcode = data[position];
            if (!Opcodes6502.Table.TryGetValue(opcode, out var entry) || !entry.IsOfficial)
            {
                position++;
                continue;
            }

            if (position + entry.Bytes > data.Length)
                break;

            currentAddress = (ushort)position;
            var operand = DisassemblerTool.FormatOperand(
                entry, data, position, currentAddress,
                new State.SymbolTable(), new State.ZeroPageMap());

            // Check for loop
            if (!path.Add(currentAddress))
            {
                operations.Add(new StackFrame(
                    currentAddress, entry.Mnemonic, operand,
                    depth, depth, "[loop detected]"));
                break;
            }

            // Get stack effect
            var effect = GetStackEffect(entry);
            var depthBefore = depth;
            depth += effect;
            var depthAfter = depth;

            // Track min/max
            maxDepth = Math.Max(maxDepth, depth);
            minDepth = Math.Min(minDepth, depth);

            // Check for negative stack (underflow)
            string? frameWarning = null;
            if (depth < 0)
            {
                frameWarning = "stack underflow";
                warnings.Add($"Stack underflow at {Formatting.HexWord(currentAddress)}: depth={depth}");
                depth = 0; // Clamp
            }

            // Add the stack frame
            operations.Add(new StackFrame(
                currentAddress, entry.Mnemonic, operand,
                depthBefore, depthAfter, frameWarning));

            // Handle termination
            if (entry.Mnemonic is "RTS" or "RTI")
            {
                break;
            }

            if (entry.Mnemonic == "BRK")
            {
                break;
            }

            // Handle JMP
            if (entry.Mnemonic == "JMP")
            {
                if (entry.Mode == AddressingMode.Indirect)
                {
                    // Indirect jump: stop tracing
                    operations.Add(new StackFrame(
                        currentAddress, entry.Mnemonic, operand,
                        depth, depth, "[indirect jump, cannot trace]"));
                    break;
                }

                // Follow the absolute jump target
                var target = (ushort)(data[position + 1] | (data[position + 2] << 8));
                if (target < data.Length)
                {
                    position = target;
                    continue;
                }
                break;
            }

            // Handle conditional branches
            if (BranchMnemonics.Contains(entry.Mnemonic))
            {
                var branchTarget = (ushort)(currentAddress + entry.Bytes + unchecked((sbyte)data[position + 1]));
                if (branchTarget < data.Length && !path.Contains(branchTarget))
                {
                    // Trace the branch path separately
                    var branchOps = new List<StackFrame>();
                    var branchWarnings = new List<string>();
                    var branchVisited = new HashSet<ushort>(visited);

                    var branchResult = TracePath(
                        data, branchTarget, branchTarget, depth,
                        budgetRemaining,
                        branchVisited, branchOps, branchWarnings,
                        recursionDepth + 1);

                    maxDepth = Math.Max(maxDepth, branchResult.MaxDepth);
                    minDepth = Math.Min(minDepth, branchResult.MinDepth);

                    // If the branch path has a different stack depth, flag it
                    if (branchResult.ExitDepth != depthAfter)
                    {
                        warnings.Add(
                            $"Branch at {Formatting.HexWord(currentAddress)} leads to " +
                            $"different stack depth ({branchResult.ExitDepth}) vs fall-through ({depthAfter})");
                    }
                }
                // Continue fall-through path
                position += entry.Bytes;
                continue;
            }

            // Handle JSR
            if (entry.Mnemonic == "JSR")
            {
                var target = (ushort)(data[position + 1] | (data[position + 2] << 8));
                if (target < data.Length && !path.Contains(target) && recursionDepth < 10)
                {
                    // Trace into the subroutine
                    var subOps = new List<StackFrame>();
                    var subWarnings = new List<string>();
                    var subVisited = new HashSet<ushort>(visited);

                    var subResult = TracePath(
                        data, target, target, depth,
                        budgetRemaining / 2,
                        subVisited, subOps, subWarnings,
                        recursionDepth + 1);

                    maxDepth = Math.Max(maxDepth, subResult.MaxDepth);
                    minDepth = Math.Min(minDepth, subResult.MinDepth);

                    // Check if the subroutine is balanced
                    if (subResult.ExitDepth != depth)
                    {
                        warnings.Add(
                            $"Subroutine at {Formatting.HexWord(target)} exits with depth " +
                            $"{subResult.ExitDepth} vs entry depth {depth}");
                    }
                }

                position += entry.Bytes;
                continue;
            }

            position += entry.Bytes;
        }

        if (budgetRemaining <= 0)
        {
            warnings.Add($"Instruction budget exhausted at {Formatting.HexWord(currentAddress)}");
        }

        return (depth, maxDepth, minDepth);
    }

    /// <summary>
    /// Get the stack effect of a 6502 instruction.
    /// </summary>
    private static int GetStackEffect(OpcodeEntry entry)
    {
        return StackEffects.TryGetValue(entry.Mnemonic, out var effect) ? effect : 0;
    }

    /// <summary>
    /// Format a stack analysis result as human-readable text.
    /// </summary>
    public static string FormatStackAnalysis(StackAnalysisResult result, string format = "text")
    {
        return format.ToLowerInvariant() switch
        {
            "csv" => FormatStackCsv(result),
            "tsv" => FormatStackTsv(result),
            "kv" => FormatStackKv(result),
            _ => FormatStackText(result)
        };
    }

    private static string FormatStackText(StackAnalysisResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Stack analysis for {Formatting.HexWord(result.EntryPoint)}:");
        sb.AppendLine($"  Entry stack depth: {result.EntryDepth} (return address on stack)");
        sb.AppendLine($"  Maximum depth: {result.MaxDepth}");
        sb.AppendLine($"  Minimum depth: {result.MinDepth}");
        sb.AppendLine($"  Exit stack depth: {result.ExitDepth} " +
            $"({(result.IsBalanced ? "balanced" : "unbalanced")})");
        sb.AppendLine();

        if (result.Operations.Count > 0)
        {
            sb.AppendLine("  Stack operations:");
            foreach (var op in result.Operations)
            {
                var depthArrow = op.DepthBefore != op.DepthAfter
                    ? $"{op.DepthBefore}→{op.DepthAfter}"
                    : $"{op.DepthBefore}";
                var warning = op.Warning is not null ? $" [{op.Warning}]" : "";
                var operandStr = string.IsNullOrWhiteSpace(op.Operand) ? "" : $" {op.Operand}";
                sb.AppendLine($"    {Formatting.HexWord(op.Address)}: {op.Mnemonic}{operandStr}  ; depth {depthArrow}{warning}");
            }
            sb.AppendLine();
        }

        if (result.Warnings.Count > 0)
        {
            sb.AppendLine("  Warnings:");
            foreach (var w in result.Warnings)
            {
                var prefix = w.Contains("unbalanced") || w.Contains("underflow") || w.Contains("differs")
                    ? "⚠ " : "  - ";
                sb.AppendLine($"    {prefix}{w}");
            }
        }

        return sb.ToString();
    }

    private static string FormatStackCsv(StackAnalysisResult result)
    {
        var headers = new[] { "address", "mnemonic", "operand", "depth_before", "depth_after", "warning" };
        var rows = result.Operations.Select(op => new[]
        {
            Formatting.HexWord(op.Address),
            op.Mnemonic,
            op.Operand,
            op.DepthBefore.ToString(),
            op.DepthAfter.ToString(),
            op.Warning ?? ""
        }).ToArray();

        // Add summary rows
        var summaryHeaders = new[] { "metric", "value", "", "", "", "" };
        var summaryRows = new[]
        {
            new[] { "entry_depth", result.EntryDepth.ToString(), "", "", "", "" },
            new[] { "max_depth", result.MaxDepth.ToString(), "", "", "", "" },
            new[] { "min_depth", result.MinDepth.ToString(), "", "", "", "" },
            new[] { "exit_depth", result.ExitDepth.ToString(), "", "", "", "" },
            new[] { "balanced", result.IsBalanced.ToString(), "", "", "", "" }
        };

        var allHeaders = headers.Concat(summaryHeaders).ToArray();
        var allRows = rows.Concat(summaryRows).ToArray();

        return OutputFormatter.FormatCsv(allHeaders, allRows);
    }

    private static string FormatStackTsv(StackAnalysisResult result)
    {
        var headers = new[] { "address", "mnemonic", "operand", "depth_before", "depth_after", "warning" };
        var rows = result.Operations.Select(op => new[]
        {
            Formatting.HexWord(op.Address),
            op.Mnemonic,
            op.Operand,
            op.DepthBefore.ToString(),
            op.DepthAfter.ToString(),
            op.Warning ?? ""
        }).ToArray();
        return OutputFormatter.FormatTsv(headers, rows);
    }

    private static string FormatStackKv(StackAnalysisResult result)
    {
        var keys = new[] { "address", "mnemonic", "operand", "depth_before", "depth_after", "warning" };
        var rows = result.Operations.Select(op => new[]
        {
            Formatting.HexWord(op.Address),
            op.Mnemonic,
            op.Operand,
            op.DepthBefore.ToString(),
            op.DepthAfter.ToString(),
            op.Warning ?? ""
        }).ToArray();
        return OutputFormatter.FormatKv(keys, rows);
    }
}