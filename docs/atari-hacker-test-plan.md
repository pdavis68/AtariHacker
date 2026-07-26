# AtariHacker Test Plan

> Unit test checklist organized by class/file being tested.
> Use this to track test implementation progress.

---

## Atari/AtariHardwareMap.cs

- [ ] `Populate` — verifies all hardware symbols are added to the SymbolTable with correct addresses and labels
- [ ] `PopulateZeroPage` — verifies all zero-page OS variables are added to the ZeroPageMap with correct addresses and labels
- [ ] `TryGetHardwareSymbol` — returns correct SymbolEntry for known hardware addresses
- [ ] `TryGetHardwareSymbol` — returns null for non-hardware addresses
- [ ] Hardware symbol dictionary is read-only and cannot be modified externally

## Atari/Opcodes6502.cs

- [ ] `Table` contains exactly 256 entries (all opcodes 0x00–0xFF)
- [ ] `Table` correctly maps known opcodes (e.g., 0xA9 → LDA immediate)
- [ ] `Table` correctly marks illegal opcodes as IsOfficial = false
- [ ] `Table` entries have correct addressing modes and byte counts
- [ ] All official opcodes have correct mnemonic strings

## Atari/AtrParser.cs

- [ ] `IsAtr` — returns true for valid ATR header (magic bytes $0296)
- [ ] `IsAtr` — returns false for data shorter than 16 bytes
- [ ] `IsAtr` — returns false for invalid magic bytes
- [ ] `ParseGeometry` — correctly parses SD (128-byte sectors, 720 sectors)
- [ ] `ParseGeometry` — correctly parses DD (256-byte sectors, 720 sectors)
- [ ] `ParseGeometry` — correctly parses ED (128-byte sectors, 1040 sectors)
- [ ] `ParseGeometry` — throws InvalidDataException for unsupported sector sizes
- [ ] `ParseGeometry` — throws InvalidDataException for non-ATR data
- [ ] `ReadSector` — returns correct bytes for a given sector number
- [ ] `ReadSector` — throws ArgumentOutOfRangeException for sector 0
- [ ] `ReadSector` — throws ArgumentOutOfRangeException for sector > count
- [ ] `ReadSector` — handles sector 1–3 with 128-byte length on DD images
- [ ] `ReadDirectory` — parses directory entries correctly from sector data
- [ ] `ReadDirectory` — skips phantom entries with impossible sector counts
- [ ] `ReadDirectory` — correctly identifies deleted/locked/binary flags
- [ ] `ExtractFile` — follows sector chain and reconstructs file data
- [ ] `ExtractFile` — throws InvalidDataException on sector chain loop
- [ ] `ExtractBootSectors` — extracts first 3 sectors correctly
- [ ] `FreeSegmentCount` — returns correct free sector count from VTOC
- [ ] `HasDosFilesystem` — detects DOS 2.x filesystem correctly
- [ ] `HasDosFilesystem` — returns false for non-DOS images
- [ ] `GetSectorChain` — follows sector links correctly
- [ ] `SectorFileOffset` — computes correct file offset for any sector

## Atari/XexParser.cs

- [ ] `IsXex` — returns true for data starting with $FFFF
- [ ] `IsXex` — returns false for data not starting with $FFFF
- [ ] `ParseSegments` — parses a simple single-segment XEX correctly
- [ ] `ParseSegments` — parses a multi-segment XEX correctly
- [ ] `ParseMetadata` — extracts RunAddress from $02E0 segment
- [ ] `ParseMetadata` — extracts InitAddress from $02E2 segment
- [ ] `ParseMetadata` — returns empty segments for non-XEX data
- [ ] `FileOffsetToMemoryAddress` — converts file offset to memory address correctly
- [ ] `FileOffsetToMemoryAddress` — returns null for offset outside any segment
- [ ] `MemoryAddressToFileOffset` — converts memory address to file offset correctly
- [ ] `MemoryAddressToFileOffset` — returns null for address outside any segment

## Analysis/AccessType.cs

- [ ] Enum values are correctly defined (Read, Write, ReadWrite, Execute)
- [ ] All values are distinct

## Analysis/CallGraph.cs

- [ ] `BuildCallGraph` — returns empty graph for empty references
- [ ] `BuildCallGraph` — builds graph from subroutine entries
- [ ] `BuildCallGraph` — respects maxDepth parameter
- [ ] `BuildCallGraphFromData` — scans for JSR instructions correctly
- [ ] `BuildCallGraphFromData` — handles empty data gracefully
- [ ] `FormatMermaid` — produces valid Mermaid syntax
- [ ] `FormatText` — produces indented text tree
- [ ] `FormatMermaid` — handles empty graph

## Analysis/CodeCoverage.cs

- [ ] `AnalyzeCoverage` — returns zero result for null/empty data
- [ ] `AnalyzeCoverage` — correctly counts code vs data bytes
- [ ] `AnalyzeCoverage` — builds correct CoverageRegion list
- [ ] `AnalyzeCoverage` — detects orphaned code bytes
- [ ] `AnalyzeCoverage` — detects embedded data bytes
- [ ] `CoverageResult.ToCsv` — produces valid CSV output
- [ ] `CoverageResult.ToTsv` — produces valid TSV output
- [ ] `CoverageResult.ToKv` — produces valid key=value output

## Analysis/DataFlowAnalyzer.cs

- [ ] `TraceForward` — returns empty result for empty session data
- [ ] `TraceForward` — finds write-to-read chains for a target address
- [ ] `TraceForward` — respects instruction budget
- [ ] `TraceForward` — respects maxDepth
- [ ] `TraceBackward` — returns empty result for empty session data
- [ ] `TraceBackward` — finds read-to-write chains going backward
- [ ] `TraceBackward` — respects instruction budget
- [ ] `FindAllReferences` — finds all instructions referencing a target address
- [ ] `FindAllReferences` — returns empty list for unreferenced address
- [ ] `FindAllReferences` — handles null session data

## Analysis/DataFlowResult.cs

- [ ] `DataFlowLink` record — correctly stores all properties
- [ ] `DataFlowResult` record — correctly stores all properties

## Analysis/DataProber.cs

- [ ] `ProbeData` — returns "Invalid range" for null data
- [ ] `ProbeData` — returns "Invalid range" for start > end
- [ ] `ProbeData` — returns "Invalid range" for end >= data length
- [ ] `ProbeData` — detects ATASCII/ASCII strings (Heuristic 1)
- [ ] `ProbeData` — detects padding bytes $00/$FF/$1A (Heuristic 2)
- [ ] `ProbeData` — detects character set data (Heuristic 3)
- [ ] `ProbeData` — detects address/lookup tables (Heuristic 4)
- [ ] `ProbeData` — detects ANTIC display lists (Heuristic 5)
- [ ] `ProbeData` — detects sprite data (Heuristic 6)
- [ ] `ProbeData` — detects map data (Heuristic 7)
- [ ] `ProbeData` — returns "Unknown data" when no heuristic matches
- [ ] `ProbeResult.ToCsv` — produces valid CSV output
- [ ] `ProbeResult.ToTsv` — produces valid TSV output
- [ ] `ProbeResult.ToKv` — produces valid key=value output

## Analysis/DiffAnalyzer.cs

- [ ] `DiffBytes` — returns no differences for identical byte arrays
- [ ] `DiffBytes` — detects single byte difference
- [ ] `DiffBytes` — detects multiple byte differences
- [ ] `DiffBytes` — handles arrays of different lengths
- [ ] `DiffBytes` — builds correct DiffRegion list
- [ ] `FormatSummary` — produces correct summary text
- [ ] `FormatVerbose` — lists each byte difference
- [ ] `FormatHexDiff` — produces side-by-side hex diff

## Analysis/DisassemblyAnalyzer.cs

- [ ] `Analyze` — returns empty ReferenceGraph for null/empty data
- [ ] `Analyze` — detects JSR targets as subroutine entries
- [ ] `Analyze` — detects JMP absolute targets as jump targets
- [ ] `Analyze` — detects branch instruction targets
- [ ] `Analyze` — detects indirect JMP targets
- [ ] `Analyze` — collects absolute data references from LDA/STA/etc.
- [ ] `Analyze` — collects indirect data references from (zp),Y mode
- [ ] `Analyze` — detects boot sector header bytes as data references
- [ ] `TraceCodeRegions` — traces code from entry points
- [ ] `TraceCodeRegions` — stops at RTS/RTI/BRK terminators
- [ ] `TraceCodeRegions` — follows JSR into subroutines
- [ ] `GenerateLabels` — generates subroutine labels (sub_XXXX)
- [ ] `GenerateLabels` — generates data labels (data_XXXX)
- [ ] `GenerateLabels` — preserves user-defined symbols with priority
- [ ] `ReferenceGraph.Empty` — returns graph with all empty sets

## Analysis/PatternDetector.cs

- [ ] `DetectStateMachines` — detects LDA → ASL → TAX → JMP pattern
- [ ] `DetectStateMachines` — returns empty list for null/empty data
- [ ] `DetectStateMachines` — enumerates jump table entries
- [ ] `DetectJumpTables` — detects JMP (table,X) patterns
- [ ] `DetectJumpTables` — returns empty list for null/empty data
- [ ] `DetectCoroutines` — detects JSR/JMP coroutine chains
- [ ] `DetectCoroutines` — returns empty list for null/empty data
- [ ] `DetectInterruptHandlers` — detects NMI/RESET/IRQ vector handlers
- [ ] `DetectInterruptHandlers` — returns empty list for null/empty data

## Analysis/StackAnalyzer.cs

- [ ] `AnalyzeStack` — returns error result for null/empty data
- [ ] `AnalyzeStack` — returns error for address beyond data length
- [ ] `AnalyzeStack` — tracks PHA/PLA stack depth correctly
- [ ] `AnalyzeStack` — tracks JSR/RTS stack depth correctly
- [ ] `AnalyzeStack` — detects balanced stack (entry depth == exit depth)
- [ ] `AnalyzeStack` — detects unbalanced stack
- [ ] `AnalyzeStack` — detects stack underflow
- [ ] `AnalyzeStack` — detects loops in execution path
- [ ] `AnalyzeStack` — respects maxInstructions budget
- [ ] `FormatStackAnalysis` — formats text output correctly
- [ ] `FormatStackAnalysis` — formats CSV output correctly

## Analysis/StructureMatcher.cs

- [ ] `MatchAll` — returns empty list for empty templates
- [ ] `MatchAll` — matches a simple byte template
- [ ] `MatchAll` — matches a word_le template
- [ ] `MatchAll` — matches a word_be template
- [ ] `MatchAll` — applies validation constraints (min/max)
- [ ] `MatchAll` — applies range validation
- [ ] `MatchAll` — sorts results by confidence descending
- [ ] `MatchTemplate` — scans range and finds all matches
- [ ] `MatchTemplate` — respects step parameter
- [ ] `ComputeTemplateSize` — computes correct total size from fields

## Analysis/XRefEntry.cs

- [ ] `XRefEntry` record — correctly stores all properties

## Helpers/AddressParser.cs

- [ ] `ParseAddress` — parses decimal string correctly
- [ ] `ParseAddress` — parses hex with $ prefix
- [ ] `ParseAddress` — parses hex with 0x prefix
- [ ] `ParseAddress` — throws FormatException for empty string
- [ ] `ParseAddress` — throws FormatException for invalid format
- [ ] `ParseAddress` — throws FormatException for value > 0xFFFF
- [ ] `ParseOffset` — parses decimal and hex values correctly
- [ ] `ParseOffset` — throws FormatException for negative values
- [ ] `ParseZeroPageAddress` — parses values 0x00–0xFF correctly
- [ ] `ParseZeroPageAddress` — throws FormatException for value > 0xFF

## Helpers/AtasciiDecoder.cs

- [ ] `DecodeByte` — decodes standard ASCII range (0x20–0x5F) correctly
- [ ] `DecodeByte` — decodes inverse video (bit 7 set) correctly
- [ ] `DecodeByte` — decodes control codes (0x00–0x1F) to letters/numbers
- [ ] `DecodeByte` — returns '.' for unmappable bytes
- [ ] `Decode` — decodes a span of ATASCII bytes to string
- [ ] `Decode` — prefixes inverse characters with '~'
- [ ] `Decode` — handles empty span

## Helpers/Formatting.cs

- [ ] `HexByte` — formats byte as $XX
- [ ] `HexWord` — formats ushort as $XXXX
- [ ] `HexOffset` — formats int as XXXXXXXX
- [ ] `DisplayAddress` — formats address or "--------" for null
- [ ] `Printable` — returns character for printable ASCII, '.' otherwise
- [ ] `WithSymbol` — appends symbol in parentheses when present

## Helpers/OutputFormatter.cs

- [ ] `FormatCsv` — produces correct CSV with header row
- [ ] `FormatCsv` — escapes commas and quotes in values
- [ ] `FormatTsv` — produces correct TSV with header row
- [ ] `FormatTsv` — replaces tabs and newlines with spaces
- [ ] `FormatKv` — produces correct key=value pairs
- [ ] `FormatKv` — separates rows with blank lines
- [ ] All formatters handle empty rows gracefully

## Helpers/SymbolResolver.cs

- [ ] `Resolve` — returns label for known symbol table entry
- [ ] `Resolve` — returns zero-page label for address <= 0xFF
- [ ] `Resolve` — returns null when OsVariables group is disabled
- [ ] `Resolve` — returns null for unknown address
- [ ] `ResolveEntry` — returns SymbolEntry for known address
- [ ] `ResolveEntry` — returns null for unknown address

## Helpers/VerboseContext.cs

- [ ] `GetMetadata` — returns empty string when disabled
- [ ] `GetMetadata` — includes execution time when enabled
- [ ] `GetMetadata` — includes bytes processed when set
- [ ] `GetMetadata` — includes passes completed when > 0
- [ ] `GetMetadata` — includes confidence when set
- [ ] `GetMetadata` — output lines start with '# '

## Helpers/XexAddressResolver.cs

- [ ] `ResolveFileOffset` — uses overrideStartAddress when provided
- [ ] `ResolveFileOffset` — uses XexParser when segments are available
- [ ] `ResolveFileOffset` — uses session.BaseAddress when set
- [ ] `ResolveFileOffset` — falls back to file offset as memory address
- [ ] `ResolveFileOffset` — returns null for offset > 0xFFFF with no mapping
- [ ] `ResolveMemoryAddress` — uses XexParser when segments are available
- [ ] `ResolveMemoryAddress` — uses session.BaseAddress when set
- [ ] `ResolveMemoryAddress` — falls back to direct mapping
- [ ] `ResolveMemoryAddress` — returns null for out-of-range address

## State/RomSession.cs

- [ ] `IsLoaded` — returns false when Data is null
- [ ] `IsLoaded` — returns true when Data is not null
- [ ] `Length` — returns Data.Length or 0 when Data is null
- [ ] `Load` — sets FilePath, Data, and clears metadata
- [ ] `ClearMetadata` — resets all optional fields to null

## State/SymbolTable.cs

- [ ] `IsSymbolEnabled` — returns true for enabled user-defined symbol
- [ ] `IsSymbolEnabled` — returns false when UserLabels group is disabled
- [ ] `IsSymbolEnabled` — returns true for enabled hardware symbol
- [ ] `IsSymbolEnabled` — returns false when hardware group is disabled
- [ ] `IsSymbolEnabled` — returns false for unknown address
- [ ] `GetOrderedSymbols` — returns symbols sorted by address then label
- [ ] `EnabledGroups` — defaults to SymbolGroup.All

## State/ZeroPageMap.cs

- [ ] Can add and retrieve SymbolEntry by byte key
- [ ] `TryGetValue` — returns false for unknown key

## State/SegmentManager.cs

- [ ] `Define` — adds a new segment
- [ ] `Define` — replaces existing segment with same name
- [ ] `Remove` — removes segment by name
- [ ] `Remove` — does nothing for non-existent name
- [ ] `Clear` — removes all segments
- [ ] `Classify` — returns correct SegmentType for address in segment
- [ ] `Classify` — returns null for address not in any segment
- [ ] `IsAddressInRange` — returns true for address in named segment
- [ ] `IsAddressInRange` — returns false for address not in named segment
- [ ] `GetSegmentName` — returns correct segment name for address
- [ ] `GetSegmentName` — returns null for address not in any segment
- [ ] `HasOverlaps` — detects overlapping segments
- [ ] `HasOverlaps` — returns false for non-overlapping segments
- [ ] `FindGaps` — finds gaps between segments
- [ ] `FindGaps` — returns empty list for no segments
- [ ] `GetOrderedSegments` — returns segments sorted by start then name

## State/SessionPersistence.cs

- [ ] `GetSidecarPath` — returns path with .atarihacker.json suffix
- [ ] `GetSidecarPath` — handles paths without directory component
- [ ] `ComputeHash` — returns SHA-256 hex string for non-empty data
- [ ] `ComputeHash` — returns null for null/empty data

## State/PatternLibrary.cs

- [ ] `Add` — adds a new pattern entry
- [ ] `Add` — throws InvalidOperationException for duplicate name
- [ ] `Remove` — removes pattern by name, returns true
- [ ] `Remove` — returns false for non-existent name
- [ ] `Find` — finds pattern by name (case-insensitive)
- [ ] `Find` — returns null for non-existent name
- [ ] `Query` — filters by tag
- [ ] `Query` — filters by category
- [ ] `Query` — filters by text query (name and description)
- [ ] `Query` — combines multiple filters
- [ ] `Query` — returns all patterns when no filters specified

## State/StructureTemplate.cs

- [ ] `StructureLibrary.Add` — adds a new template
- [ ] `StructureLibrary.Add` — throws InvalidOperationException for duplicate name
- [ ] `StructureLibrary.Remove` — removes template by name
- [ ] `StructureLibrary.Find` — finds template by name (case-insensitive)
- [ ] `StructureLibrary.Query` — filters by tag, category, and text
- [ ] `StructureMatch` — stores all properties correctly

## Tools/DisassemblerTool.cs

- [ ] `Disassemble` — returns error when no ROM is loaded
- [ ] `Disassemble` — returns error for offset beyond ROM size
- [ ] `Disassemble` — disassembles known opcodes correctly
- [ ] `Disassemble` — treats illegal opcodes as .db data bytes
- [ ] `Disassemble` — formats listing output correctly
- [ ] `Disassemble` — formats ca65 output correctly
- [ ] `Disassemble` — formats atasm output correctly
- [ ] `Disassemble` — formats mac65 output correctly
- [ ] `Disassemble` — uses address override when provided
- [ ] `FormatOperand` — formats immediate operands with #
- [ ] `FormatOperand` — formats zero-page operands correctly
- [ ] `FormatOperand` — formats absolute operands correctly
- [ ] `FormatOperand` — formats indexed operands with ,X/,Y
- [ ] `FormatOperand` — formats indirect operands with ()
- [ ] `FormatOperand` — resolves symbols in operand display
- [ ] `ResolveOperandAddress` — computes correct target address
- [ ] `TryGetOfficialEntry` — returns true for official opcodes
- [ ] `TryGetOfficialEntry` — returns false for illegal opcodes

## Tools/HexDumpTool.cs

- [ ] `HexDump` — returns error when no ROM is loaded
- [ ] `HexDump` — returns error for offset beyond ROM size
- [ ] `HexDump` — returns error for zero/negative byte count
- [ ] `HexDump` — produces correct hex dump output
- [ ] `HexDump` — uses address override when provided
- [ ] `GenerateHexDump` — handles partial last row correctly
- [ ] `GenerateHexDump` — shows ASCII representation correctly

## Tools/FindPatternTool.cs

- [ ] `FindPattern` — returns error when no ROM is loaded
- [ ] `FindPattern` — returns error for empty pattern
- [ ] `FindPattern` — finds exact byte pattern matches
- [ ] `FindPattern` — handles wildcard (??) tokens correctly
- [ ] `FindPattern` — respects maxResults limit
- [ ] `FindPattern` — reports 0 matches for non-existent pattern
- [ ] `ParseToken` — parses hex byte correctly
- [ ] `ParseToken` — parses wildcard correctly
- [ ] `ParseToken` — throws FormatException for invalid token

## Tools/StringSearchTool.cs

- [ ] `FindStrings` — returns error when no ROM is loaded
- [ ] `FindStrings` — finds ASCII strings of minimum length
- [ ] `FindStrings` — finds ATASCII strings when encoding=atascii
- [ ] `FindStrings` — respects minLength parameter
- [ ] `FindStrings` — respects maxResults limit
- [ ] `FindStrings` — filters by substring when filter is provided
- [ ] `FindStrings` — returns "<none>" when no strings found

## Tools/FileTools.cs

- [ ] `BuildRomInfo` — includes file path and size
- [ ] `BuildRomInfo` — lists XEX segments when present
- [ ] `BuildRomInfo` — shows base address for raw binaries
- [ ] `BuildRomInfo` — indicates sidecar status
- [ ] `PopulateMetadata` — clears existing metadata
- [ ] `PopulateMetadata` — parses XEX segments for XEX files
- [ ] `PopulateMetadata` — does nothing for non-XEX files

## Tools/ConversionTools.cs

- [ ] `HexToDecimal` — converts hex string to decimal
- [ ] `HexToDecimal` — handles $ prefix
- [ ] `HexToDecimal` — handles 0x prefix
- [ ] `DecimalToHex` — converts decimal to hex string
- [ ] `DecimalToHex` — returns error for negative values

## Tools/SymbolTools.cs

- [ ] `DefineSymbol` — adds symbol to table
- [ ] `DefineSymbol` — returns error when no ROM is loaded
- [ ] `DefineSymbol` — validates label format
- [ ] `DefineSymbol` — warns when overwriting hardware symbol without --force
- [ ] `RemoveSymbol` — removes user-defined symbol
- [ ] `RemoveSymbol` — returns error when trying to remove hardware symbol
- [ ] `LookupSymbol` — returns symbol details for known address
- [ ] `LookupSymbol` — returns "No symbol defined" for unknown address
- [ ] `ListSymbols` — lists user-defined symbols
- [ ] `ListSymbols` — includes hardware symbols when requested
- [ ] `ListSymbols` — filters by substring
- [ ] `SetSymbols` — enables/disables symbol groups

## Tools/ZeroPageTool.cs

- [ ] `AnnotateZeroPage` — adds annotation to zero page map
- [ ] `AnnotateZeroPage` — returns error when no ROM is loaded
- [ ] `ShowZeroPageMap` — lists annotations
- [ ] `ShowZeroPageMap` — shows hex dump when showUnannotated is true

## Tools/SegmentTools.cs

- [ ] `DefineSegment` — creates segment with correct properties
- [ ] `DefineSegment` — validates segment type string
- [ ] `DefineSegment` — validates start <= end
- [ ] `DefineSegment` — detects overlapping segments
- [ ] `RemoveSegment` — removes segment by name
- [ ] `ListSegments` — lists all segments in text format
- [ ] `ListSegments` — lists segments in CSV format
- [ ] `ListSegments` — lists segments in TSV format
- [ ] `ListSegments` — lists segments in KV format
- [ ] `ClearSegments` — removes all segments
- [ ] `GenerateLinkerConfig` — produces valid cc65 linker config

## Tools/XRefTool.cs

- [ ] `XRef` — returns error when no ROM is loaded
- [ ] `XRef` — finds cross-references to a target address
- [ ] `XRef` — returns "No cross-references" for unreferenced address
- [ ] `XRef` — filters by access type (read/write/execute)
- [ ] `ClassifyAccess` — STA/STX/STY → Write
- [ ] `ClassifyAccess` — INC/DEC/ASL/LSR/ROL/ROR → ReadWrite
- [ ] `ClassifyAccess` — JSR/JMP → Execute
- [ ] `ClassifyAccess` — all others → Read
- [ ] `XRef` — formats text output correctly
- [ ] `XRef` — formats CSV output correctly

## Tools/ControlFlowTool.cs

- [ ] `TraceControlFlow` — returns error when no ROM is loaded
- [ ] `TraceControlFlow` — returns error for address not in loaded ROM
- [ ] `TraceControlFlow` — traces execution flow from start address
- [ ] `TraceControlFlow` — respects maxDepth parameter
- [ ] `TraceControlFlow` — respects maxInstructions budget
- [ ] `TraceControlFlow` — detects loops in execution path
- [ ] `TraceControlFlow` — delegates to StackAnalyzer when trackStack=true
- [ ] `TraceControlFlow` — formats text output correctly
- [ ] `TraceControlFlow` — formats CSV/TSV/KV output correctly

## Tools/DataFlowTool.cs

- [ ] `TraceAccess` — returns error when no ROM is loaded
- [ ] `TraceAccess` — traces forward data flow
- [ ] `TraceAccess` — traces backward data flow
- [ ] `TraceAccess` — formats text output correctly
- [ ] `TraceAccess` — formats CSV/TSV/KV output correctly

## Tools/AnalysisTools.cs

- [ ] `AnalyzeAndDisassemble` — returns error when no ROM is loaded
- [ ] `AnalyzeAndDisassemble` — runs analysis then disassembly
- [ ] `ProbeAndSegment` — returns error when no ROM is loaded
- [ ] `ProbeAndSegment` — probes data and auto-creates segment
- [ ] `AnalyzeFull` — returns error when no ROM is loaded
- [ ] `AnalyzeFull` — runs full analysis and creates segments
- [ ] `AnalyzeDisassembly` — runs analysis and returns summary
- [ ] `ProbeData` — probes a memory range
- [ ] `GenerateCallGraph` — generates call graph
- [ ] `AnalyzeCoverage` — analyzes code coverage
- [ ] `DiffRoms` — compares two byte arrays

## Tools/AtrTools.cs

- [ ] `AtrInfo` — returns error for non-ATR file
- [ ] `AtrInfo` — displays ATR geometry and directory
- [ ] `AtrHeader` — parses and displays ATR header fields
- [ ] `ListAtrDirectory` — lists directory entries
- [ ] `LoadAtrFile` — extracts file from ATR into session
- [ ] `LoadAtrBoot` — loads boot sectors into session

## Tools/AtrWriteTools.cs

- [ ] `ExtractAtrFile` — extracts file from ATR to disk
- [ ] `ExtractAtrFile` — returns error for non-existent file
- [ ] `InjectAtrFile` — injects data into ATR (copy-on-write)
- [ ] `InjectAtrFile` — returns error when input exceeds capacity
- [ ] `CreateAtr` — creates blank ATR with correct header
- [ ] `CreateAtr` — validates density parameter
- [ ] `WriteAtrSector` — writes raw data to a sector
- [ ] `WriteAtrFile` — creates new DOS file entry
- [ ] `DefineFilesystem` — defines custom filesystem layout

## Tools/AtrForensicTools.cs

- [ ] `SectorMap` — returns error for non-ATR file
- [ ] `SectorMap` — builds sector map for DOS-formatted ATR
- [ ] `SectorMap` — formats text output correctly
- [ ] `SectorMap` — formats ASCII output correctly
- [ ] `AnalyzeFragmentation` — detects file fragmentation
- [ ] `RecoverDeletedFile` — attempts file recovery
- [ ] `ScanBootSectors` — scans boot sectors across multiple ATRs

## Tools/PatternTools.cs

- [ ] `ListPatterns` — lists all patterns
- [ ] `ListPatterns` — filters by tag/category/query
- [ ] `AddPattern` — adds new pattern to library
- [ ] `AddPattern` — validates hex pattern
- [ ] `AddPattern` — updates existing pattern with --force
- [ ] `RemovePattern` — removes pattern by name
- [ ] `ShowPattern` — displays pattern details
- [ ] `SearchPattern` — searches binary using saved pattern
- [ ] `ImportPatterns` — imports patterns from JSON file
- [ ] `ExportPatterns` — exports patterns to JSON file

## Tools/PatternDetectionTool.cs

- [ ] `DetectPatterns` — returns error when no ROM is loaded
- [ ] `DetectPatterns` — detects all pattern types
- [ ] `DetectPatterns` — filters by type
- [ ] `DetectPatterns` — formats CSV output correctly

## Tools/StructureTools.cs

- [ ] `ListTemplates` — lists all templates
- [ ] `ListTemplates` — filters by tag/category/query
- [ ] `DefineTemplate` — adds template from JSON file
- [ ] `DefineTemplate` — adds template from inline JSON
- [ ] `DefineTemplate` — validates template structure
- [ ] `RemoveTemplate` — removes template by name
- [ ] `ShowTemplate` — displays template details
- [ ] `MatchTemplates` — scans range for template matches

## Tools/ScriptRunner.cs

- [ ] `RunScript` — returns error for non-existent script file
- [ ] `RunScript` — executes commands from script file
- [ ] `RunScript` — skips comments and blank lines
- [ ] `RunScript` — stops on first error
- [ ] `RunScript` — handles output redirection (removes > ...)
- [ ] `ParseCommandLine` — parses key=value arguments
- [ ] `ParseCommandLine` — handles quoted values
- [ ] `DispatchCommand` — dispatches to correct tool method

## Config.cs

- [ ] `Load` — returns null when no config file exists
- [ ] `Load` — loads config from specified path
- [ ] `Load` — searches upward from current directory
- [ ] `Load` — returns null on parse failure (logs warning)
- [ ] `Save` — writes config to specified path
- [ ] `Save` — writes to default path when not specified
- [ ] `ResolveTarget` — CLI target takes priority over config
- [ ] `ResolveTarget` — returns config target when no CLI target
- [ ] `ResolveTarget` — returns null when neither is set
