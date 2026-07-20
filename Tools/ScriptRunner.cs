using System.Text;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Tools;

public static class ScriptRunner
{
    public static string RunScript(
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SessionPersistence persistence,
        SegmentManager segmentManager,
        string script)
    {
        try
        {
            if (!File.Exists(script))
                return $"ERROR: Script file not found: {script}";

            var lines = File.ReadAllLines(script);
            var results = new List<string>();
            var errors = 0;
            var executed = 0;

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();

                // Skip comments and blank lines
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                // Skip output redirection (captured by the shell, not the script runner)
                var line = trimmed;
                if (line.Contains('>'))
                {
                    line = line[..line.IndexOf('>')].Trim();
                }

                results.Add($"> {trimmed}");

                try
                {
                    var result = DispatchCommand(line, session, symbols, zeroPageMap, persistence, segmentManager);
                    results.Add(result);
                    executed++;
                }
                catch (Exception ex)
                {
                    results.Add($"ERROR: {ex.Message}");
                    errors++;
                    break; // Stop on first error
                }
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Script executed: {executed} commands, {errors} errors");
            summary.AppendLine();
            summary.Append(string.Join('\n', results));

            return summary.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string DispatchCommand(
        string line,
        RomSession session,
        SymbolTable symbols,
        ZeroPageMap zeroPageMap,
        SessionPersistence persistence,
        SegmentManager segmentManager)
    {
        // Parse command line: "command_name param1=value1 param2=value2"
        var parts = ParseCommandLine(line);
        if (parts.Count == 0)
            return "ERROR: Empty command.";

        var command = parts[0].ToLowerInvariant();
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts.Skip(1))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                var key = part[..eq];
                var value = part[(eq + 1)..];
                // Remove quotes if present
                if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }
                args[key] = value;
            }
        }

        return command switch
        {
            "load_rom" => FileTools.LoadRom(session, symbols, zeroPageMap, persistence, GetArg(args, "filePath")),
            "rom_info" => FileTools.RomInfo(session, symbols, zeroPageMap),
            "define_symbol" => SymbolTools.DefineSymbol(session, symbols, persistence, GetArg(args, "address"), GetArg(args, "label"), GetOptArg(args, "comment")),
            "remove_symbol" => SymbolTools.RemoveSymbol(session, symbols, persistence, GetArg(args, "address")),
            "lookup_symbol" => SymbolTools.LookupSymbol(session, symbols, GetArg(args, "address")),
            "list_symbols" => SymbolTools.ListSymbols(session, symbols, bool.TryParse(GetOptArg(args, "includeHardware"), out var hw) && hw, GetOptArg(args, "filter")),
            "annotate_zero_page" => ZeroPageTool.AnnotateZeroPage(session, zeroPageMap, persistence, GetArg(args, "address"), GetArg(args, "label"), GetOptArg(args, "comment")),
            "show_zero_page_map" => ZeroPageTool.ShowZeroPageMap(session, zeroPageMap, bool.TryParse(GetOptArg(args, "showUnannotated"), out var show) && show),
            "define_segment" => SegmentTools.DefineSegment(segmentManager, persistence, GetArg(args, "name"), GetArg(args, "type"), GetArg(args, "start"), GetArg(args, "end"), GetOptArg(args, "comment")),
            "remove_segment" => SegmentTools.RemoveSegment(segmentManager, persistence, GetArg(args, "name")),
            "list_segments" => SegmentTools.ListSegments(segmentManager),
            "clear_segments" => SegmentTools.ClearSegments(segmentManager, persistence),
            "generate_linker_config" => SegmentTools.GenerateLinkerConfig(segmentManager, GetArg(args, "output")),
            "disassemble" => DisassemblerTool.Disassemble(session, symbols, zeroPageMap,
                GetArg(args, "offset"),
                int.Parse(GetArg(args, "numBytes")),
                GetOptArg(args, "startAddress"),
                GetOptArg(args, "format") ?? "listing"),
            "analyze_disassembly" => AnalysisTools.AnalyzeDisassembly(session, symbols, zeroPageMap,
                GetOptArg(args, "startAddress"),
                int.TryParse(GetOptArg(args, "numBytes"), out var nb) ? nb : null,
                GetOptArg(args, "format") ?? "summary"),
            "probe_data" => AnalysisTools.ProbeData(session, GetArg(args, "start"), GetArg(args, "end")),
            "generate_callgraph" => AnalysisTools.GenerateCallGraph(session, symbols, zeroPageMap,
                GetOptArg(args, "startAddress"),
                int.TryParse(GetOptArg(args, "depth"), out var d) ? d : 3,
                GetOptArg(args, "format") ?? "mermaid"),
            "analyze_coverage" => AnalysisTools.AnalyzeCoverage(session, GetArg(args, "start"), GetArg(args, "end")),
            "diff_roms" => AnalysisTools.DiffRoms(GetArg(args, "file1"), GetArg(args, "file2"), GetOptArg(args, "format") ?? "summary"),
            "define_filesystem" => AtrWriteTools.DefineFilesystem(GetArg(args, "filePath"), GetArg(args, "directoryOffset"), int.Parse(GetArg(args, "entrySize")), int.Parse(GetArg(args, "filenameLength")), int.Parse(GetArg(args, "extensionLength")), int.Parse(GetArg(args, "startSectorOffset")), int.Parse(GetArg(args, "sectorCountOffset"))),
            "extract_atr_file" => AtrWriteTools.ExtractAtrFile(GetArg(args, "filePath"), GetArg(args, "name"), GetArg(args, "output")),
            "inject_atr_file" => AtrWriteTools.InjectAtrFile(GetArg(args, "filePath"), GetArg(args, "name"), GetArg(args, "input")),
            "create_atr" => AtrWriteTools.CreateAtr(GetArg(args, "output"), int.Parse(GetArg(args, "sectors")), GetArg(args, "density")),
            "write_atr_sector" => AtrWriteTools.WriteAtrSector(GetArg(args, "filePath"), GetArg(args, "sector"), GetArg(args, "input")),
            "write_atr_file" => AtrWriteTools.WriteAtrFile(GetArg(args, "filePath"), GetArg(args, "name"), GetArg(args, "input"), GetOptArg(args, "startSector")),
            "set_symbols" => SymbolTools.SetSymbols(symbols, zeroPageMap, persistence,
                bool.TryParse(GetOptArg(args, "hardware"), out var h) ? h : null,
                bool.TryParse(GetOptArg(args, "osVariables"), out var ov) ? ov : null,
                bool.TryParse(GetOptArg(args, "osRom"), out var or) ? or : null,
                bool.TryParse(GetOptArg(args, "userLabels"), out var ul) ? ul : null),
            "load_labels" => SymbolTools.LoadLabels(symbols, zeroPageMap, segmentManager, GetArg(args, "filePath")),
            "save_labels" => SymbolTools.SaveLabels(persistence, GetOptArg(args, "filePath")),
            "hex_dump" => HexDumpTool.HexDump(session, GetArg(args, "offset"), int.Parse(GetArg(args, "numBytes")), GetOptArg(args, "startAddress")),
            "find_strings" => StringSearchTool.FindStrings(session,
                int.TryParse(GetOptArg(args, "minLength"), out var ml) ? ml : 4,
                GetOptArg(args, "encoding") ?? "ascii",
                GetOptArg(args, "filter"),
                int.TryParse(GetOptArg(args, "maxResults"), out var mr) ? mr : 50),
            "find_pattern" => FindPatternTool.FindPattern(session, GetArg(args, "pattern"), int.TryParse(GetOptArg(args, "maxResults"), out var mp) ? mp : 50),
            "x_ref" => XRefTool.XRef(session, symbols, zeroPageMap, GetArg(args, "address")),
            "trace_control_flow" => ControlFlowTool.TraceControlFlow(session, symbols, zeroPageMap, GetArg(args, "address"), int.TryParse(GetOptArg(args, "maxDepth"), out var md) ? md : 5),
            "hex_to_decimal" => ConversionTools.HexToDecimal(GetArg(args, "hex")),
            "decimal_to_hex" => ConversionTools.DecimalToHex(int.Parse(GetArg(args, "value"))),
            _ => $"ERROR: Unknown command '{command}'."
        };
    }

    private static List<string> ParseCommandLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        var quoteChar = '\0';

        foreach (var ch in line)
        {
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch is '"' or '\'')
            {
                inQuote = true;
                quoteChar = ch;
            }
            else if (ch == ' ')
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static string GetArg(Dictionary<string, string> args, string key)
    {
        if (args.TryGetValue(key, out var value))
            return value;
        throw new ArgumentException($"Missing required argument '{key}'.");
    }

    private static string? GetOptArg(Dictionary<string, string> args, string key)
    {
        return args.TryGetValue(key, out var value) ? value : null;
    }
}