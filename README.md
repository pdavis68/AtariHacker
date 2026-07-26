# AtariHacker

A command-line toolkit for reverse-engineering Atari 8-bit binaries, ROMs, and ATR disk images. Supports multi-pass disassembly analysis, code/data separation, segment-aware output, ATR file injection, batch scripting, and a comprehensive Atari hardware symbol table.

## Requirements

- .NET SDK 10.0+
- Linux, macOS, or Windows

## Build

```bash
dotnet build AtariHacker/AtariHacker.csproj
```

Publish a release build:

```bash
dotnet publish AtariHacker/AtariHacker.csproj -c Release -o publish
./publish/AtariHacker --help
```

## Install

Pre-built binaries are available for each [release](https://github.com/pdavis68/AtariHacker/releases). The binaries are self-contained single-file executables — no .NET runtime required.

### Linux

```bash
# x64 (Intel/AMD)
curl -sL https://github.com/pdavis68/AtariHacker/releases/latest/download/AtariHacker-linux-x64.tar.gz | tar xz
sudo mv AtariHacker-linux-x64 /usr/local/bin/atarihacker

# ARM64 (e.g. Raspberry Pi)
curl -sL https://github.com/pdavis68/AtariHacker/releases/latest/download/AtariHacker-linux-arm64.tar.gz | tar xz
sudo mv AtariHacker-linux-arm64 /usr/local/bin/atarihacker
```

### macOS

```bash
# Intel (x64)
curl -sL https://github.com/pdavis68/AtariHacker/releases/latest/download/AtariHacker-osx-x64.tar.gz | tar xz
sudo mv AtariHacker-osx-x64 /usr/local/bin/atarihacker

# Apple Silicon (M1/M2/M3/M4)
curl -sL https://github.com/pdavis68/AtariHacker/releases/latest/download/AtariHacker-osx-arm64.tar.gz | tar xz
sudo mv AtariHacker-osx-arm64 /usr/local/bin/atarihacker
```

### Windows (PowerShell)

```powershell
# x64
Invoke-WebRequest -Uri https://github.com/pdavis68/AtariHacker/releases/latest/download/AtariHacker-win-x64.zip -OutFile AtariHacker-win-x64.zip
Expand-Archive -Path AtariHacker-win-x64.zip -DestinationPath .
Move-Item AtariHacker-win-x64.exe (Some directory in your `PATH`)\atarihacker.exe
```


### Verify

```bash
atarihacker --version
```

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

### Config file

The `.atari-hacker.config` file is a simple JSON file that specifies the default target:

```json
{
  "target": "path/to/your/file.atr"
}
```

When a config file is present, the `--target` option can be omitted. The config file is searched upward from the current directory, so you can place it in a project root.

### Specifying addresses

All addresses can be specified in decimal, hex with `$` prefix, or hex with `0x` prefix:

```bash
# These are all equivalent:
atarihacker -- disassemble 0x700 256
atarihacker -- disassemble $700 256
atarihacker -- disassemble 1792 256
```

## Commands

### File operations

| Command | Description |
|---------|-------------|
| `load <path>` | Load a ROM, XEX, or ATR file into the session |
| `info` | Display information about the currently loaded binary |
| `script <path>` | Execute a sequence of commands from a script file |

### Disassembly

| Command | Description |
|---------|-------------|
| `disassemble <offset> <bytes> [options]` | Disassemble 6502 machine code |
| `hex-dump <offset> <bytes> [options]` | Produce a hex dump with file offsets, memory addresses, and ASCII |

`disassemble` options:
- `--start-address <addr>` — Override memory start address
- `--format <format>` — Output format: `listing` (default), `ca65`, `atasm`, or `mac65`
- `--analyze` — Use multi-pass analysis for label generation and code/data separation

When `--analyze` is used, the engine generates meaningful labels (`sub_XXXX`, `data_XXXX`, `jmp_XXXX`), code/data separation, segment-aware `.segment`/`.proc`/`.endproc` output, ATASCII string formatting, and procedure header comments.

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

### Control flow & cross-reference

| Command | Description |
|---------|-------------|
| `trace <address> [options]` | Statically trace execution from a starting address |
| `xref <address>` | Find locations that reference a target address |

`trace` options:
- `--max-depth <n>` — Maximum call depth (default: 5)
- `--max-instructions <n>` — Instruction budget (default: 500)

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

`segment define` types: `code`, `data`, `graphics`, `text`, `zero_page`

### Zero page management

| Command | Description |
|---------|-------------|
| `zero-page annotate <address> <label>` | Add or update a zero page annotation |
| `zero-page show [options]` | Display zero page annotations |

`zero-page annotate` options:
- `--comment <text>` — Optional comment

`zero-page show` options:
- `--all` — Show all 256 bytes of zero page

### Labels

| Command | Description |
|---------|-------------|
| `labels load <path>` | Load labels and segments from a sidecar file |
| `labels save [options]` | Save current labels and segments to a sidecar file |

`labels save` options:
- `--output <path>` — Optional output path (defaults to ROM path + `.atarihacker.json`)

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

`atr create` densities: `sd` (single, 128-byte sectors), `dd` (double, 256-byte), `ed` (enhanced, 128-byte)

`atr sector-dump` options:
- `--count <n>` — Number of consecutive sectors to dump (default: 1)

`atr search-boot` options:
- `--pattern <hex>` — Hex byte pattern with `??` wildcards
- `--mode <mode>` — Search mode: `pattern` (default) or `diff`

### Diff

| Command | Description |
|---------|-------------|
| `diff <file1> <file2> [options]` | Compare two ROM or ATR files |

`diff` options:
- `--format <fmt>` — Format: `summary` (default), `verbose`, or `hex`

### Utilities

| Command | Description |
|---------|-------------|
| `hex-to-decimal <hex>` | Convert a hexadecimal value to decimal |
| `decimal-to-hex <value>` | Convert a decimal integer to hexadecimal |

## Multi-Pass Disassembly

When `--analyze` is passed to `disassemble`, the engine performs three passes:

1. **Pass 1 — Reference Collection**: Scans all instruction boundaries across the ROM, recording JSR targets, JMP targets, branch targets, indirect jump targets, and absolute/indirect data references into a `ReferenceGraph`.
2. **Pass 2 — Code Region Tracing**: Starting from each code entry point, traces execution flow (following JSR, JMP, branches, stopping at RTS/RTI/BRK) to mark bytes as code or data.
3. **Pass 3 — Label Generation**: Produces meaningful labels (`sub_XXXX`, `jmp_XXXX`, `data_XXXX`, `L_XXXX`) with proper priority ordering (user > subroutine > data > hardware > branch). Zero-page OS variable symbols are shown as operand comments, not code labels.

### Example

```ca65
; --------------------------------------------------
; Generated by Atari Hacker v4
; --------------------------------------------------

.segment "MAIN_CODE"
.org $0C00

; --------------------------------------------------
; Subroutine: game_init
; Calls:     load_ag_obj, load_ag_dat, main_loop
; --------------------------------------------------
.proc game_init
    LDA #$00
    STA $D400          ; DMACTL
    ...
.endproc

; String table at $1712
credits_text:
    .byte "Scholastic", $9B    ; $9B = ATASCII EOL
    .byte "Wizware", $9B
    .byte $00
```

## Atari Hardware Symbol Table

The built-in symbol table covers over 200 entries across all major Atari components:

| Group | Range | Entries |
|-------|-------|---------|
| **GTIA** | $D000–$D01F | 22 registers (player/missile graphics, color, control) |
| **POKEY** | $D200–$D21F | 32 registers (sound, I/O, reserved, read/write aliasing) |
| **PIA** | $D300–$D303 | 4 registers (parallel I/O) |
| **ANTIC** | $D400–$D41F | 20 registers (DMA, display list, NMI, reserved) |
| **OS ROM** | $C000–$FFFF | 13 entry points (SIO, CIO, vectors) |
| **Zero page OS** | $00–$FF | ~220 variables (timers, I/O control, floating point, screen, paddles, joysticks) |

Symbol groups can be toggled with `symbol set` to avoid conflicts when user code overlaps OS address ranges.

## ATR Write Operations

The ATR write subsystem provides copy-on-write disk image modification:

| Operation | Command | Description |
|-----------|---------|-------------|
| Extract | `atr extract <path> <name> <output>` | Extract a DOS file from an ATR to the host filesystem |
| Inject | `atr inject <path> <name> <input>` | Replace a file entry's data in an ATR (creates `.modified` copy) |
| Create | `atr create <output> <sectors> <density>` | Create a blank ATR with specified sector count and density |
| Write sector | `atr write-sector <path> <sector> <input>` | Write raw binary data to a specific sector |
| Write file | `atr write-file <path> <name> <input>` | Create a new DOS file entry with sector allocation |
| Filesystem | `atr filesystem <path> <options>` | Describe a non-DOS filesystem layout for custom-format disks |

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

## Typical Workflow

### Basic exploration

```bash
# With config file
echo '{"target": "game.rom"}' > .atari-hacker.config

# Inspect structure
atarihacker -- info

# Examine raw bytes and find text
atarihacker -- hex-dump 0 256
atarihacker -- find-strings

# Basic disassembly
atarihacker -- disassemble 0 100
```

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

### Iterative refinement workflow

```bash
# 1. Initial analysis
atarihacker -- analyze

# 2. Annotate key addresses
atarihacker -- symbol define $1540 game_init --comment "Main game entry"

# 3. Mark code/data regions
atarihacker -- segment define name=main_code start=$0C00 end=$1CFF type=code

# 4. Persist annotations
atarihacker -- labels save

# 5. Re-disassemble with annotations
atarihacker -- disassemble $0C00 5376 --format ca65 --analyze

# 6. Review, repeat steps 2-5 as needed
```

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

## Notes

- The `disassemble` command without `--analyze` behaves exactly as in v3 — full backward compatibility.
- Self-modifying code and jump table dispatch (`JMP (table,X)`) cannot be resolved statically; use `segment define` to mark those regions manually.
- For full design details, see the design documents in [`docs/`](docs/).

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
