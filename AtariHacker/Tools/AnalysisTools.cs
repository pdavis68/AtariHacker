using System.Text;
using AtariHacker.Analysis;
using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

public static class AnalysisTools
{
    /// <summary>
    /// Compound command: run multi-pass analysis then immediately disassemble a range.
    /// </summary>
    public static string AnalyzeAndDisassemble(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SegmentManager segmentManager,
        string startAddress,
        int bytes,
        string format = "listing",
        VerboseContext? verbose = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            // 1. Run analysis
            var data = session.Data;
            var segments = session.Segments;
            var baseAddress = session.BaseAddress;

            var references = DisassemblyAnalyzer.Analyze(data, segments, baseAddress);
            var (codeRegions, dataRegions) = DisassemblyAnalyzer.TraceCodeRegions(data, references, segments, baseAddress);
            var labelMap = DisassemblyAnalyzer.GenerateLabels(references, symbols, zeroPageMap, codeRegions);

            if (verbose is not null)
            {
                verbose.BytesProcessed = data.Length;
                verbose.PassesCompleted = 3;
            }

            // 2. Run disassembly with analysis results
            var result = DisassemblerTool.Disassemble(session, symbols, zeroPageMap,
                startAddress, bytes, startAddress, format, analyze: true, verbose);

            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Compound command: probe a range then auto-create a segment from the highest-confidence detection.
    /// </summary>
    public static string ProbeAndSegment(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SegmentManager segmentManager,
        SessionPersistence persistence,
        string start,
        string end,
        VerboseContext? verbose = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var startAddr = AddressParser.ParseAddress(start);
            var endAddr = AddressParser.ParseAddress(end);

            if (startAddr > endAddr)
            {
                return "ERROR: Start address must be <= end address.";
            }

            // Run probe
            var result = DataProber.ProbeData(session.Data, startAddr, endAddr);

            if (verbose is not null)
            {
                verbose.BytesProcessed = endAddr - startAddr + 1;
                verbose.Confidence = result.Confidence;
            }

            // Auto-create segment based on highest-confidence detection
            var segmentType = result.Confidence switch
            {
                "High" => result.Description.Contains("String", StringComparison.OrdinalIgnoreCase) ? "text"
                    : result.Description.Contains("Display", StringComparison.OrdinalIgnoreCase) ? "graphics"
                    : result.Description.Contains("Font", StringComparison.OrdinalIgnoreCase) ? "graphics"
                    : result.Description.Contains("Table", StringComparison.OrdinalIgnoreCase) ? "data"
                    : "data",
                "Medium" => "data",
                _ => "data"
            };

            var segmentName = $"auto_{startAddr:X4}_{endAddr:X4}";
            var segmentResult = SegmentTools.DefineSegment(segmentManager, persistence,
                segmentName, segmentType, start, end,
                $"Auto-created from probe: {result.Description}");

            var sb = new StringBuilder();
            sb.AppendLine("=== Probe Results ===");
            sb.AppendLine(result.Description);
            sb.AppendLine($"  Confidence: {result.Confidence}");
            foreach (var detail in result.Details)
            {
                sb.AppendLine($"  {detail}");
            }
            sb.AppendLine();
            sb.AppendLine("=== Auto-Created Segment ===");
            sb.AppendLine(segmentResult);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Compound command: run full analysis, generate labels, create segments, and output summary.
    /// </summary>
    public static string AnalyzeFull(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SegmentManager segmentManager,
        SessionPersistence persistence,
        VerboseContext? verbose = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var data = session.Data;
            var segments = session.Segments;
            var baseAddress = session.BaseAddress;

            // 1. Run full analysis
            var references = DisassemblyAnalyzer.Analyze(data, segments, baseAddress);
            var (codeRegions, dataRegions) = DisassemblyAnalyzer.TraceCodeRegions(data, references, segments, baseAddress);
            var labelMap = DisassemblyAnalyzer.GenerateLabels(references, symbols, zeroPageMap, codeRegions);

            // Persist generated labels into the symbol table so they are
            // available to subsequent commands (e.g. 'symbol list') and
            // saved to the sidecar file by persistence.Save() below.
            foreach (var (addr, label) in labelMap.Labels)
            {
                if (!symbols.ContainsKey(addr))
                {
                    symbols[addr] = new SymbolEntry(label, Comment: null, IsHardware: false, IsUserDefined: false);
                }
            }

            if (verbose is not null)
            {
                verbose.BytesProcessed = data.Length;
                verbose.PassesCompleted = 3;
            }

            // 2. Create segments from detected code/data regions
            var segmentCount = 0;
            if (codeRegions.Count > 0)
            {
                var codeRanges = GroupConsecutive(codeRegions);
                foreach (var (rangeStart, rangeEnd) in codeRanges)
                {
                    var name = $"code_{rangeStart:X4}_{rangeEnd:X4}";
                    segmentManager.Define(new SegmentDefinition(name, SegmentType.Code, rangeStart, rangeEnd, "Auto-detected code region"));
                    segmentCount++;
                }
            }
            if (dataRegions.Count > 0)
            {
                var dataRanges = GroupConsecutive(dataRegions);
                foreach (var (rangeStart, rangeEnd) in dataRanges)
                {
                    var name = $"data_{rangeStart:X4}_{rangeEnd:X4}";
                    segmentManager.Define(new SegmentDefinition(name, SegmentType.Data, rangeStart, rangeEnd, "Auto-detected data region"));
                    segmentCount++;
                }
            }
            persistence.Save();

            // 3. Build summary output
            var totalBytes = dataRegions.Count + codeRegions.Count;
            var codePct = totalBytes > 0 ? (double)codeRegions.Count / totalBytes * 100 : 0;
            var dataPct = totalBytes > 0 ? (double)dataRegions.Count / totalBytes * 100 : 0;

            var sb = new StringBuilder();
            sb.AppendLine("=== Full Analysis Complete ===");
            sb.AppendLine($"  Code entry points: {references.CodeEntryPoints.Count}");
            sb.AppendLine($"  Subroutines: {references.SubroutineEntries.Count}");
            sb.AppendLine($"  Jump targets: {references.JumpTargets.Count}");
            sb.AppendLine($"  Branch targets: {references.BranchTargets.Count}");
            sb.AppendLine($"  Data references: {references.AbsoluteDataReferences.Count}");
            sb.AppendLine($"  Code bytes: {codeRegions.Count} ({codePct:F1}%)");
            sb.AppendLine($"  Data bytes: {dataRegions.Count} ({dataPct:F1}%)");
            sb.AppendLine($"  Labels generated: {labelMap.Labels.Count}");
            sb.AppendLine($"  Segments created: {segmentCount}");
            sb.AppendLine();
            sb.AppendLine("Use 'disassemble' with --analyze to view analyzed output.");
            sb.AppendLine("Use 'segment list' to review auto-created segments.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Group consecutive ushort values into contiguous ranges.
    /// </summary>
    private static List<(ushort Start, ushort End)> GroupConsecutive(HashSet<ushort> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var ranges = new List<(ushort Start, ushort End)>();
        if (sorted.Count == 0) return ranges;

        ushort? rangeStart = null;
        ushort? rangeEnd = null;
        foreach (var addr in sorted)
        {
            if (rangeStart is null) { rangeStart = addr; rangeEnd = addr; }
            else if (rangeEnd is not null && addr == rangeEnd + 1) { rangeEnd = addr; }
            else
            {
                ranges.Add((rangeStart!.Value, rangeEnd!.Value));
                rangeStart = addr; rangeEnd = addr;
            }
        }
        if (rangeStart is not null && rangeEnd is not null)
        {
            ranges.Add((rangeStart.Value, rangeEnd.Value));
        }
        return ranges;
    }

    public static string AnalyzeDisassembly(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string? startAddress = null,
        int? numBytes = null,
        string format = "summary",
        VerboseContext? verbose = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var data = session.Data;
            var segments = session.Segments;
            var baseAddress = session.BaseAddress;

            // Determine analysis range
            var startOffset = 0;
            var byteCount = data.Length;
            if (!string.IsNullOrWhiteSpace(startAddress))
            {
                var addr = AddressParser.ParseAddress(startAddress);
                var offset = XexAddressResolver.ResolveMemoryAddress(session, addr);
                if (offset is null)
                {
                    return $"ERROR: Address {Formatting.HexWord(addr)} is not in the loaded data.";
                }
                startOffset = offset.Value;
            }
            if (numBytes is not null)
            {
                byteCount = Math.Min(numBytes.Value, data.Length - startOffset);
            }

            // Run analysis on the full ROM for complete reference graph
            var references = DisassemblyAnalyzer.Analyze(data, segments, baseAddress);
            var (codeRegions, dataRegions) = DisassemblyAnalyzer.TraceCodeRegions(data, references, segments, baseAddress);
            var labelMap = DisassemblyAnalyzer.GenerateLabels(references, symbols, zeroPageMap, codeRegions);
            if (verbose is not null)
            {
                verbose.BytesProcessed = data.Length;
                verbose.PassesCompleted = 3;
            }

            // Determine the range for analysis output
            ushort analysisStart;
            ushort analysisEnd;
            if (segments is { Count: > 0 })
            {
                analysisStart = segments[0].LoadAddress;
                analysisEnd = segments[^1].EndAddress;
            }
            else if (baseAddress is not null)
            {
                analysisStart = baseAddress.Value;
                analysisEnd = (ushort)(baseAddress.Value + data.Length - 1);
            }
            else
            {
                analysisStart = 0;
                analysisEnd = (ushort)Math.Min(data.Length - 1, 0xFFFF);
            }

            switch (format.ToLowerInvariant())
            {
                case "graph":
                    return FormatGraph(references, symbols, zeroPageMap);

                case "labels":
                    return FormatLabels(labelMap);

                case "full":
                    return FormatFull(references, labelMap, codeRegions, dataRegions, analysisStart, analysisEnd, data.Length);

                default:
                    return FormatSummary(references, codeRegions, dataRegions, analysisStart, analysisEnd, data.Length, symbols, zeroPageMap);
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string ProbeData(
        RomSession session,
        string start,
        string end,
        string format = "text",
        VerboseContext? verbose = null,
        bool runStructMatch = false)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var startAddr = AddressParser.ParseAddress(start);
            var endAddr = AddressParser.ParseAddress(end);

            if (startAddr > endAddr)
            {
                return "ERROR: Start address must be <= end address.";
            }

            // Convert memory addresses to file offsets
            var startOffset = XexAddressResolver.ResolveMemoryAddress(session, startAddr);
            var endOffset = XexAddressResolver.ResolveMemoryAddress(session, endAddr);

            if (startOffset is null || endOffset is null)
            {
                return $"ERROR: Address range ${startAddr:X4}–${endAddr:X4} is not in the loaded data.";
            }

            // Map back to memory addresses for the probe (the prober works on byte ranges)
            var probeStart = startAddr;
            var probeEnd = endAddr;

            var result = DataProber.ProbeData(session.Data, probeStart, probeEnd);
            if (verbose is not null)
            {
                verbose.BytesProcessed = endOffset.Value - startOffset.Value + 1;
                verbose.Confidence = result.Confidence;
            }

            var probeOutput = format.ToLowerInvariant() switch
            {
                "csv" => result.ToCsv(),
                "tsv" => result.ToTsv(),
                "kv" => result.ToKv(),
                _ => FormatProbeText(result)
            };

            // Optionally run structural matching
            if (runStructMatch)
            {
                var library = StructureLibrary.Load();
                if (library.Templates.Count > 0)
                {
                    var baseAddress = session.BaseAddress ?? 0;
                    var matches = StructureMatcher.MatchAll(session.Data, baseAddress, startAddr, endAddr, library.Templates);

                    if (matches.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine(probeOutput);
                        sb.AppendLine();
                        sb.AppendLine("=== Structural Template Matches ===");
                        sb.AppendLine($"Found {matches.Count} structural match(es):");
                        sb.AppendLine();

                        foreach (var match in matches.Take(10))
                        {
                            sb.AppendLine($"  Template: {match.TemplateName}");
                            sb.AppendLine($"  Address:  ${match.Address:X4}");
                            sb.AppendLine($"  Confidence: {match.Confidence:P1}");
                            sb.AppendLine();
                        }

                        if (matches.Count > 10)
                        {
                            sb.AppendLine($"  ... and {matches.Count - 10} more matches");
                            sb.AppendLine("  Use 'struct match' to see full details.");
                        }

                        return sb.ToString();
                    }
                }
            }

            return probeOutput;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string FormatProbeText(ProbeResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Description);
        sb.AppendLine($"  Confidence: {result.Confidence}");
        foreach (var detail in result.Details)
        {
            sb.AppendLine($"  {detail}");
        }
        return sb.ToString();
    }

    public static string GenerateCallGraph(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        string? startAddress = null,
        int depth = 3,
        string format = "mermaid")
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            ushort? startAddr = null;
            if (!string.IsNullOrWhiteSpace(startAddress))
            {
                startAddr = AddressParser.ParseAddress(startAddress);
            }

            var references = DisassemblyAnalyzer.Analyze(session.Data, session.Segments, session.BaseAddress);

            var graph = CallGraph.BuildCallGraphFromData(
                session.Data, references, session.Segments, session.BaseAddress,
                startAddr, depth);

            return format.ToLowerInvariant() switch
            {
                "text" => CallGraph.FormatText(graph, symbols, zeroPageMap),
                "csv" => CallGraph.FormatCsv(graph, symbols, zeroPageMap),
                "tsv" => CallGraph.FormatTsv(graph, symbols, zeroPageMap),
                "kv" => CallGraph.FormatKv(graph, symbols, zeroPageMap),
                _ => CallGraph.FormatMermaid(graph, symbols, zeroPageMap)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string AnalyzeCoverage(
        RomSession session,
        string start,
        string end,
        string format = "text",
        VerboseContext? verbose = null)
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var startAddr = AddressParser.ParseAddress(start);
            var endAddr = AddressParser.ParseAddress(end);

            // Validate addresses against the session's memory-mapped range.
            // This ensures that raw file offsets (e.g. 0, 384) are not silently
            // treated as memory addresses, and that hex addresses (e.g. 0x0700)
            // are properly validated against the loaded data's address space.
            var startOffset = XexAddressResolver.ResolveMemoryAddress(session, startAddr);
            var endOffset = XexAddressResolver.ResolveMemoryAddress(session, endAddr);
            if (startOffset is null || endOffset is null)
            {
                return $"ERROR: Address range ${startAddr:X4}–${endAddr:X4} is not in the loaded data.";
            }

            var references = DisassemblyAnalyzer.Analyze(session.Data, session.Segments, session.BaseAddress);
            var (codeRegions, dataRegions) = DisassemblyAnalyzer.TraceCodeRegions(session.Data, references, session.Segments, session.BaseAddress);

            var result = CodeCoverage.AnalyzeCoverage(session.Data, references, codeRegions, dataRegions, startAddr, endAddr);
            if (verbose is not null)
            {
                verbose.BytesProcessed = endAddr - startAddr + 1;
            }

            return format.ToLowerInvariant() switch
            {
                "csv" => result.ToCsv(),
                "tsv" => result.ToTsv(),
                "kv" => result.ToKv(),
                _ => CodeCoverage.FormatCoverage(result)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public static string DiffRoms(
        string file1,
        string file2,
        string format = "summary")
    {
        try
        {
            if (!File.Exists(file1))
                return $"ERROR: File not found: {file1}";
            if (!File.Exists(file2))
                return $"ERROR: File not found: {file2}";

            var result = DiffAnalyzer.DiffRoms(file1, file2);

            return format.ToLowerInvariant() switch
            {
                "verbose" => DiffAnalyzer.FormatVerbose(result),
                "hex" => DiffAnalyzer.FormatHexDiff(result),
                _ => DiffAnalyzer.FormatSummary(result)
            };
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ─── Formatting helpers ────────────────────────────────────────────────

    private static string FormatSummary(
        ReferenceGraph references,
        HashSet<ushort> codeRegions,
        HashSet<ushort> dataRegions,
        ushort analysisStart,
        ushort analysisEnd,
        int dataLength,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap)
    {
        var totalBytes = dataRegions.Count + codeRegions.Count;
        var codePct = totalBytes > 0 ? (double)codeRegions.Count / totalBytes * 100 : 0;
        var dataPct = totalBytes > 0 ? (double)dataRegions.Count / totalBytes * 100 : 0;

        var sb = new StringBuilder();
        sb.AppendLine("Disassembly Analysis:");
        sb.AppendLine($"  Code entry points: {references.CodeEntryPoints.Count}");
        sb.AppendLine($"  Data references: {references.AbsoluteDataReferences.Count}");
        sb.AppendLine($"  Branch targets: {references.BranchTargets.Count}");
        sb.AppendLine($"  Subroutines: {references.SubroutineEntries.Count}");
        sb.AppendLine($"  Code bytes: {codeRegions.Count} ({codePct:F1}%)");
        sb.AppendLine($"  Data bytes: {dataRegions.Count} ({dataPct:F1}%)");

        // Find top-level subroutines (not called by other subroutines in range)
        var calledSubs = new HashSet<ushort>();
        // (Simplified: show all subroutine entries)
        if (references.SubroutineEntries.Count > 0)
        {
            sb.AppendLine("  ---");
            sb.AppendLine("  Subroutine entries:");
            var subList = references.SubroutineEntries.OrderBy(a => a).Take(10).ToList();
            foreach (var sub in subList)
            {
                var name = SymbolResolver.Resolve(sub, symbols, zeroPageMap) ?? $"sub_{sub:X4}";
                sb.AppendLine($"    {Formatting.HexWord(sub)} ({name})");
            }
            if (references.SubroutineEntries.Count > 10)
            {
                sb.AppendLine($"    ... and {references.SubroutineEntries.Count - 10} more");
            }
        }

        // Find unreferenced code regions (addresses in codeRegions but not in instruction addresses)
        var unreferenced = codeRegions.Except(references.InstructionAddresses)
            .Where(a => a >= analysisStart && a <= analysisEnd)
            .OrderBy(a => a)
            .ToList();

        if (unreferenced.Count > 0)
        {
            sb.AppendLine("  ---");
            sb.AppendLine("  Unreferenced code regions (potential dead code or data):");
            // Group consecutive addresses
            ushort? regionStart = null;
            ushort? regionEnd = null;
            foreach (var addr in unreferenced)
            {
                if (regionStart is null) { regionStart = addr; regionEnd = addr; }
                else if (regionEnd is not null && addr == regionEnd + 1) { regionEnd = addr; }
                else
                {
                    if (regionStart is not null && regionEnd is not null && regionEnd - regionStart >= 3)
                        sb.AppendLine($"    {Formatting.HexWord(regionStart.Value)}–{Formatting.HexWord(regionEnd.Value)} ({regionEnd - regionStart + 1} bytes)");
                    regionStart = addr; regionEnd = addr;
                }
            }
            if (regionStart is not null && regionEnd is not null && regionEnd - regionStart >= 3)
                sb.AppendLine($"    {Formatting.HexWord(regionStart.Value)}–{Formatting.HexWord(regionEnd.Value)} ({regionEnd - regionStart + 1} bytes)");
        }

        return sb.ToString();
    }

    private static string FormatGraph(ReferenceGraph references, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Reference Graph:");

        if (references.SubroutineEntries.Count > 0)
        {
            sb.AppendLine($"  Subroutine entries ({references.SubroutineEntries.Count}):");
            foreach (var addr in references.SubroutineEntries.OrderBy(a => a))
            {
                var name = SymbolResolver.Resolve(addr, symbols, zeroPageMap) ?? $"sub_{addr:X4}";
                sb.AppendLine($"    {Formatting.HexWord(addr)} → {name}");
            }
        }

        if (references.JumpTargets.Count > 0)
        {
            sb.AppendLine($"  Jump targets ({references.JumpTargets.Count}):");
            foreach (var addr in references.JumpTargets.OrderBy(a => a))
            {
                sb.AppendLine($"    {Formatting.HexWord(addr)}");
            }
        }

        if (references.AbsoluteDataReferences.Count > 0)
        {
            sb.AppendLine($"  Data references ({references.AbsoluteDataReferences.Count}):");
            foreach (var addr in references.AbsoluteDataReferences.OrderBy(a => a).Take(20))
            {
                sb.AppendLine($"    {Formatting.HexWord(addr)}");
            }
            if (references.AbsoluteDataReferences.Count > 20)
            {
                sb.AppendLine($"    ... and {references.AbsoluteDataReferences.Count - 20} more");
            }
        }

        return sb.ToString();
    }

    private static string FormatLabels(LabelMap labelMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Generated Labels:");

        foreach (var kvp in labelMap.Labels.OrderBy(p => p.Key))
        {
            var comment = labelMap.Comments.TryGetValue(kvp.Key, out var cmt) ? $"  ; {cmt}" : string.Empty;
            sb.AppendLine($"  {Formatting.HexWord(kvp.Key)}  {kvp.Value}{comment}");
        }

        return sb.ToString();
    }

    private static string FormatFull(
        ReferenceGraph references,
        LabelMap labelMap,
        HashSet<ushort> codeRegions,
        HashSet<ushort> dataRegions,
        ushort analysisStart,
        ushort analysisEnd,
        int dataLength)
    {
        var totalBytes = dataRegions.Count + codeRegions.Count;
        var codePct = totalBytes > 0 ? (double)codeRegions.Count / totalBytes * 100 : 0;
        var dataPct = totalBytes > 0 ? (double)dataRegions.Count / totalBytes * 100 : 0;

        var sb = new StringBuilder();
        sb.AppendLine("=== Full Analysis ===");
        sb.AppendLine();
        sb.AppendLine($"Code entry points: {references.CodeEntryPoints.Count}");
        sb.AppendLine($"Subroutines: {references.SubroutineEntries.Count}");
        sb.AppendLine($"Jump targets: {references.JumpTargets.Count}");
        sb.AppendLine($"Indirect jump targets: {references.IndirectJumpTargets.Count}");
        sb.AppendLine($"Branch targets: {references.BranchTargets.Count}");
        sb.AppendLine($"Absolute data references: {references.AbsoluteDataReferences.Count}");
        sb.AppendLine($"Indirect data references (zero page): {references.IndirectDataReferences.Count}");
        sb.AppendLine($"Instruction addresses: {references.InstructionAddresses.Count}");
        sb.AppendLine($"Code bytes: {codeRegions.Count} ({codePct:F1}%)");
        sb.AppendLine($"Data bytes: {dataRegions.Count} ({dataPct:F1}%)");
        sb.AppendLine();

        sb.AppendLine("--- Labels ---");
        foreach (var kvp in labelMap.Labels.OrderBy(p => p.Key))
        {
            var comment = labelMap.Comments.TryGetValue(kvp.Key, out var cmt) ? $"  ; {cmt}" : string.Empty;
            sb.AppendLine($"  {Formatting.HexWord(kvp.Key)}  {kvp.Value}{comment}");
        }

        return sb.ToString();
    }
}