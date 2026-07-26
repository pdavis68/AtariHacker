# Design Document: Command Design Improvements

**Issue:** CLI Optimization — Command Design  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:15)  
**Status:** Draft

---

## 1. Problem Statement

Three distinct improvements are needed for command design:

### 1.1 Atomic Operations (Idempotency)
Commands like `symbol define` are not idempotent — running them twice with the same parameters may produce different state (e.g., overwriting a user label with different metadata). For LLM retry logic, commands should be safe to re-run.

### 1.2 Compound Commands
Common multi-step workflows (e.g., analyze then disassemble) require multiple CLI roundtrips. A compound command reduces latency and simplifies LLM orchestration.

### 1.3 Validation / Dry-Run Mode
Destructive operations (`atr inject`, `atr write-sector`, `segment clear`, `symbol remove`) have no preview mechanism. An LLM cannot verify the effect of a command before executing it.

---

## 2. Proposed Design

### 2.1 Atomic Operations (Idempotency)

**Principle:** Running any command twice with identical parameters should produce identical state.

**Specific changes:**

| Command              | Current Behavior                                      | Idempotent Behavior                                    |
|----------------------|-------------------------------------------------------|--------------------------------------------------------|
| `symbol define`      | Overwrites existing symbol silently                   | Same result — already idempotent if same label/comment |
| `symbol remove`      | Errors if symbol doesn't exist                        | Succeeds silently (no-op) if already removed            |
| `segment define`     | Overwrites existing segment silently                  | Same result — already idempotent if same definition     |
| `segment remove`     | Errors if segment doesn't exist                       | Succeeds silently (no-op) if already removed            |
| `segment clear`      | Clears all segments                                   | Already idempotent                                      |
| `zero-page annotate` | Overwrites existing annotation silently               | Same result — already idempotent                        |
| `labels load`        | Merges labels from file                               | Already idempotent (merge semantics)                    |
| `labels save`        | Writes sidecar file                                   | Already idempotent                                      |

**Key changes needed:**

1. **`symbol remove`** — change from throwing if symbol not found to returning a warning but succeeding:
   ```csharp
   // Before
   if (!_symbolTable.Remove(address))
       throw new InvalidOperationException($"No symbol at ${address:X4}");
   
   // After
   if (!_symbolTable.Remove(address))
       Console.WriteLine($"# Warning: no symbol at ${address:X4}, nothing to remove");
   ```

2. **`segment remove`** — same pattern as symbol remove.

3. **`symbol define`** — add `--force` flag to explicitly allow overwriting hardware symbols; by default, only overwrite user symbols of the same name.

### 2.2 Compound Commands

Introduce shorthand commands that combine common operations:

#### `analyze-disassemble`

```bash
atarihacker -- analyze-disassemble <start> <bytes> [options]
```

This runs the multi-pass analyzer then immediately disassembles the analyzed range.

**Implementation:** New method in [`Tools/AnalysisTools.cs`](../Tools/AnalysisTools.cs):

```csharp
public static void AnalyzeAndDisassemble(
    CliSession session,
    ushort startAddress,
    int bytes,
    string format,
    bool verbose)
{
    // 1. Run analysis
    var analyzer = new DisassemblyAnalyzer(session);
    analyzer.Analyze();
    analyzer.TraceCodeRegions();
    analyzer.GenerateLabels();
    
    // 2. Run disassembly with analysis results
    DisassemblerTool.Disassemble(session, startAddress, bytes, 
        startAddress, format, analyze: true, verbose);
}
```

#### `probe-and-segment`

```bash
atarihacker -- probe-and-segment <start> <end>
```

Runs `probe` on a range, then automatically creates a segment definition based on the highest-confidence detection.

#### `analyze-full`

```bash
atarihacker -- analyze-full
```

Runs full analysis, generates labels, creates segments from detected code/data regions, and outputs a summary — all in one command.

### 2.3 Dry-Run Mode

Add a `--dry-run` flag to destructive commands that shows what would happen without making changes.

**Affected commands:**

| Command              | Dry-Run Behavior                                              |
|----------------------|---------------------------------------------------------------|
| `atr inject`         | Show file size, target sectors, capacity check result          |
| `atr write-sector`   | Show sector number, current bytes, new bytes (diff)            |
| `atr write-file`     | Show file name, size, sectors that would be allocated          |
| `atr create`         | Show output path, geometry, total size                         |
| `segment clear`      | Show list of segments that would be removed                    |
| `symbol remove`      | Show symbol that would be removed                              |
| `segment remove`     | Show segment that would be removed                             |

**Implementation pattern:**

```csharp
public static void InjectAtrFile(
    string path, string name, byte[] data, bool dryRun)
{
    if (dryRun)
    {
        Console.WriteLine($"# DRY RUN: Inject '{name}' into {path}");
        Console.WriteLine($"#   File size: {data.Length} bytes");
        Console.WriteLine($"#   Required sectors: {CalculateRequiredSectors(data.Length, sectorSize)}");
        Console.WriteLine($"#   Available capacity: {availableCapacity} bytes");
        Console.WriteLine("#   Run without --dry-run to apply changes");
        return;
    }
    // ... actual injection logic ...
}
```

## 3. Implementation Plan

### Phase 1: Idempotency Fixes

1. Update [`Tools/SymbolTools.cs`](../Tools/SymbolTools.cs) — `RemoveSymbol`: change error to warning
2. Update [`Tools/SegmentTools.cs`](../Tools/SegmentTools.cs) — `RemoveSegment`: change error to warning
3. Add `--force` flag to `symbol define` for hardware symbol overwrite

### Phase 2: Compound Commands

1. Add `analyze-disassemble` command in [`Program.cs`](../Program.cs)
2. Add `AnalyzeAndDisassemble` method in [`Tools/AnalysisTools.cs`](../Tools/AnalysisTools.cs)
3. Add `probe-and-segment` command
4. Add `analyze-full` command

### Phase 3: Dry-Run

1. Add `--dry-run` option to destructive commands in [`Program.cs`](../Program.cs)
2. Implement dry-run logic in:
   - [`Tools/AtrWriteTools.cs`](../Tools/AtrWriteTools.cs) — `InjectAtrFile`, `WriteAtrSector`, `CreateAtr`
   - [`Tools/SegmentTools.cs`](../Tools/SegmentTools.cs) — `ClearSegments`, `RemoveSegment`
   - [`Tools/SymbolTools.cs`](../Tools/SymbolTools.cs) — `RemoveSymbol`

## 4. API/Syntax Changes

```bash
# Idempotent remove (no error if already removed)
atarihacker -- symbol remove $B000

# Compound commands
atarihacker -- analyze-disassemble $0C00 5376 --format ca65
atarihacker -- probe-and-segment $1D00 $2FFF
atarihacker -- analyze-full

# Dry-run destructive operations
atarihacker -- atr inject game.atr AGENT.OBJ build/AGENT.OBJ --dry-run
atarihacker -- segment clear --dry-run
```

## 5. Data Structures

No new data structures required. Changes are behavioral/logical only.

## 6. Affected Files

| File                          | Change                                                |
|-------------------------------|-------------------------------------------------------|
| `Program.cs`                  | Add compound commands, `--dry-run` options             |
| `Tools/SymbolTools.cs`        | Idempotent remove, `--force` flag for define           |
| `Tools/SegmentTools.cs`       | Idempotent remove, dry-run for clear/remove            |
| `Tools/AnalysisTools.cs`      | Add `AnalyzeAndDisassemble` method                     |
| `Tools/AtrWriteTools.cs`      | Add dry-run parameter to inject, write-sector, create  |
| `Tools/DisassemblerTool.cs`   | No changes (reused by compound command)                |

## 7. Testing Considerations

- Idempotency: run `symbol remove $B000` twice — second call should succeed with warning
- Compound commands: verify `analyze-disassemble` produces same output as separate `analyze` + `disassemble`
- Dry-run: verify no side effects (file modifications, symbol changes) when `--dry-run` is set
- Dry-run output: should clearly indicate it's a preview and instruct user to run without flag
