# Design Document: Enhanced Cross-Reference & Data Flow

**Issue:** Expanded Hacking Functionality — Cross-Reference and Data Flow  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:31)  
**Status:** Draft

---

## 1. Problem Statement

### 1.1 Enhanced Xref
The current `xref` command finds all instructions that reference a target address, but it only shows **code references** — instructions whose operand matches the target. It does not show:
- Which addresses **read** vs. **write** the target
- Data flow: which values are stored to/loaded from the target
- The context of the reference (inside which procedure/segment)

### 1.2 Memory Access Tracing
There is no way to statically trace how data flows through memory. For example, if address `$B000` is written by instruction A and read by instruction B, there is no tool to discover this read-write chain. Understanding data structures without runtime execution requires this capability.

## 2. Proposed Design

### 2.1 Enhanced Xref (`xref++`)

Extend the `xref` command with additional analysis dimensions:

**New output columns:**

| Column          | Description                                      |
|-----------------|--------------------------------------------------|
| `address`       | Address of the referencing instruction            |
| `mnemonic`      | Instruction mnemonic (LDA, STA, JSR, etc.)        |
| `operand`       | Full operand string                              |
| `access_type`   | `read`, `write`, `execute`, or `read-write`       |
| `procedure`     | Name of the containing procedure (if analyzed)    |
| `segment`       | Name of the containing memory segment             |
| `value_context` | Known or inferred value (for LDA/STA immediate)   |

**Access type classification:**

| Mnemonic Group       | Access Type | Examples                                    |
|----------------------|-------------|---------------------------------------------|
| Load instructions    | `read`      | LDA, LDX, LDY, BIT, CMP, ADC, SBC, AND, ORA, EOR |
| Store instructions   | `write`     | STA, STX, STY                                |
| Modify instructions  | `read-write`| INC, DEC, ASL, LSR, ROL, ROR                 |
| Jump/call            | `execute`   | JSR, JMP                                     |
| Push/pull            | `read`/`write` | PHA, PLA, PHP, PLP                        |

**New `--type` filter option:**

```bash
# Show only writes to the target
atarihacker -- xref $D000 --type write

# Show only reads from the target
atarihacker -- xref $D000 --type read

# Show only calls to the target
atarihacker -- xref $E410 --type execute
```

### 2.2 Memory Access Tracing (`trace-access`)

New command that statically traces data flow through memory:

```bash
atarihacker -- trace-access <address> [--direction forward|backward] [--depth <n>]
```

**Forward trace** (default): Starting from instructions that **write** to the target address, follow execution to find instructions that **read** from it.

**Backward trace**: Starting from instructions that **read** from the target address, trace backward to find instructions that **write** to it.

**Algorithm:**

1. **Find all references** to the target address using the existing `xref` mechanism
2. **Classify** each reference as read or write
3. **For forward trace:**
   a. Start from each write instruction
   b. Walk forward through the instruction stream (following branches, jumps, and fall-through)
   c. Collect all read instructions that access the target
   d. Stop at RTS/RTI/BRK or when depth limit is reached
4. **For backward trace:**
   a. Start from each read instruction
   b. Walk backward through the instruction stream
   c. Collect all write instructions that access the target
   d. Stop at subroutine entry or when depth limit is reached
5. **Build a data flow graph** showing the chain of read/write operations

**Output format:**

```
Data flow for $B000 (score_buffer):
  Written by:
    $C040: STA $B000       ; in procedure update_score
    $C0A0: STA $B000       ; in procedure init_game
  Read by:
    $C200: LDA $B000       ; in procedure draw_score
    $C2A0: CMP $B000       ; in procedure check_high_score

Data flow chain:
  $C040 (write) ──→ $C200 (read)  [fall-through, 3 instructions]
  $C0A0 (write) ──→ $C200 (read)  [via JSR $C1F0, 2 calls deep]
```

### 2.3 Integration with Procedure Detection

When the `DisassemblyAnalyzer` has run, `xref` and `trace-access` should use `ProcedureInfo` to:
- Group references by containing procedure
- Show call context (who calls the procedure containing the reference)
- Estimate data flow boundaries (procedure entry/exit points)

## 3. Implementation Plan

### Phase 1: Enhanced Xref

1. Update [`Tools/XRefTool.cs`](../Tools/XRefTool.cs):
   - Add `AccessType` classification logic
   - Add `--type` filter parameter
   - Add procedure/segment context lookup
   - Add `--format` support for structured output

2. Update `XRefResult` to include access type and context:
   ```csharp
   public record XRefEntry(
       ushort Address,
       string Mnemonic,
       string Operand,
       AccessType Access,
       string? Procedure,
       string? Segment
   );
   ```

### Phase 2: Memory Access Tracing

1. Create [`Analysis/DataFlowAnalyzer.cs`](../Analysis/DataFlowAnalyzer.cs):
   ```csharp
   public class DataFlowAnalyzer
   {
       public DataFlowResult TraceForward(ushort targetAddress, int maxDepth);
       public DataFlowResult TraceBackward(ushort targetAddress, int maxDepth);
   }
   ```

2. Create [`Tools/DataFlowTool.cs`](../Tools/DataFlowTool.cs):
   - `TraceAccess` method — CLI entry point
   - Format output as text tree or structured format

3. Register `trace-access` command in [`Program.cs`](../Program.cs)

### Phase 3: Procedure Integration

1. Update `XRefTool` to accept `ProcedureInfo` list when available
2. Update `DataFlowAnalyzer` to use procedure boundaries as trace limits

## 4. API/Syntax Changes

```bash
# Enhanced xref with access type
atarihacker -- xref $D000
atarihacker -- xref $D000 --type write
atarihacker -- xref $D000 --type read
atarihacker -- xref $D000 --format csv

# Memory access tracing
atarihacker -- trace-access $B000
atarihacker -- trace-access $B000 --direction backward
atarihacker -- trace-access $B000 --depth 10
atarihacker -- trace-access $B000 --format kv
```

## 5. Data Structures

### New: `AccessType` enum

```csharp
public enum AccessType { Read, Write, ReadWrite, Execute }
```

### New: `XRefEntry` record

```csharp
public record XRefEntry(
    ushort Address,
    string Mnemonic,
    string Operand,
    AccessType Access,
    string? Procedure,
    string? Segment
);
```

### New: `DataFlowLink`

```csharp
public record DataFlowLink(
    ushort FromAddress,
    AccessType FromAccess,
    ushort ToAddress,
    AccessType ToAccess,
    string Path,
    int InstructionCount
);
```

### New: `DataFlowResult`

```csharp
public record DataFlowResult(
    ushort TargetAddress,
    List<XRefEntry> Writes,
    List<XRefEntry> Reads,
    List<DataFlowLink> Chain
);
```

## 6. Affected Files

| File                              | Change                                                |
|-----------------------------------|-------------------------------------------------------|
| `Tools/XRefTool.cs`               | Add access type classification, `--type` filter        |
| `Analysis/DataFlowAnalyzer.cs`    | **New** — data flow tracing engine                     |
| `Tools/DataFlowTool.cs`           | **New** — `trace-access` CLI implementation            |
| `Program.cs`                      | Register `trace-access` command, update `xref` options |
| `Analysis/DisassemblyAnalyzer.cs` | Export `ProcedureInfo` for xref context                |

## 7. Testing Considerations

- Access type classification: verify all 6502 mnemonics are correctly classified
- `--type` filter: verify filtered results contain only the requested access type
- Forward trace: verify it correctly follows branches and stops at RTS
- Backward trace: verify it correctly walks backward through instruction stream
- Depth limit: verify trace stops at the specified depth
- No references: `trace-access` on an unreferenced address should return empty result
- Self-modifying code: trace should handle read-write to same address
- Performance: tracing should have a configurable instruction budget (default 1000)
