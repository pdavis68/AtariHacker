# Design Document: Structured Output Formats

**Issue:** CLI Optimization — Output Consistency and Parseability  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:7)  
**Status:** Draft

---

## 1. Problem Statement

Currently, commands like `analyze`, `callgraph`, `probe`, `coverage`, and `symbol list` output free-form text designed for human readability. When an LLM (or script) consumes these outputs, it must parse unstructured text, which is error-prone and fragile. Adding machine-readable but text-based output formats (CSV, TSV, key-value pairs) will make parsing deterministic without requiring a full JSON dependency.

## 2. Proposed Design

Add a `--format` option to applicable commands that accepts one of:

| Value     | Description                                      |
|-----------|--------------------------------------------------|
| `text`    | Default human-readable format (current behavior) |
| `csv`     | Comma-separated values with header row           |
| `tsv`     | Tab-separated values with header row             |
| `kv`      | Key=value pairs, one per line                    |

### 2.1 Format Selection Strategy

Rather than threading `--format` through every tool method, introduce a **format dispatch layer** in each tool class. Each tool method that currently writes to `Console` will instead return a structured result object, and a formatter helper will render it in the requested format.

### 2.2 Structured Result Types

New record types in a `Results/` namespace (or inline in each tool file):

```csharp
// Analysis/ProbeResult.cs — already exists, extend with ToCsv/ToTsv/ToKv
// New: Results/CoverageResult.cs — already exists as record, add formatting
// New: Results/CallGraphResult.cs
// New: Results/SymbolListResult.cs
```

Each result type will implement an interface:

```csharp
public interface IStructuredOutput
{
    string ToText();
    string ToCsv();
    string ToTsv();
    string ToKv();
}
```

### 2.3 Affected Commands

| Command       | Current Output Type     | Structured Columns (CSV/TSV)                          |
|---------------|-------------------------|-------------------------------------------------------|
| `analyze`     | Summary paragraph       | `address,type,label,confidence,details`               |
| `callgraph`   | Mermaid or text tree    | `caller,callee,depth,address`                         |
| `probe`       | Text description        | `start,end,detected_type,confidence,evidence`         |
| `coverage`    | Text summary + regions  | `start,end,classification,bytes,percentage`           |
| `symbol list` | Formatted table         | `address,label,group,comment,is_user_defined`         |
| `segment list`| Formatted table         | `name,type,start,end,size`                            |
| `xref`        | Grouped text            | `address,mnemonic,operand,file_offset`                |
| `trace`       | Indented text tree      | `depth,address,mnemonic,operand,type`                 |

## 3. Implementation Plan

### Phase 1: Formatter Infrastructure

1. Create [`Helpers/OutputFormatter.cs`](../Helpers/OutputFormatter.cs) — a static utility class with methods:
   - `FormatCsv<T>(IEnumerable<T> rows, string[] headers, Func<T, string[]> extractor)`
   - `FormatTsv<T>(...)` — same as CSV but tab-delimited
   - `FormatKv<T>(IEnumerable<T> rows, string[] keys, Func<T, string[]> extractor)`
   - Handle quoting/escaping for values containing commas, tabs, newlines

2. Create [`Helpers/FormatOption.cs`](../Helpers/FormatOption.cs) — a shared `--format` option definition:
   ```csharp
   public static readonly Option<string> FormatOption = new(
       "--format",
       () => "text",
       "Output format: text, csv, tsv, or kv");
   ```

### Phase 2: Tool-by-Tool Adoption

For each tool, in order of impact:

1. **`symbol list`** — simplest, already tabular
2. **`segment list`** — simplest, already tabular
3. **`coverage`** — already has `CoverageResult` record
4. **`probe`** — already has `ProbeResult` record
5. **`analyze`** — needs new result type
6. **`callgraph`** — needs new result type
7. **`xref`** — needs new result type
8. **`trace`** — needs new result type

### Phase 3: Integration in Program.cs

Register `--format` as a global option or per-command option. Pass the value through to the tool method.

```csharp
var formatOption = new Option<string>("--format", () => "text",
    "Output format: text, csv, tsv, kv");
rootCommand.AddGlobalOption(formatOption);
```

## 4. API/Syntax Changes

```bash
# Current
atarihacker -- symbol list

# New — CSV output
atarihacker -- symbol list --format csv

# New — TSV output piped to file
atarihacker -- coverage $0C00 $1CFF --format tsv > coverage.tsv

# New — KV output for LLM consumption
atarihacker -- analyze --format kv
```

## 5. Data Structures

### New: `OutputFormatter` static class

```csharp
public static class OutputFormatter
{
    public static string ToCsv(string[] headers, string[][] rows);
    public static string ToTsv(string[] headers, string[][] rows);
    public static string ToKv(string[][] rows); // key=value per cell
    private static string EscapeCsv(string value);
    private static string EscapeTsv(string value);
}
```

### Extended: `CoverageResult`

Add methods `ToCsv()`, `ToTsv()`, `ToKv()`.

### New: `AnalysisSummaryResult`

```csharp
public record AnalysisSummaryResult(
    int TotalBytes,
    int CodeBytes,
    int DataBytes,
    int OrphanedBytes,
    int ProcedureCount,
    int ReferenceCount
) : IStructuredOutput;
```

## 6. Affected Files

| File                          | Change                                                       |
|-------------------------------|--------------------------------------------------------------|
| `Helpers/OutputFormatter.cs`  | **New** — CSV/TSV/KV formatting utilities                    |
| `Helpers/FormatOption.cs`     | **New** — shared `--format` option definition                |
| `Program.cs`                  | Register `--format` option on applicable commands            |
| `Tools/SymbolTools.cs`        | Add format switch to `ListSymbols`                           |
| `Tools/SegmentTools.cs`       | Add format switch to `ListSegments`                          |
| `Tools/AnalysisTools.cs`      | Add format switch to `AnalyzeDisassembly`, `ProbeData`, etc. |
| `Analysis/CodeCoverage.cs`    | Add structured output methods to `CoverageResult`            |
| `Analysis/DataProber.cs`      | Add structured output methods to `ProbeResult`               |
| `Analysis/CallGraph.cs`       | Add structured output methods                                |
| `Tools/XRefTool.cs`           | Add structured output methods                                |
| `Tools/ControlFlowTool.cs`    | Add structured output methods                                |

## 7. Testing Considerations

- CSV escaping: values containing `,`, `"`, newlines must be properly quoted
- TSV escaping: values containing tabs must be escaped
- KV output: multi-line values should be base64-encoded or truncated
- Empty results: all formats should produce valid output (headers only for CSV/TSV)
- Backward compatibility: `--format text` must produce identical output to current behavior
