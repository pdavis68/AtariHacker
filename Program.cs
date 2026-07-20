using System.CommandLine;
using AtariHackerMCP.Atari;
using AtariHackerMCP.State;
using AtariHackerMCP.Tools;

namespace AtariHackerMCP;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // ─── Global options ───────────────────────────────────────────────
        var targetOption = new Option<string?>(
            ["--target", "-t"],
            "Target file path (ATR, ROM, XEX). Overrides .atari-hacker.config.");
        var configOption = new Option<string?>(
            ["--config", "-c"],
            "Path to config file (default: .atari-hacker.config in current or parent directory).");

        var rootCommand = new RootCommand("AtariHacker — 6502 reverse engineering toolkit for Atari 8-bit binaries, ROMs, and disk images")
        {
            targetOption,
            configOption
        };

        // ─── Helper to create session state ─────────────────────────────
        static CliSession CreateCliSession()
        {
            var session = new RomSession();
            var symbols = new SymbolTable();
            AtariHardwareMap.Populate(symbols);
            var zeroPageMap = new ZeroPageMap();
            AtariHardwareMap.PopulateZeroPage(zeroPageMap);
            var segmentManager = new SegmentManager();
            var persistence = new SessionPersistence(session, symbols, zeroPageMap, segmentManager);
            return new CliSession(session, symbols, zeroPageMap, segmentManager, persistence);
        }

        static string LoadTarget(string? target, string? configPath, CliSession s)
        {
            var config = CliConfig.Load(configPath);
            var resolved = CliConfig.ResolveTarget(target, config);

            if (string.IsNullOrWhiteSpace(resolved))
                return "ERROR: No target file specified. Use --target <path> or create a .atari-hacker.config file.";

            if (!File.Exists(resolved))
                return $"ERROR: Target file not found: {resolved}";

            var ext = Path.GetExtension(resolved).ToLowerInvariant();
            if (ext is ".atr" or ".atx")
            {
                var bytes = File.ReadAllBytes(resolved);
                if (!AtrParser.IsAtr(bytes))
                    return $"ERROR: Not a valid ATR image: {resolved}";
                return AtrTools.LoadAtrBoot(s.Rom, s.Persistence, resolved);
            }

            return FileTools.LoadRom(s.Rom, s.Symbols, s.ZeroPage, s.Persistence, resolved);
        }

        static string EnsureLoaded(CliSession s, string? target, string? configPath)
        {
            if (s.Rom.IsLoaded) return string.Empty;
            var result = LoadTarget(target, configPath, s);
            if (result.StartsWith("ERROR:"))
                return result;
            return string.Empty;
        }

        // Helper to run a session-based command
        static void Run(Func<CliSession, string> action, string? target, string? config)
        {
            var s = CreateCliSession();
            var err = EnsureLoaded(s, target, config);
            if (err != string.Empty)
            {
                Console.Error.WriteLine(err);
                return;
            }
            Console.WriteLine(action(s));
        }

        // ═══════════════════════════════════════════════════════════════════
        // FILE COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        var loadCommand = new Command("load", "Load a ROM, XEX, or ATR file into the session");
        var loadPathArg = new Argument<string>("path", "Path to the file to load");
        loadCommand.AddArgument(loadPathArg);
        loadCommand.SetHandler((string path, string? target, string? config) =>
        {
            var s = CreateCliSession();
            var finalTarget = target ?? path;
            Console.Error.WriteLine($"Loading: {finalTarget}");
            Console.WriteLine(LoadTarget(finalTarget, config, s));
        }, loadPathArg, targetOption, configOption);

        var infoCommand = new Command("info", "Display information about the currently loaded binary");
        infoCommand.SetHandler((string? target, string? config) =>
        {
            Run(s => FileTools.RomInfo(s.Rom, s.Symbols, s.ZeroPage), target, config);
        }, targetOption, configOption);

        var scriptCommand = new Command("script", "Execute a sequence of commands from a script file");
        var scriptPathArg = new Argument<string>("path", "Path to the script file");
        scriptCommand.AddArgument(scriptPathArg);
        scriptCommand.SetHandler((string path, string? target, string? config) =>
        {
            var s = CreateCliSession();
            Console.WriteLine(ScriptRunner.RunScript(s.Rom, s.Symbols, s.ZeroPage, s.Persistence, s.Segments, path));
        }, scriptPathArg, targetOption, configOption);

        // ═══════════════════════════════════════════════════════════════════
        // DISASSEMBLY
        // ═══════════════════════════════════════════════════════════════════

        var disassembleCommand = new Command("disassemble", "Disassemble 6502 machine code");
        var offsetArg = new Argument<string>("offset", "File offset as decimal or hex");
        var numBytesArg = new Argument<int>("bytes", "Number of bytes to disassemble");
        var startAddrOpt = new Option<string?>("--start-address", "Override memory start address");
        var formatOpt = new Option<string>("--format", () => "listing", "Output format: listing, ca65, atasm, or mac65");
        var analyzeOpt = new Option<bool>("--analyze", "Use multi-pass analysis for label generation");
        disassembleCommand.AddArgument(offsetArg);
        disassembleCommand.AddArgument(numBytesArg);
        disassembleCommand.AddOption(startAddrOpt);
        disassembleCommand.AddOption(formatOpt);
        disassembleCommand.AddOption(analyzeOpt);
        disassembleCommand.SetHandler((string offset, int bytes, string? startAddr, string format, bool analyze, string? target, string? config) =>
        {
            Run(s => DisassemblerTool.Disassemble(s.Rom, s.Symbols, s.ZeroPage, offset, bytes, startAddr, format, analyze), target, config);
        }, offsetArg, numBytesArg, startAddrOpt, formatOpt, analyzeOpt, targetOption, configOption);

        // ═══════════════════════════════════════════════════════════════════
        // HEX DUMP
        // ═══════════════════════════════════════════════════════════════════

        var hexDumpCommand = new Command("hex-dump", "Produce a hex dump with file offsets, memory addresses, and ASCII");
        var hdOffsetArg = new Argument<string>("offset", "File offset as decimal or hex");
        var hdBytesArg = new Argument<int>("bytes", "Number of bytes to dump");
        var hdAddrOpt = new Option<string?>("--start-address", "Override memory start address");
        hexDumpCommand.AddArgument(hdOffsetArg);
        hexDumpCommand.AddArgument(hdBytesArg);
        hexDumpCommand.AddOption(hdAddrOpt);
        hexDumpCommand.SetHandler((string offset, int bytes, string? addr, string? target, string? config) =>
        {
            Run(s => HexDumpTool.HexDump(s.Rom, offset, bytes, addr), target, config);
        }, hdOffsetArg, hdBytesArg, hdAddrOpt, targetOption, configOption);

        // ═══════════════════════════════════════════════════════════════════
        // SEARCH
        // ═══════════════════════════════════════════════════════════════════

        var findPatternCommand = new Command("find-pattern", "Search for a byte pattern with optional wildcards");
        var patternArg = new Argument<string>("pattern", "Space-separated hex bytes. Use ?? for wildcards");
        var maxResultsOpt = new Option<int>("--max-results", () => 50, "Maximum number of matches to return");
        findPatternCommand.AddArgument(patternArg);
        findPatternCommand.AddOption(maxResultsOpt);
        findPatternCommand.SetHandler((string pattern, int maxResults, string? target, string? config) =>
        {
            Run(s => FindPatternTool.FindPattern(s.Rom, pattern, maxResults), target, config);
        }, patternArg, maxResultsOpt, targetOption, configOption);

        var findStringsCommand = new Command("find-strings", "Search for runs of printable ASCII or ATASCII characters");
        var minLenOpt = new Option<int>("--min-length", () => 4, "Minimum string length");
        var encodingOpt = new Option<string>("--encoding", () => "ascii", "String encoding: ascii or atascii");
        var filterOpt = new Option<string?>("--filter", "Optional substring filter");
        var fsMaxOpt = new Option<int>("--max-results", () => 50, "Maximum number of results");
        findStringsCommand.AddOption(minLenOpt);
        findStringsCommand.AddOption(encodingOpt);
        findStringsCommand.AddOption(filterOpt);
        findStringsCommand.AddOption(fsMaxOpt);
        findStringsCommand.SetHandler((int minLen, string encoding, string? filter, int maxRes, string? target, string? config) =>
        {
            Run(s => StringSearchTool.FindStrings(s.Rom, minLen, encoding, filter, maxRes), target, config);
        }, minLenOpt, encodingOpt, filterOpt, fsMaxOpt, targetOption, configOption);

        // ═══════════════════════════════════════════════════════════════════
        // ANALYSIS
        // ═══════════════════════════════════════════════════════════════════

        var analyzeCommand = new Command("analyze", "Perform multi-pass analysis to build reference graph and identify code/data regions");
        var anStartOpt = new Option<string?>("--start-address", "Starting address for analysis (hex)");
        var anBytesOpt = new Option<int?>("--bytes", "Number of bytes to analyze");
        var anFormatOpt = new Option<string>("--format", () => "summary", "Output format: summary, graph, labels, or full");
        analyzeCommand.AddOption(anStartOpt);
        analyzeCommand.AddOption(anBytesOpt);
        analyzeCommand.AddOption(anFormatOpt);
        analyzeCommand.SetHandler((string? startAddr, int? bytes, string format, string? target, string? config) =>
        {
            Run(s => AnalysisTools.AnalyzeDisassembly(s.Rom, s.Symbols, s.ZeroPage, startAddr, bytes, format), target, config);
        }, anStartOpt, anBytesOpt, anFormatOpt, targetOption, configOption);

        var probeCommand = new Command("probe", "Analyze a memory range to identify data type");
        var probeStartArg = new Argument<string>("start", "Start address (hex)");
        var probeEndArg = new Argument<string>("end", "End address (hex, inclusive)");
        probeCommand.AddArgument(probeStartArg);
        probeCommand.AddArgument(probeEndArg);
        probeCommand.SetHandler((string start, string end, string? target, string? config) =>
        {
            Run(s => AnalysisTools.ProbeData(s.Rom, start, end), target, config);
        }, probeStartArg, probeEndArg, targetOption, configOption);

        var callgraphCommand = new Command("callgraph", "Generate a call graph showing subroutine relationships");
        var cgStartOpt = new Option<string?>("--start-address", "Starting address for call graph root (hex)");
        var cgDepthOpt = new Option<int>("--depth", () => 3, "Maximum call depth");
        var cgFormatOpt = new Option<string>("--format", () => "mermaid", "Output format: mermaid or text");
        callgraphCommand.AddOption(cgStartOpt);
        callgraphCommand.AddOption(cgDepthOpt);
        callgraphCommand.AddOption(cgFormatOpt);
        callgraphCommand.SetHandler((string? startAddr, int depth, string format, string? target, string? config) =>
        {
            Run(s => AnalysisTools.GenerateCallGraph(s.Rom, s.Symbols, s.ZeroPage, startAddr, depth, format), target, config);
        }, cgStartOpt, cgDepthOpt, cgFormatOpt, targetOption, configOption);

        var coverageCommand = new Command("coverage", "Analyze code coverage — which bytes are executed vs data");
        var covStartArg = new Argument<string>("start", "Start address (hex)");
        var covEndArg = new Argument<string>("end", "End address (hex, inclusive)");
        coverageCommand.AddArgument(covStartArg);
        coverageCommand.AddArgument(covEndArg);
        coverageCommand.SetHandler((string start, string end, string? target, string? config) =>
        {
            Run(s => AnalysisTools.AnalyzeCoverage(s.Rom, start, end), target, config);
        }, covStartArg, covEndArg, targetOption, configOption);

        // ═══════════════════════════════════════════════════════════════════
        // CONTROL FLOW & XREF
        // ═══════════════════════════════════════════════════════════════════

        var traceCommand = new Command("trace", "Statically trace execution from a starting address");
        var traceAddrArg = new Argument<string>("address", "Starting memory address");
        var traceDepthOpt = new Option<int>("--max-depth", () => 5, "Maximum call depth");
        var traceBudgetOpt = new Option<int>("--max-instructions", () => 500, "Instruction budget");
        traceCommand.AddArgument(traceAddrArg);
        traceCommand.AddOption(traceDepthOpt);
        traceCommand.AddOption(traceBudgetOpt);
        traceCommand.SetHandler((string addr, int depth, int budget, string? target, string? config) =>
        {
            Run(s => ControlFlowTool.TraceControlFlow(s.Rom, s.Symbols, s.ZeroPage, addr, depth, budget), target, config);
        }, traceAddrArg, traceDepthOpt, traceBudgetOpt, targetOption, configOption);

        var xrefCommand = new Command("xref", "Find locations that reference a target address");
        var xrefAddrArg = new Argument<string>("address", "Target address to cross-reference");
        xrefCommand.AddArgument(xrefAddrArg);
        xrefCommand.SetHandler((string addr, string? target, string? config) =>
        {
            Run(s => XRefTool.XRef(s.Rom, s.Symbols, s.ZeroPage, addr), target, config);
        }, xrefAddrArg, targetOption, configOption);

        // ═══════════════════════════════════════════════════════════════════
        // SYMBOL COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        var symbolCommand = new Command("symbol", "Manage symbol labels");
        var symbolDefineCommand = new Command("define", "Add or update a named label for a memory address");
        var symAddrArg = new Argument<string>("address", "Memory address");
        var symLabelArg = new Argument<string>("label", "Label to define");
        var symCommentOpt = new Option<string?>("--comment", "Optional comment");
        symbolDefineCommand.AddArgument(symAddrArg);
        symbolDefineCommand.AddArgument(symLabelArg);
        symbolDefineCommand.AddOption(symCommentOpt);
        symbolDefineCommand.SetHandler((string addr, string label, string? comment, string? target, string? config) =>
        {
            Run(s => SymbolTools.DefineSymbol(s.Rom, s.Symbols, s.Persistence, addr, label, comment), target, config);
        }, symAddrArg, symLabelArg, symCommentOpt, targetOption, configOption);
        symbolCommand.AddCommand(symbolDefineCommand);

        var symbolRemoveCommand = new Command("remove", "Remove a user-defined symbol");
        var symRemoveAddrArg = new Argument<string>("address", "Address of the symbol to remove");
        symbolRemoveCommand.AddArgument(symRemoveAddrArg);
        symbolRemoveCommand.SetHandler((string addr, string? target, string? config) =>
        {
            Run(s => SymbolTools.RemoveSymbol(s.Rom, s.Symbols, s.Persistence, addr), target, config);
        }, symRemoveAddrArg, targetOption, configOption);
        symbolCommand.AddCommand(symbolRemoveCommand);

        var symbolLookupCommand = new Command("lookup", "Look up the symbol entry for an address");
        var symLookupAddrArg = new Argument<string>("address", "Memory address");
        symbolLookupCommand.AddArgument(symLookupAddrArg);
        symbolLookupCommand.SetHandler((string addr, string? target, string? config) =>
        {
            Run(s => SymbolTools.LookupSymbol(s.Rom, s.Symbols, addr), target, config);
        }, symLookupAddrArg, targetOption, configOption);
        symbolCommand.AddCommand(symbolLookupCommand);

        var symbolListCommand = new Command("list", "List symbols in the symbol table");
        var symListHwOpt = new Option<bool>("--include-hardware", "Include built-in hardware symbols");
        var symListFilterOpt = new Option<string?>("--filter", "Optional substring filter");
        symbolListCommand.AddOption(symListHwOpt);
        symbolListCommand.AddOption(symListFilterOpt);
        symbolListCommand.SetHandler((bool includeHw, string? filter, string? target, string? config) =>
        {
            Run(s => SymbolTools.ListSymbols(s.Rom, s.Symbols, includeHw, filter), target, config);
        }, symListHwOpt, symListFilterOpt, targetOption, configOption);
        symbolCommand.AddCommand(symbolListCommand);

        var symbolSetCommand = new Command("set", "Enable or disable groups of built-in symbols");
        var setHwOpt = new Option<bool?>("--hardware", "Enable hardware register symbols");
        var setOsVarsOpt = new Option<bool?>("--os-variables", "Enable OS zero-page variable symbols");
        var setOsRomOpt = new Option<bool?>("--os-rom", "Enable OS ROM entry point symbols");
        var setUserOpt = new Option<bool?>("--user-labels", "Enable user-defined symbols");
        symbolSetCommand.AddOption(setHwOpt);
        symbolSetCommand.AddOption(setOsVarsOpt);
        symbolSetCommand.AddOption(setOsRomOpt);
        symbolSetCommand.AddOption(setUserOpt);
        symbolSetCommand.SetHandler((bool? hw, bool? osVars, bool? osRom, bool? user) =>
        {
            var s = CreateCliSession();
            Console.WriteLine(SymbolTools.SetSymbols(s.Symbols, s.ZeroPage, s.Persistence, hw, osVars, osRom, user));
        }, setHwOpt, setOsVarsOpt, setOsRomOpt, setUserOpt);
        symbolCommand.AddCommand(symbolSetCommand);

        // ═══════════════════════════════════════════════════════════════════
        // SEGMENT COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        var segmentCommand = new Command("segment", "Manage memory segments for segment-aware disassembly");
        var segDefineCommand = new Command("define", "Define a memory segment by type");
        var segNameArg = new Argument<string>("name", "Segment name (e.g., boot_loader, main_code)");
        var segTypeArg = new Argument<string>("type", "Segment type: code, data, graphics, text, or zero_page");
        var segStartArg = new Argument<string>("start", "Start address (hex)");
        var segEndArg = new Argument<string>("end", "End address (hex, inclusive)");
        var segCommentOpt = new Option<string?>("--comment", "Optional comment");
        segDefineCommand.AddArgument(segNameArg);
        segDefineCommand.AddArgument(segTypeArg);
        segDefineCommand.AddArgument(segStartArg);
        segDefineCommand.AddArgument(segEndArg);
        segDefineCommand.AddOption(segCommentOpt);
        segDefineCommand.SetHandler((string name, string type, string start, string end, string? comment) =>
        {
            var s = CreateCliSession();
            Console.WriteLine(SegmentTools.DefineSegment(s.Segments, s.Persistence, name, type, start, end, comment));
        }, segNameArg, segTypeArg, segStartArg, segEndArg, segCommentOpt);
        segmentCommand.AddCommand(segDefineCommand);

        var segRemoveCommand = new Command("remove", "Remove a defined memory segment by name");
        var segRemoveNameArg = new Argument<string>("name", "Name of the segment to remove");
        segRemoveCommand.AddArgument(segRemoveNameArg);
        segRemoveCommand.SetHandler((string name) =>
        {
            var s = CreateCliSession();
            Console.WriteLine(SegmentTools.RemoveSegment(s.Segments, s.Persistence, name));
        }, segRemoveNameArg);
        segmentCommand.AddCommand(segRemoveCommand);

        var segListCommand = new Command("list", "List all defined memory segments");
        segListCommand.SetHandler(() =>
        {
            var s = CreateCliSession();
            Console.WriteLine(SegmentTools.ListSegments(s.Segments));
        });
        segmentCommand.AddCommand(segListCommand);

        var segClearCommand = new Command("clear", "Clear all defined memory segments");
        segClearCommand.SetHandler(() =>
        {
            var s = CreateCliSession();
            Console.WriteLine(SegmentTools.ClearSegments(s.Segments, s.Persistence));
        });
        segmentCommand.AddCommand(segClearCommand);

        var segLinkerCommand = new Command("linker-config", "Generate a cc65 linker configuration file");
        var linkerOutputArg = new Argument<string>("output", "Output path for the linker config file");
        segLinkerCommand.AddArgument(linkerOutputArg);
        segLinkerCommand.SetHandler((string output) =>
        {
            var s = CreateCliSession();
            Console.WriteLine(SegmentTools.GenerateLinkerConfig(s.Segments, output));
        }, linkerOutputArg);
        segmentCommand.AddCommand(segLinkerCommand);

        // ═══════════════════════════════════════════════════════════════════
        // ZERO PAGE COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        var zpCommand = new Command("zero-page", "Manage zero page annotations");
        var zpAnnotateCommand = new Command("annotate", "Add or update a zero page annotation");
        var zpAddrArg = new Argument<string>("address", "Zero page address");
        var zpLabelArg = new Argument<string>("label", "Label to assign");
        var zpCommentOpt = new Option<string?>("--comment", "Optional comment");
        zpAnnotateCommand.AddArgument(zpAddrArg);
        zpAnnotateCommand.AddArgument(zpLabelArg);
        zpAnnotateCommand.AddOption(zpCommentOpt);
        zpAnnotateCommand.SetHandler((string addr, string label, string? comment, string? target, string? config) =>
        {
            Run(s => ZeroPageTool.AnnotateZeroPage(s.Rom, s.ZeroPage, s.Persistence, addr, label, comment), target, config);
        }, zpAddrArg, zpLabelArg, zpCommentOpt, targetOption, configOption);
        zpCommand.AddCommand(zpAnnotateCommand);

        var zpShowCommand = new Command("show", "Display zero page annotations");
        var zpShowAllOpt = new Option<bool>("--all", "Show all 256 bytes of zero page");
        zpShowCommand.AddOption(zpShowAllOpt);
        zpShowCommand.SetHandler((bool all, string? target, string? config) =>
        {
            Run(s => ZeroPageTool.ShowZeroPageMap(s.Rom, s.ZeroPage, all), target, config);
        }, zpShowAllOpt, targetOption, configOption);
        zpCommand.AddCommand(zpShowCommand);

        // ═══════════════════════════════════════════════════════════════════
        // LABELS COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        var labelsCommand = new Command("labels", "Load or save label files");
        var labelsLoadCommand = new Command("load", "Load labels from a sidecar file");
        var loadLabelsPathArg = new Argument<string>("path", "Path to the sidecar file (*.atarihacker.json)");
        labelsLoadCommand.AddArgument(loadLabelsPathArg);
        labelsLoadCommand.SetHandler((string path) =>
        {
            var s = CreateCliSession();
            Console.WriteLine(SymbolTools.LoadLabels(s.Symbols, s.ZeroPage, s.Segments, path));
        }, loadLabelsPathArg);
        labelsCommand.AddCommand(labelsLoadCommand);

        var labelsSaveCommand = new Command("save", "Save current labels and segments to a sidecar file");
        var saveLabelsPathOpt = new Option<string?>("--output", "Optional output path (defaults to ROM path + .atarihacker.json)");
        labelsSaveCommand.AddOption(saveLabelsPathOpt);
        labelsSaveCommand.SetHandler((string? output, string? target, string? config) =>
        {
            Run(s => SymbolTools.SaveLabels(s.Persistence, output), target, config);
        }, saveLabelsPathOpt, targetOption, configOption);
        labelsCommand.AddCommand(labelsSaveCommand);

        // ═══════════════════════════════════════════════════════════════════
        // ATR COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        var atrCommand = new Command("atr", "ATR disk image operations");

        var atrInfoCommand = new Command("info", "Display structural information about an ATR disk image");
        var atrInfoPathArg = new Argument<string>("path", "Path to the ATR file");
        atrInfoCommand.AddArgument(atrInfoPathArg);
        atrInfoCommand.SetHandler((string path) => Console.WriteLine(AtrTools.AtrInfo(path)), atrInfoPathArg);
        atrCommand.AddCommand(atrInfoCommand);

        var atrHeaderCommand = new Command("header", "Display the ATR header fields");
        var atrHeaderPathArg = new Argument<string>("path", "Path to the ATR file");
        atrHeaderCommand.AddArgument(atrHeaderPathArg);
        atrHeaderCommand.SetHandler((string path) => Console.WriteLine(AtrTools.AtrHeader(path)), atrHeaderPathArg);
        atrCommand.AddCommand(atrHeaderCommand);

        var atrDirCommand = new Command("directory", "List the directory of a DOS-formatted ATR disk image");
        var atrDirPathArg = new Argument<string>("path", "Path to the ATR file");
        atrDirCommand.AddArgument(atrDirPathArg);
        atrDirCommand.SetHandler((string path) => Console.WriteLine(AtrTools.ListAtrDirectory(path)), atrDirPathArg);
        atrCommand.AddCommand(atrDirCommand);

        var atrCreateCommand = new Command("create", "Create a new ATR disk image from scratch");
        var atrCreateOutputArg = new Argument<string>("output", "Output path");
        var atrCreateSectorsArg = new Argument<int>("sectors", "Number of sectors (720, 1040, etc.)");
        var atrCreateDensityArg = new Argument<string>("density", "Density: sd (single), dd (double), ed (enhanced)");
        atrCreateCommand.AddArgument(atrCreateOutputArg);
        atrCreateCommand.AddArgument(atrCreateSectorsArg);
        atrCreateCommand.AddArgument(atrCreateDensityArg);
        atrCreateCommand.SetHandler((string output, int sectors, string density) =>
            Console.WriteLine(AtrWriteTools.CreateAtr(output, sectors, density)),
            atrCreateOutputArg, atrCreateSectorsArg, atrCreateDensityArg);
        atrCommand.AddCommand(atrCreateCommand);

        var atrExtractCommand = new Command("extract", "Extract a file from an ATR image and save to disk");
        var atrExtractPathArg = new Argument<string>("path", "Path to the ATR file");
        var atrExtractNameArg = new Argument<string>("name", "Atari DOS filename to extract");
        var atrExtractOutputArg = new Argument<string>("output", "Output path");
        atrExtractCommand.AddArgument(atrExtractPathArg);
        atrExtractCommand.AddArgument(atrExtractNameArg);
        atrExtractCommand.AddArgument(atrExtractOutputArg);
        atrExtractCommand.SetHandler((string path, string name, string output) =>
            Console.WriteLine(AtrWriteTools.ExtractAtrFile(path, name, output)),
            atrExtractPathArg, atrExtractNameArg, atrExtractOutputArg);
        atrCommand.AddCommand(atrExtractCommand);

        var atrInjectCommand = new Command("inject", "Inject a file into an ATR image, replacing existing entry");
        var atrInjectPathArg = new Argument<string>("path", "Path to the ATR file");
        var atrInjectNameArg = new Argument<string>("name", "Atari DOS filename to replace");
        var atrInjectInputArg = new Argument<string>("input", "Path to the input file");
        atrInjectCommand.AddArgument(atrInjectPathArg);
        atrInjectCommand.AddArgument(atrInjectNameArg);
        atrInjectCommand.AddArgument(atrInjectInputArg);
        atrInjectCommand.SetHandler((string path, string name, string input) =>
            Console.WriteLine(AtrWriteTools.InjectAtrFile(path, name, input)),
            atrInjectPathArg, atrInjectNameArg, atrInjectInputArg);
        atrCommand.AddCommand(atrInjectCommand);

        var atrWriteSectorCommand = new Command("write-sector", "Write raw data to a specific sector");
        var atrWsPathArg = new Argument<string>("path", "Path to the ATR file");
        var atrWsSectorArg = new Argument<string>("sector", "Sector number (1-based)");
        var atrWsInputArg = new Argument<string>("input", "Path to the input file");
        atrWriteSectorCommand.AddArgument(atrWsPathArg);
        atrWriteSectorCommand.AddArgument(atrWsSectorArg);
        atrWriteSectorCommand.AddArgument(atrWsInputArg);
        atrWriteSectorCommand.SetHandler((string path, string sector, string input) =>
            Console.WriteLine(AtrWriteTools.WriteAtrSector(path, sector, input)),
            atrWsPathArg, atrWsSectorArg, atrWsInputArg);
        atrCommand.AddCommand(atrWriteSectorCommand);

        var atrWriteFileCommand = new Command("write-file", "Write a file to an ATR image, creating a new directory entry");
        var atrWfPathArg = new Argument<string>("path", "Path to the ATR file");
        var atrWfNameArg = new Argument<string>("name", "Atari DOS filename (8.3 format)");
        var atrWfInputArg = new Argument<string>("input", "Path to the input file");
        var atrWfStartOpt = new Option<string?>("--start-sector", "Starting sector for the file data (hex)");
        atrWriteFileCommand.AddArgument(atrWfPathArg);
        atrWriteFileCommand.AddArgument(atrWfNameArg);
        atrWriteFileCommand.AddArgument(atrWfInputArg);
        atrWriteFileCommand.AddOption(atrWfStartOpt);
        atrWriteFileCommand.SetHandler((string path, string name, string input, string? startSector) =>
            Console.WriteLine(AtrWriteTools.WriteAtrFile(path, name, input, startSector)),
            atrWfPathArg, atrWfNameArg, atrWfInputArg, atrWfStartOpt);
        atrCommand.AddCommand(atrWriteFileCommand);

        var atrBootSectorCommand = new Command("analyze-boot", "Decode the boot sector header from an ATR disk image");
        var atrBootPathArg = new Argument<string>("path", "Path to the ATR file");
        atrBootSectorCommand.AddArgument(atrBootPathArg);
        atrBootSectorCommand.SetHandler((string path) => Console.WriteLine(AtrTools.AnalyzeBootSector(path)), atrBootPathArg);
        atrCommand.AddCommand(atrBootSectorCommand);

        var atrSectorDumpCommand = new Command("sector-dump", "Hex dump sectors from an ATR disk image");
        var atrSdPathArg = new Argument<string>("path", "Path to the ATR file");
        var atrSdSectorArg = new Argument<string>("sector", "Starting sector number (1-based)");
        var atrSdCountOpt = new Option<int>("--count", () => 1, "Number of consecutive sectors to dump");
        atrSectorDumpCommand.AddArgument(atrSdPathArg);
        atrSectorDumpCommand.AddArgument(atrSdSectorArg);
        atrSectorDumpCommand.AddOption(atrSdCountOpt);
        atrSectorDumpCommand.SetHandler((string path, string sector, int count) =>
            Console.WriteLine(AtrTools.SectorDump(path, sector, count)),
            atrSdPathArg, atrSdSectorArg, atrSdCountOpt);
        atrCommand.AddCommand(atrSectorDumpCommand);

        var atrSearchBootCommand = new Command("search-boot", "Scan boot sectors across multiple ATR images");
        var atrSbPathsArg = new Argument<string[]>("paths", "Paths to ATR files to scan");
        var atrSbPatternOpt = new Option<string?>("--pattern", "Hex byte pattern with ?? wildcards");
        var atrSbModeOpt = new Option<string>("--mode", () => "pattern", "Search mode: pattern or diff");
        atrSearchBootCommand.AddArgument(atrSbPathsArg);
        atrSearchBootCommand.AddOption(atrSbPatternOpt);
        atrSearchBootCommand.AddOption(atrSbModeOpt);
        atrSearchBootCommand.SetHandler((string[] paths, string? pattern, string mode) =>
            Console.WriteLine(AtrTools.SearchBootSector(paths, pattern, mode)),
            atrSbPathsArg, atrSbPatternOpt, atrSbModeOpt);
        atrCommand.AddCommand(atrSearchBootCommand);

        var atrFsCommand = new Command("filesystem", "Define a custom filesystem layout for non-DOS ATR images");
        var atrFsPathArg = new Argument<string>("path", "Path to the ATR file");
        var atrFsDirOffsetArg = new Argument<string>("directory-offset", "File offset of the directory table (hex)");
        var atrFsEntrySizeArg = new Argument<int>("entry-size", "Size of each directory entry in bytes");
        var atrFsFnLenArg = new Argument<int>("filename-length", "Length of the filename field");
        var atrFsExtLenArg = new Argument<int>("extension-length", "Length of the extension field");
        var atrFsStartOffArg = new Argument<int>("start-sector-offset", "Offset of start sector in entry");
        var atrFsSectorCountOffArg = new Argument<int>("sector-count-offset", "Offset of sector count in entry");
        atrFsCommand.AddArgument(atrFsPathArg);
        atrFsCommand.AddArgument(atrFsDirOffsetArg);
        atrFsCommand.AddArgument(atrFsEntrySizeArg);
        atrFsCommand.AddArgument(atrFsFnLenArg);
        atrFsCommand.AddArgument(atrFsExtLenArg);
        atrFsCommand.AddArgument(atrFsStartOffArg);
        atrFsCommand.AddArgument(atrFsSectorCountOffArg);
        atrFsCommand.SetHandler((string path, string dirOff, int entrySize, int fnLen, int extLen, int startOff, int sectorCountOff) =>
            Console.WriteLine(AtrWriteTools.DefineFilesystem(path, dirOff, entrySize, fnLen, extLen, startOff, sectorCountOff)),
            atrFsPathArg, atrFsDirOffsetArg, atrFsEntrySizeArg, atrFsFnLenArg, atrFsExtLenArg, atrFsStartOffArg, atrFsSectorCountOffArg);
        atrCommand.AddCommand(atrFsCommand);

        // ═══════════════════════════════════════════════════════════════════
        // DIFF
        // ═══════════════════════════════════════════════════════════════════

        var diffCommand = new Command("diff", "Compare two ROM or ATR files");
        var diffFile1Arg = new Argument<string>("file1", "Path to the first file");
        var diffFile2Arg = new Argument<string>("file2", "Path to the second file");
        var diffFormatOpt = new Option<string>("--format", () => "summary", "Format: summary, verbose, or hex");
        diffCommand.AddArgument(diffFile1Arg);
        diffCommand.AddArgument(diffFile2Arg);
        diffCommand.AddOption(diffFormatOpt);
        diffCommand.SetHandler((string f1, string f2, string format) =>
            Console.WriteLine(AnalysisTools.DiffRoms(f1, f2, format)),
            diffFile1Arg, diffFile2Arg, diffFormatOpt);

        // ═══════════════════════════════════════════════════════════════════
        // UTILITY COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        var hexToDecCommand = new Command("hex-to-decimal", "Convert a hexadecimal value to decimal");
        var h2dHexArg = new Argument<string>("hex", "Hex value with or without $ or 0x prefix");
        hexToDecCommand.AddArgument(h2dHexArg);
        hexToDecCommand.SetHandler((string hex) => Console.WriteLine(ConversionTools.HexToDecimal(hex)), h2dHexArg);

        var decToHexCommand = new Command("decimal-to-hex", "Convert a decimal integer to hexadecimal");
        var d2hDecArg = new Argument<int>("value", "Decimal integer to convert");
        decToHexCommand.AddArgument(d2hDecArg);
        decToHexCommand.SetHandler((int value) => Console.WriteLine(ConversionTools.DecimalToHex(value)), d2hDecArg);

        // ═══════════════════════════════════════════════════════════════════
        // REGISTER ALL COMMANDS
        // ═══════════════════════════════════════════════════════════════════

        rootCommand.AddCommand(loadCommand);
        rootCommand.AddCommand(infoCommand);
        rootCommand.AddCommand(scriptCommand);
        rootCommand.AddCommand(disassembleCommand);
        rootCommand.AddCommand(hexDumpCommand);
        rootCommand.AddCommand(findPatternCommand);
        rootCommand.AddCommand(findStringsCommand);
        rootCommand.AddCommand(analyzeCommand);
        rootCommand.AddCommand(probeCommand);
        rootCommand.AddCommand(callgraphCommand);
        rootCommand.AddCommand(coverageCommand);
        rootCommand.AddCommand(traceCommand);
        rootCommand.AddCommand(xrefCommand);
        rootCommand.AddCommand(symbolCommand);
        rootCommand.AddCommand(segmentCommand);
        rootCommand.AddCommand(zpCommand);
        rootCommand.AddCommand(labelsCommand);
        rootCommand.AddCommand(atrCommand);
        rootCommand.AddCommand(diffCommand);
        rootCommand.AddCommand(hexToDecCommand);
        rootCommand.AddCommand(decToHexCommand);

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Holds all session-related state for a single CLI invocation.
    /// </summary>
    private sealed record CliSession(
        RomSession Rom,
        SymbolTable Symbols,
        ZeroPageMap ZeroPage,
        SegmentManager Segments,
        SessionPersistence Persistence);
}
