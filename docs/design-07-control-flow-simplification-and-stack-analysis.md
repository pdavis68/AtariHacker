# Design Document: Control Flow Simplification & Stack Analysis

**Issue:** Expanded Hacking Functionality — Improved Static Analysis  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:37)  
**Status:** Draft

---

## 1. Problem Statement

### 1.1 Control Flow Simplification
The current `trace` and `callgraph` tools show raw execution paths but do not identify higher-level control flow patterns that are common in 6502 code:

- **State machines**: A dispatch loop that reads a state variable and jumps to a handler via a computed address
- **Jump tables**: An indexed jump (`JMP (table,X)`) that dispatches to multiple handlers
- **Coroutine patterns**: Cooperative multitasking where routines yield control via `JMP` instead of `JSR`/`RTS`
- **Interrupt handlers**: Code entered via hardware interrupts with `RTI` termination

These patterns are difficult to analyze statically because they use indirect addressing or non-standard calling conventions.

### 1.2 Stack Analysis
The `trace` command does not track stack depth. Understanding stack usage is critical for:
- Determining subroutine calling conventions (how many parameters are passed on the stack?)
- Identifying stack-allocated local variables
- Detecting stack imbalances (potential bugs or obfuscation)
- Understanding interrupt handler stack requirements

## 2. Proposed Design

### 2.1 Control Flow Pattern Detection

Introduce a `detect-patterns` command that scans analyzed code for known control flow patterns:

#### State Machine Detection

Detects the pattern:
```
LDA state_var    ; Load state
ASL A            ; or other scaling
TAX
JMP table,X      ; or JMP (table,X)
```

**Output:**
```
State machine detected at $C000:
  State variable: $00E0 (GAMESTAT)
  Jump table: $D000
  Entries: 8 (estimated)
  Handlers: $D100, $D200, $D300, $D400, ...
```

#### Jump Table Detection

Detects indexed jump patterns and attempts to enumerate all targets:

```
Pattern: JMP (table,X) or JMP table,X
```

**Output:**
```
Jump table detected at $D000:
  Type: Absolute indirect (JMP ($D000,X))
  Entries: 12
  Targets: $C100, $C200, $C300, $C310, $C320, ...
```

#### Coroutine Detection

Detects patterns where routines chain via `JMP` instead of `JSR`/`RTS`:

```
Coroutine chain detected:
  $C000: JMP $C100
  $C100: JMP $C200
  $C200: JMP $C000
  (circular dependency detected)
```

#### Interrupt Handler Detection

Detects code reachable only from hardware vectors ($FFFA-$FFFF):

```
Interrupt handler detected at $E000:
  Type: NMI (from $FFFA)
  Terminates with: RTI
  Saves: A, X, Y, status
```

### 2.2 Stack Analysis

Add stack depth tracking to the `trace` command and a new `stack-analyze` command:

#### Stack Depth Tracking

During execution tracing, maintain a virtual stack pointer:

| Operation | Stack Effect |
|-----------|-------------|
| `JSR addr` | Push return address (+2 bytes) |
| `RTS` | Pop return address (-2 bytes) |
| `PHA` | Push A (+1 byte) |
| `PLA` | Pop A (-1 byte) |
| `PHP` | Push status (+1 byte) |
| `PLP` | Pop status (-1 byte) |
| `RTI` | Pop status + return address (-4 bytes) |
| `BRK` | Push return + status (+4 bytes) |

#### Stack Analysis Output

```bash
atarihacker -- stack-analyze <address>
```

```
Stack analysis for $C000 (update_score):
  Entry stack depth: 2 (return address on stack)
  Maximum depth: 6
  Minimum depth: 2
  Exit stack depth: 2 (balanced)
  
  Stack operations:
    $C010: PHA          ; depth 2→3 (save A)
    $C020: JSR $D000    ; depth 3→5 (call helper)
    $C030: PLA          ; depth 5→4 (restore A)
    $C040: RTS          ; depth 4→2 (return)
  
  Warnings:
    - Stack depth at exit (2) matches entry (2): ✓ balanced
    - No unbalanced branches detected
```

#### Stack Imbalance Detection

Flag cases where:
- Stack depth at exit differs from entry (leaked pushes/pops)
- Conditional branches lead to different stack depths
- `RTS` is reached with incorrect stack depth

### 2.3 CLI Commands

| Command              | Description                                      |
|----------------------|--------------------------------------------------|
| `detect-patterns`    | Scan for state machines, jump tables, coroutines  |
| `stack-analyze`      | Analyze stack usage at a given address            |
| `trace --stack`      | Enhanced trace with stack depth annotations       |

## 3. Implementation Plan

### Phase 1: Pattern Detection Engine

1. Create [`Analysis/PatternDetector.cs`](../Analysis/PatternDetector.cs):
   - `DetectStateMachines(byte[] data, ReferenceGraph graph)`
   - `DetectJumpTables(byte[] data, ReferenceGraph graph)`
   - `DetectCoroutines(ReferenceGraph graph)`
   - `DetectInterruptHandlers(RomSession session)`

2. Create [`Tools/PatternDetectionTool.cs`](../Tools/PatternDetectionTool.cs):
   - `DetectPatterns` method — CLI entry point

### Phase 2: Stack Analysis

1. Create [`Analysis/StackAnalyzer.cs`](../Analysis/StackAnalyzer.cs):
   - `AnalyzeStack(byte[] data, ushort startAddress, int maxInstructions)`
   - Track virtual stack pointer through instruction stream
   - Handle conditional branches (fork stack tracking)
   - Detect imbalances

2. Update [`Tools/ControlFlowTool.cs`](../Tools/ControlFlowTool.cs):
   - Add `--stack` flag to `trace` command
   - Annotate each trace line with current stack depth

### Phase 3: Integration

1. Register new commands in [`Program.cs`](../Program.cs)
2. Add `--stack` option to existing `trace` command

## 4. API/Syntax Changes

```bash
# Control flow pattern detection
atarihacker -- detect-patterns
atarihacker -- detect-patterns --type state-machine
atarihacker -- detect-patterns --type jump-table
atarihacker -- detect-patterns --format csv

# Stack analysis
atarihacker -- stack-analyze $C000
atarihacker -- stack-analyze $C000 --max-instructions 2000

# Trace with stack depth
atarihacker -- trace $C000 --stack
atarihacker -- trace $C000 --stack --format kv
```

## 5. Data Structures

### New: `ControlFlowPattern`

```csharp
public abstract record ControlFlowPattern
{
    public string Type { get; init; }
    public ushort Address { get; init; }
    public double Confidence { get; init; }
}

public record StateMachinePattern : ControlFlowPattern
{
    public ushort StateVariable { get; init; }
    public ushort JumpTable { get; init; }
    public int EntryCount { get; init; }
    public List<ushort> Handlers { get; init; }
}

public record JumpTablePattern : ControlFlowPattern
{
    public ushort TableAddress { get; init; }
    public string JumpType { get; init; }  // "absolute_indexed" or "absolute_indirect_indexed"
    public int EntryCount { get; init; }
    public List<ushort> Targets { get; init; }
}

public record CoroutinePattern : ControlFlowPattern
{
    public List<ushort> Chain { get; init; }
    public bool IsCircular { get; init; }
}

public record InterruptPattern : ControlFlowPattern
{
    public string VectorName { get; init; }  // NMI, RESET, IRQ
    public ushort VectorAddress { get; init; }
}
```

### New: `StackFrame`

```csharp
public record StackFrame(
    ushort Address,
    string Mnemonic,
    int DepthBefore,
    int DepthAfter,
    string? Warning
);
```

### New: `StackAnalysisResult`

```csharp
public record StackAnalysisResult(
    ushort EntryPoint,
    int EntryDepth,
    int MaxDepth,
    int MinDepth,
    int ExitDepth,
    bool IsBalanced,
    List<StackFrame> Operations,
    List<string> Warnings
);
```

## 6. Affected Files

| File                              | Change                                                |
|-----------------------------------|-------------------------------------------------------|
| `Analysis/PatternDetector.cs`     | **New** — control flow pattern detection engine        |
| `Tools/PatternDetectionTool.cs`   | **New** — `detect-patterns` CLI implementation         |
| `Analysis/StackAnalyzer.cs`       | **New** — stack depth analysis engine                  |
| `Tools/ControlFlowTool.cs`        | Add `--stack` flag, stack depth annotations            |
| `Program.cs`                      | Register `detect-patterns`, `stack-analyze` commands   |

## 7. Testing Considerations

- State machine detection: verify with known dispatch loop pattern
- Jump table enumeration: verify all targets are correctly extracted
- Coroutine detection: verify circular JMP chains are detected
- Stack balance: verify balanced subroutines show no warnings
- Stack imbalance: verify intentional imbalance (e.g., RTS trick) is flagged
- Conditional branches: verify stack tracking forks correctly
- Interrupt handlers: verify detection from vector table entries
- Performance: pattern detection on 64KB ROM should complete in < 2 seconds
