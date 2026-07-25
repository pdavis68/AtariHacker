# AtariHacker LLM Optimization Roadmap

## CLI Optimization for LLM Usage

### Output Consistency and Parseability

- [ ] **Structured Text Formats**: Add `--format` options that produce machine-readable but text-based outputs (like CSV, TSV, or key-value pairs) for commands like `analyze`, `callgraph`, and `probe` to make parsing easier without requiring JSON. [1](#2-0) 

- [ ] **Deterministic Ordering**: Ensure all list commands (`symbol list`, `segment list`) output results in a consistent sort order (by address, then name) so LLMs can rely on stable output across sessions. [2](#2-1) 

- [ ] **Verbose Mode**: Add a global `--verbose` flag that includes execution metadata (execution time, bytes processed, confidence scores) to help LLMs understand command results and debug their own analysis workflows. [3](#2-2) 

### Command Design Improvements

- [ ] **Atomic Operations**: Ensure commands are idempotent where possible (e.g., running `symbol define` twice with the same parameters produces the same state) to make LLM retry logic simpler. [4](#2-3) 

- [ ] **Compound Commands**: Add shorthand commands that combine common operations, like `analyze-disassemble <start> <bytes>` that runs analysis then disassembly in one step, reducing the number of CLI roundtrips. [5](#2-4) 

- [ ] **Validation Feedback**: Add `--dry-run` modes for destructive operations (like `atr inject`) that show what would happen without making changes, helping LLMs verify operations before executing them. [6](#2-5) 

## Expanded Hacking Functionality

### Advanced Pattern Analysis

- [ ] **Pattern Library**: Add a `patterns` command to save and reuse byte patterns with metadata, enabling LLMs to build and share pattern libraries for common Atari game structures. [7](#2-6) 

- [ ] **Structural Pattern Matching**: Add commands to detect common game structures (level headers, sprite tables, music data formats) using configurable templates, going beyond the current 7 probe heuristics. [8](#2-7) 

### Cross-Reference and Data Flow

- [ ] **Enhanced Xref**: Expand `xref` to show data flow analysis (which addresses read/write which memory locations) and not just code references, helping LLMs understand data usage patterns. [9](#2-8) 

- [ ] **Memory Access Tracing**: Add a `trace-access` command that statically identifies potential memory access patterns from instruction sequences, helping identify data structures without runtime execution. [9](#2-8) 

### Improved Static Analysis

- [ ] **Control Flow Simplification**: Add commands to identify and annotate common control flow patterns (state machines, jump tables, coroutine patterns) that are currently difficult to analyze statically. [10](#2-9) 

- [ ] **Stack Analysis**: Add stack depth analysis to `trace` to identify potential stack usage patterns and subroutine calling conventions, helping LLMs understand function signatures. [9](#2-8) 

### Disk and Filesystem Operations

- [ ] **ATR Forensics**: Add commands for deep ATR analysis (sector usage maps, file fragmentation analysis, deleted file recovery) to support more comprehensive disk image investigation. [6](#2-5) 

- [ ] **Batch File Operations**: Add commands to extract/inject multiple files at once using pattern matching, reducing the number of CLI calls needed for bulk operations. [11](#2-10) 

## Notes

The current batch scripting capability is already well-suited for LLM automation, as it allows complex multi-step workflows to be encoded in text files that LLMs can generate and execute. [12](#2-11)  The persistence system with sidecar JSON files provides good state management for iterative analysis sessions. [13](#2-12) 

Wiki pages you might want to explore:
- [AtariHacker Overview (pdavis68/AtariHacker)](/wiki/pdavis68/AtariHacker#1)
- [Roadmap and Future Development (pdavis68/AtariHacker)](/wiki/pdavis68/AtariHacker#9)

### Citations

**File:** README.md (L67-89)
```markdown
## Usage

### Quick start

```bash
# Load a target and run a command
atarihacker -- --target game.rom info

# Or use a config file to avoid repeating the target
echo '{"target": "game.rom"}' > .atari-hacker.config
atarihacker -- info
atarihacker -- disassemble 0 100
```

### Global options

| Option | Description |
|--------|-------------|
| `-t, --target <path>` | Target file path (ATR, ROM, XEX). Overrides `.atari-hacker.config`. |
| `-c, --config <path>` | Path to config file. Default: searches current and parent directories for `.atari-hacker.config`. |
| `--version` | Show version information |
| `-?, -h, --help` | Show help |

```

**File:** README.md (L137-148)
```markdown
### Search

| Command | Description |
|---------|-------------|
| `find-pattern <pattern>` | Search for a byte pattern with optional wildcards (`??`) |
| `find-strings [options]` | Search for runs of printable ASCII or ATASCII characters |

`find-strings` options:
- `--min-length <n>` — Minimum string length (default: 4)
- `--encoding <enc>` — String encoding: `ascii` (default) or `atascii`
- `--filter <text>` — Optional substring filter
- `--max-results <n>` — Maximum number of results (default: 50)
```

**File:** README.md (L150-168)
```markdown
### Analysis

| Command | Description |
|---------|-------------|
| `analyze [options]` | Multi-pass analysis to build reference graph and identify code/data regions |
| `probe <start> <end>` | Analyze a memory range to identify data type |
| `callgraph [options]` | Generate a call graph showing subroutine relationships |
| `coverage <start> <end>` | Analyze code coverage — which bytes are executed vs. data |

`analyze` options:
- `--start-address <addr>` — Starting address for analysis (hex)
- `--bytes <n>` — Number of bytes to analyze
- `--format <fmt>` — Output format: `summary` (default), `graph`, `labels`, or `full`

`callgraph` options:
- `--start-address <addr>` — Starting address for call graph root (hex)
- `--depth <n>` — Maximum call depth (default: 3)
- `--format <fmt>` — Output format: `mermaid` (default) or `text`

```

**File:** README.md (L169-179)
```markdown
### Control flow & cross-reference

| Command | Description |
|---------|-------------|
| `trace <address> [options]` | Statically trace execution from a starting address |
| `xref <address>` | Find locations that reference a target address |

`trace` options:
- `--max-depth <n>` — Maximum call depth (default: 5)
- `--max-instructions <n>` — Instruction budget (default: 500)

```

**File:** README.md (L180-211)
```markdown
### Symbol management

| Command | Description |
|---------|-------------|
| `symbol define <address> <label>` | Add or update a named label for a memory address |
| `symbol remove <address>` | Remove a user-defined symbol |
| `symbol lookup <address>` | Look up the symbol entry for an address |
| `symbol list [options]` | List symbols in the symbol table |
| `symbol set [options]` | Enable or disable groups of built-in symbols |

`symbol define` options:
- `--comment <text>` — Optional comment

`symbol list` options:
- `--include-hardware` — Include built-in hardware symbols
- `--filter <text>` — Optional substring filter

`symbol set` options:
- `--hardware <bool>` — Enable hardware register symbols (GTIA, POKEY, PIA, ANTIC)
- `--os-variables <bool>` — Enable OS zero-page variable symbols
- `--os-rom <bool>` — Enable OS ROM entry point symbols
- `--user-labels <bool>` — Enable user-defined symbols

### Segment management

| Command | Description |
|---------|-------------|
| `segment define <name> <type> <start> <end>` | Define a memory segment by type |
| `segment remove <name>` | Remove a defined memory segment by name |
| `segment list` | List all defined memory segments (shows gaps) |
| `segment clear` | Clear all defined segments |
| `segment linker-config <output>` | Generate a cc65 linker configuration from current segments |
```

**File:** README.md (L238-254)
```markdown
### ATR disk image operations

| Command | Description |
|---------|-------------|
| `atr info <path>` | Display structural information about an ATR disk image |
| `atr header <path>` | Display the ATR header fields |
| `atr directory <path>` | List the directory of a DOS-formatted ATR disk image |
| `atr create <output> <sectors> <density>` | Create a new ATR disk image from scratch |
| `atr extract <path> <name> <output>` | Extract a file from an ATR image and save to disk |
| `atr inject <path> <name> <input>` | Inject a file into an ATR (copy-on-write, creates `.modified` copy) |
| `atr write-sector <path> <sector> <input>` | Write raw data to a specific sector of an ATR |
| `atr write-file <path> <name> <input>` | Write a file to an ATR with directory entry creation |
| `atr analyze-boot <path>` | Decode the boot sector header from an ATR |
| `atr sector-dump <path> <sector>` | Hex dump sectors from an ATR by logical sector number |
| `atr search-boot <paths> [options]` | Scan boot sectors across multiple ATRs for patterns or differences |
| `atr filesystem <path> <options>` | Define a custom filesystem layout for non-DOS ATR images |

```

**File:** README.md (L280-286)
```markdown
## Multi-Pass Disassembly

When `--analyze` is passed to `disassemble`, the engine performs three passes:

1. **Pass 1 — Reference Collection**: Scans all instruction boundaries across the ROM, recording JSR targets, JMP targets, branch targets, indirect jump targets, and absolute/indirect data references into a `ReferenceGraph`.
2. **Pass 2 — Code Region Tracing**: Starting from each code entry point, traces execution flow (following JSR, JMP, branches, stopping at RTS/RTI/BRK) to mark bytes as code or data.
3. **Pass 3 — Label Generation**: Produces meaningful labels (`sub_XXXX`, `jmp_XXXX`, `data_XXXX`, `L_XXXX`) with proper priority ordering (user > subroutine > data > hardware > branch). Zero-page OS variable symbols are shown as operand comments, not code labels.
```

**File:** README.md (L343-362)
```markdown
## Batch Scripting

The `script` command executes sequences of commands from text files:

```bash
# disassemble_all.txt
load game.xex
symbol define address=$1540 label=game_init
segment define name=boot_loader start=$0700 end=$087F type=code
segment define name=main_code start=$0C00 end=$1CFF type=code
disassemble offset=$0700 numBytes=384 format=ca65 analyze=true
segment linker-config output=game.cfg
labels save --output=game.annotations.json
```

```bash
atarihacker -- script disassemble_all.txt
```

The script format uses `command key=value` syntax. Quoted values are supported for values containing spaces.
```

**File:** README.md (L364-376)
```markdown
## Data Probing

`probe` uses 7 heuristics to identify data types in memory ranges:

- **String detection** — ATASCII/ASCII printable runs, `$9B`-terminated, null-terminated
- **Padding detection** — Runs of `$00`, `$FF`, `$1A` (ATASCII EOL)
- **Character set detection** — 1024-byte (128×8) and 512-byte (64×8) blocks
- **Table detection** — 2-byte address tables, 1-byte lookup tables
- **Display list detection** — ANTIC display list opcodes with LMS extraction
- **Sprite data detection** — 8/16/32-byte aligned blocks with byte variety analysis
- **Map data detection** — 2D grid patterns with consistent row lengths

Each result includes a confidence level (High/Medium/Low) and supporting details.
```

**File:** README.md (L397-422)
```markdown
### Analysis-driven disassembly

```bash
# Load and get overview
atarihacker -- analyze --format summary

# View auto-generated labels
atarihacker -- analyze --format labels

# Identify data regions
atarihacker -- probe $1712 $1AD4

# Define segments
atarihacker -- segment define name=main_code start=$0C00 end=$1CFF type=code
atarihacker -- segment define name=game_data start=$1D00 end=$BBA4 type=data

# Segment-aware disassembly with analysis
atarihacker -- disassemble $0C00 5376 --format ca65 --analyze

# Visualize structure
atarihacker -- callgraph --start-address $1540 --depth 5 --format mermaid

# Iterate: annotate and re-disassemble
atarihacker -- symbol define $1540 game_init
atarihacker -- disassemble $0C00 5376 --format ca65 --analyze
```
```

**File:** README.md (L445-462)
```markdown
### ATR modification workflow

```bash
# Create blank disk
atarihacker -- atr create build/disk.atr 720 sd

# Write boot sector
atarihacker -- atr write-sector build/disk.atr 1 boot.bin

# Add file
atarihacker -- atr write-file build/disk.atr AGENT.OBJ build/AGENT.OBJ

# Extract file
atarihacker -- atr extract game.atr AGENT.OBJ extracted/AGENT.OBJ

# Replace file
atarihacker -- atr inject game.atr AGENT.OBJ build/AGENT.OBJ
```
```

**File:** README.md (L464-478)
```markdown
## Persistence

User-defined symbols, zero-page annotations, and segment definitions are saved automatically to a sidecar JSON file next to the loaded target:

```
<rom-or-synthetic-path>.atarihacker.json
```

The v4 sidecar format includes:
- Version field for forward compatibility
- SHA-256 ROM hash for integrity checking
- Segments (name, type, start, end, comment)
- Custom filesystem definitions

v3 sidecar files (without version field) are read transparently and upgraded on save.
```
