# Design Document: ATR Forensics & Batch File Operations

**Issue:** Expanded Hacking Functionality — Disk and Filesystem Operations  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:43)  
**Status:** Draft

---

## 1. Problem Statement

### 1.1 ATR Forensics
The current ATR tools (`atr info`, `atr directory`, `atr header`) provide basic disk inspection but lack deeper forensic capabilities:

- **Sector usage maps**: No visualization of which sectors are used, free, or damaged
- **File fragmentation analysis**: No way to see how fragmented files are across the disk
- **Deleted file recovery**: Deleted directory entries are shown but the sector data is not recoverable
- **VTOC inspection**: No way to examine the Volume Table of Contents bitmap directly
- **Boot sector analysis**: The `analyze-boot` command exists but is limited to header decoding

### 1.2 Batch File Operations
Current file operations (`atr extract`, `atr inject`) work on single files. For bulk operations (extracting all files from a disk, injecting multiple files), users must make repeated CLI calls, which is slow for LLM-driven workflows.

## 2. Proposed Design

### 2.1 ATR Forensics Commands

#### `atr sector-map`

Visualize sector usage across the disk:

```bash
atr sector-map <path> [--format text|ascii|svg]
```

**Output (text format):**
```
Sector map for GAME.ATR (720 sectors, SD):
  Sectors 001-003: [BOOT] Boot loader
  Sectors 004-359: [DATA] File data
  Sectors 360-360: [VTOC] Volume Table of Contents
  Sectors 361-368: [DIR]  Directory
  Sectors 369-720: [FREE] Free

Usage: 368/720 sectors (51.1% used)
Fragmentation: 12 free regions, largest: 152 sectors
```

**Output (ASCII art):**
```
Sector Map: GAME.ATR (720 sectors)
┌──────────────────────────────────────────────┐
│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░│
│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░│
│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░│
│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░│
└──────────────────────────────────────────────┘
▓=Used  ░=Free
```

#### `atr file-frag <name>`

Analyze fragmentation of a specific file:

```bash
atr file-frag <path> <name>
```

```
Fragmentation analysis for AGENT.OBJ:
  File size: 12,345 bytes
  Total sectors: 97
  Fragments: 5
  Fragmentation ratio: 5.2% (low)
  
  Sector chain:
    042 → 045 → 048 → 051 → 054 → ...
    Gap: 042→045 (2 sectors), 045→048 (2 sectors), ...
```

#### `atr recover <name> <output>`

Recover a deleted file by name:

```bash
atr recover <path> <name> <output>
```

```
Recovering deleted file 'DELETED.OBJ'...
  Directory entry found at sector 365, offset 4
  Status: Deleted (flag = $80)
  Original size: 8,192 bytes
  Start sector: 200
  Recovering sector chain starting at 200...
  Recovery complete: 8,192 bytes written to recovered/DELETED.OBJ
```

**Algorithm:**
1. Scan directory sectors for entries with deleted flag ($80 in first byte)
2. Match by filename (case-insensitive)
3. Read the start sector from the directory entry
4. Follow the sector chain (same as `ExtractFile`)
5. Write recovered data to output path

#### `atr vtoc <path>`

Display the VTOC bitmap:

```bash
atr vtoc <path>
```

```
VTOC analysis for GAME.ATR:
  Sector: 360
  Total sectors: 720
  Free sectors: 352
  Used sectors: 368
  
  Bitmap (first 32 bytes):
    FF FF FF FF FF FF FF FF  FF FF FF FF FF FF FF FF
    FF FF FF FF FF FF FF FF  FF FF FF FF 00 00 00 00
    ...
  
  Free sector ranges:
    369-720 (352 sectors)
```

### 2.2 Batch File Operations

#### `atr extract-all <path> [--output-dir <dir>]`

Extract all files from the disk:

```bash
atr extract-all GAME.ATR --output-dir extracted/
```

```
Extracting all files from GAME.ATR...
  [1/8] AGENT.OBJ → extracted/AGENT.OBJ (12,345 bytes)
  [2/8] LEVEL1.DAT → extracted/LEVEL1.DAT (4,096 bytes)
  [3/8] LEVEL2.DAT → extracted/LEVEL2.DAT (4,096 bytes)
  [4/8] LEVEL3.DAT → extracted/LEVEL3.DAT (4,096 bytes)
  [5/8] MUSIC.DAT → extracted/MUSIC.DAT (8,192 bytes)
  [6/8] SPRITES.OBJ → extracted/SPRITES.OBJ (16,384 bytes)
  [7/8] TITLE.SCR → extracted/TITLE.SCR (8,192 bytes)
  [8/8] HIGHSCR.DAT → extracted/HIGHSCR.DAT (128 bytes)
  Complete: 8 files extracted (57,529 bytes total)
```

#### `atr inject-all <path> <source-dir> [--pattern <glob>]`

Inject multiple files matching a pattern:

```bash
atr inject-all GAME.ATR build/ --pattern "*.OBJ"
```

```
Injecting files into GAME.ATR (copy-on-write: GAME.ATR.modified)...
  [1/3] AGENT.OBJ → GAME.ATR (12,345 bytes) ✓
  [2/3] SPRITES.OBJ → GAME.ATR (16,384 bytes) ✓
  [3/3] TITLE.SCR → GAME.ATR (8,192 bytes) ✓
  Complete: 3 files injected (36,921 bytes total)
  
  Note: File HIGHSCR.DAT (128 bytes) skipped — no matching source file
```

#### `atr batch <path> <script>`

Execute a batch of ATR operations from a script file:

```bash
atr batch GAME.ATR operations.txt
```

Script format:
```
# operations.txt
extract AGENT.OBJ extracted/
inject build/AGENT.OBJ AGENT.OBJ
extract-all --output-dir extracted/
sector-map --format ascii
```

## 3. Implementation Plan

### Phase 1: Forensic Tools

1. Create [`Tools/AtrForensicTools.cs`](../Tools/AtrForensicTools.cs):
   - `SectorMap` — sector usage visualization
   - `FileFragmentation` — fragmentation analysis
   - `RecoverDeletedFile` — deleted file recovery
   - `ShowVtoc` — VTOC bitmap display

2. Add helper methods to [`Atari/AtrParser.cs`](../Atari/AtrParser.cs):
   - `GetSectorBitmap()` — return free/used bitmap
   - `FindDeletedEntry(string name)` — locate deleted directory entries
   - `GetSectorChain(int startSector)` — follow sector chain

### Phase 2: Batch Operations

1. Add to [`Tools/AtrTools.cs`](../Tools/AtrTools.cs):
   - `ExtractAll` — bulk extract
   - `InjectAll` — bulk inject with glob pattern matching

2. Add to [`Tools/AtrWriteTools.cs`](../Tools/AtrWriteTools.cs):
   - `BatchOperations` — scripted batch execution

### Phase 3: Integration

1. Register new commands in [`Program.cs`](../Program.cs)
2. Add `--dry-run` support to batch operations

## 4. API/Syntax Changes

```bash
# Forensics
atarihacker -- atr sector-map GAME.ATR
atarihacker -- atr sector-map GAME.ATR --format ascii
atarihacker -- atr file-frag GAME.ATR AGENT.OBJ
atarihacker -- atr recover GAME.ATR DELETED.OBJ recovered/
atarihacker -- atr vtoc GAME.ATR

# Batch operations
atarihacker -- atr extract-all GAME.ATR --output-dir extracted/
atarihacker -- atr inject-all GAME.ATR build/ --pattern "*.OBJ"
atarihacker -- atr batch GAME.ATR operations.txt

# Dry-run support
atarihacker -- atr inject-all GAME.ATR build/ --dry-run
```

## 5. Data Structures

### New: `SectorInfo`

```csharp
public record SectorInfo(
    int SectorNumber,
    SectorType Type,     // Boot, VTOC, Directory, FileData, Free
    int? FileIndex,      // Which file this sector belongs to
    int? NextSector      // Next sector in chain (if file data)
);

public enum SectorType { Boot, VTOC, Directory, FileData, Free }
```

### New: `FragmentationResult`

```csharp
public record FragmentationResult(
    string FileName,
    int FileSize,
    int TotalSectors,
    int FragmentCount,
    double FragmentationRatio,
    List<int> SectorChain,
    List<(int From, int To)> Gaps
);
```

### New: `RecoveryResult`

```csharp
public record RecoveryResult(
    string FileName,
    int OriginalSize,
    int RecoveredSize,
    int StartSector,
    bool Success,
    string? ErrorMessage
);
```

## 6. Affected Files

| File                              | Change                                                |
|-----------------------------------|-------------------------------------------------------|
| `Tools/AtrForensicTools.cs`       | **New** — forensic analysis tools                     |
| `Atari/AtrParser.cs`              | Add `GetSectorBitmap()`, `FindDeletedEntry()`, `GetSectorChain()` |
| `Tools/AtrTools.cs`               | Add `ExtractAll`, `InjectAll` methods                 |
| `Tools/AtrWriteTools.cs`          | Add `BatchOperations` method                          |
| `Program.cs`                      | Register new `atr` sub-commands                       |

## 7. Testing Considerations

- Sector map: verify correct classification of boot/VTOC/directory/data/free sectors
- Fragmentation: verify with known fragmented file (multiple gaps in sector chain)
- Deleted file recovery: verify with deleted entry — should recover original data
- VTOC display: verify bitmap matches actual free/used sectors
- Extract-all: verify all files are extracted with correct sizes
- Inject-all: verify only matching files are injected
- Batch script: verify all operations in script execute in order
- Dry-run: verify no modifications when `--dry-run` is set
- Edge cases: empty disk, full disk, non-DOS formatted disk, corrupted VTOC
