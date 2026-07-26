# Design Document: Deterministic Ordering & Verbose Mode

**Issue:** CLI Optimization — Output Consistency  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:9)  
**Status:** Draft

---

## 1. Problem Statement

### Deterministic Ordering
List commands (`symbol list`, `segment list`, `zero-page show`) do not guarantee a consistent sort order. The underlying `Dictionary<ushort, SymbolEntry>` and `List<SegmentDefinition>` may return items in insertion order or hash order, which changes across sessions. An LLM relying on positional output (e.g., "the third symbol") will get inconsistent results.

### Verbose Mode
There is no global mechanism to surface execution metadata (execution time, bytes processed, confidence scores, analysis pass counts). LLMs cannot currently gauge the cost or reliability of a command's output, making it harder to debug analysis workflows.

## 2. Proposed Design

### 2.1 Deterministic Ordering

Enforce a **primary sort by address (ascending)**, then **secondary sort by name (alphabetical)** for all list-type commands.

**Affected commands and sort keys:**

| Command              | Primary Sort | Secondary Sort |
|----------------------|--------------|----------------|
| `symbol list`        | Address (asc) | Label (alpha)  |
| `segment list`       | Start address | Name (alpha)   |
| `zero-page show`     | Address (asc) | Label (alpha)  |
| `xref <address>`     | Address (asc) | Mnemonic       |
| `coverage <s> <e>`   | Address (asc) | —              |
| `find-pattern`       | File offset   | —              |
| `find-strings`       | File offset   | —              |

**Implementation approach:**

1. In each tool method that produces a list, replace the raw enumeration with a `.OrderBy(addr).ThenBy(name)` pipeline before formatting.
2. For `SymbolTable`, add a helper method `GetOrderedSymbols()` that returns symbols sorted by address then label.
3. For `SegmentManager`, add a helper method `GetOrderedSegments()` that returns segments sorted by start address then name.

### 2.2 Verbose Mode

Add a global `--verbose` / `-v` flag that, when set, prepends or appends execution metadata to command output.

**Metadata fields to include:**

| Field              | Description                              | Source                                |
|--------------------|------------------------------------------|---------------------------------------|
| `execution_ms`     | Wall-clock time for the command          | `Stopwatch` in command handler        |
| `bytes_processed`  | Number of bytes scanned/analyzed         | Tool-specific counter                 |
| `session_target`   | Currently loaded file path               | `RomSession.LoadedPath`               |
| `session_size`     | Size of loaded binary                    | `RomSession.Data.Length`              |
| `symbol_count`     | Number of active symbols                 | `SymbolTable.Count`                   |
| `segment_count`    | Number of defined segments               | `SegmentManager.Segments.Count`       |
| `confidence`       | Confidence score (probe/coverage only)   | `ProbeResult.Confidence`              |
| `passes_completed` | Analysis passes run (analyze only)       | `DisassemblyAnalyzer.PassesCompleted` |

**Output format in verbose mode:**

Metadata is emitted as `# ` prefixed lines (shell-compatible comments) before the main output:

```
# execution_ms=142
# bytes_processed=16384
# session_target=game.xex
# session_size=32768
# symbol_count=215
# segment_count=4
<main command output follows...>
```

This format is:
- Shell-compatible (lines starting with `#` are ignored by bash)
- Easy for LLMs to parse (key=value)
- Non-intrusive to existing text parsers

## 3. Implementation Plan

### Phase 1: Sort Enforcement

1. Add `GetOrderedSymbols()` to [`State/SymbolTable.cs`](../State/SymbolTable.cs):
   ```csharp
   public IEnumerable<KeyValuePair<ushort, SymbolEntry>> GetOrderedSymbols()
   {
       return this.OrderBy(kvp => kvp.Key)
                  .ThenBy(kvp => kvp.Value.Label);
   }
   ```

2. Add `GetOrderedSegments()` to [`State/SegmentManager.cs`](../State/SegmentManager.cs):
   ```csharp
   public IEnumerable<SegmentDefinition> GetOrderedSegments()
   {
       return _segments.OrderBy(s => s.Start)
                       .ThenBy(s => s.Name);
   }
   ```

3. Update all list-formatting methods in [`Tools/SymbolTools.cs`](../Tools/SymbolTools.cs), [`Tools/SegmentTools.cs`](../Tools/SegmentTools.cs), [`Tools/ZeroPageTool.cs`](../Tools/ZeroPageTool.cs) to use ordered accessors.

### Phase 2: Verbose Infrastructure

1. Create [`Helpers/VerboseContext.cs`](../Helpers/VerboseContext.cs):
   ```csharp
   public class VerboseContext
   {
       public bool Enabled { get; set; }
       public Stopwatch Timer { get; } = new();
       public long BytesProcessed { get; set; }
       public int PassesCompleted { get; set; }
       public string? Confidence { get; set; }

       public string FormatMetadata(RomSession session, SymbolTable symbols, SegmentManager segments);
   }
   ```

2. Register `--verbose` as a global option in [`Program.cs`](../Program.cs):
   ```csharp
   var verboseOption = new Option<bool>("--verbose", "Show execution metadata");
   rootCommand.AddGlobalOption(verboseOption);
   ```

3. Create a `VerboseHandler` middleware that wraps command execution:
   ```csharp
   // In Program.cs or a new Helpers/CommandMiddleware.cs
   command.SetHandler(async (context) =>
   {
       var verbose = context.ParseResult.GetValueForOption(verboseOption);
       if (verbose) Console.Error.WriteLine("# verbose mode enabled");
       // ... execute original handler ...
   });
   ```

### Phase 3: Tool Integration

For each tool, add `VerboseContext` parameter and populate `BytesProcessed`:

- `DisassemblerTool.Disassemble` — set `BytesProcessed = bytes`
- `AnalysisTools.AnalyzeDisassembly` — set `BytesProcessed`, `PassesCompleted`
- `DataProber.ProbeData` — set `BytesProcessed`, `Confidence`
- `CodeCoverage.AnalyzeCoverage` — set `BytesProcessed`
- `FindPatternTool.FindPattern` — set `BytesProcessed`
- `StringSearchTool.FindStrings` — set `BytesProcessed`

## 4. API/Syntax Changes

```bash
# Verbose mode
atarihacker --verbose -- symbol list
atarihacker -v -- analyze

# Output example:
# execution_ms=142
# bytes_processed=16384
# session_target=game.xex
$B000: game_init
$B040: sub_B040
...
```

## 5. Data Structures

### New: `VerboseContext`

```csharp
public class VerboseContext
{
    public bool Enabled { get; set; }
    public Stopwatch Timer { get; init; } = new();
    public long BytesProcessed { get; set; }
    public int PassesCompleted { get; set; }
    public string? Confidence { get; set; }

    public string GetMetadata(RomSession session, SymbolTable symbols, SegmentManager segments);
}
```

## 6. Affected Files

| File                              | Change                                                |
|-----------------------------------|-------------------------------------------------------|
| `Helpers/VerboseContext.cs`       | **New** — verbose metadata collector                  |
| `State/SymbolTable.cs`            | Add `GetOrderedSymbols()` method                      |
| `State/SegmentManager.cs`         | Add `GetOrderedSegments()` method                     |
| `Program.cs`                      | Register `--verbose` option, wire VerboseContext       |
| `Tools/SymbolTools.cs`            | Use ordered symbols, accept VerboseContext             |
| `Tools/SegmentTools.cs`           | Use ordered segments, accept VerboseContext            |
| `Tools/ZeroPageTool.cs`           | Use ordered zero-page map                             |
| `Tools/DisassemblerTool.cs`       | Accept VerboseContext, set BytesProcessed              |
| `Tools/AnalysisTools.cs`          | Accept VerboseContext, set BytesProcessed/Passes       |
| `Analysis/DataProber.cs`          | Accept VerboseContext, set Confidence                  |
| `Analysis/CodeCoverage.cs`        | Accept VerboseContext, set BytesProcessed              |
| `Tools/FindPatternTool.cs`        | Accept VerboseContext                                  |
| `Tools/StringSearchTool.cs`       | Accept VerboseContext                                  |

## 7. Testing Considerations

- Sort order: verify that `symbol list` with 3 symbols at addresses `$B000`, `$A000`, `$C000` returns `$A000`, `$B000`, `$C000`
- Sort stability: verify that symbols at the same address are sorted by label alphabetically
- Verbose metadata: verify `# ` prefixed lines appear only when `--verbose` is set
- Verbose + structured formats: metadata should appear before the structured output
- Empty lists: `symbol list` with no symbols should still show metadata in verbose mode
