# AtariHackerMCP v2 Implementation Plan

## Overview

This plan implements all changes described in `docs/version2-design.md`, organized into 4 phases as recommended by the design document. Each todo item maps to specific code changes in specific files.

---

## Phase 1: Bug Fixes (items 1–5)

These are the highest-priority changes, unblocking ATR analysis for non-DOS disks.

### Todo 1: Add `HasDosFilesystem()` to `AtrParser.cs` and fix `atr_info` crash

**File:** [`Atari/AtrParser.cs`](Atari/AtrParser.cs)

**Changes:**
- Add static method `HasDosFilesystem(byte[] data)` implementing the VTOC heuristic from §2.1 Layer 1
  - Parse geometry from data
  - Check `geometry.SectorCount >= 368`
  - Read VTOC sector (sector 360)
  - Validate: `vtoc[0]` (dir sector count) in range 1–16
  - Validate: `(vtoc[1] | vtoc[2] << 8)` (total sectors) in range 1..geometry.SectorCount
  - Return `true` if all checks pass

**File:** [`Atari/AtrParser.cs`](Atari/AtrParser.cs), method `ReadDirectory()` (~line 94–100)

**Changes (§4.2):**
- After reading `sectorCount` (~line 99): add guard `if (sectorCount == 0 || sectorCount > geometry.SectorCount) continue;`
- After reading `startSector` (~line 100): add guard `if (startSector == 0 || startSector > geometry.SectorCount) continue;`

**File:** [`Tools/AtrTools.cs`](Tools/AtrTools.cs), method `AtrInfo()` (~line 37)

**Changes (§2.1 Layer 2):**
- Wrap the `ExtractFile` call + formatting inside a `try-catch`
- On exception, emit a warning row `[unreadable]` instead of crashing
- Add: if `!AtrParser.HasDosFilesystem(bytes)` → skip directory listing entirely with a graceful message

---

### Todo 2: Add directory entry sector bounds validation to `ReadDirectory()`

**Already covered in Todo 1 above** (the guard clauses in `ReadDirectory`).

---

## Phase 2: Quick Wins (items 3–6)

### Todo 3: Add `atr_header` tool

**File:** [`Tools/AtrTools.cs`](Tools/AtrTools.cs) — add new method

**Method:** `AtrHeader(string filePath)`
- Read file bytes, validate with `IsAtr()`
- Parse geometry with `ParseGeometry()`
- Decode header bytes: paragraphs (bytes 2-3 + 6-7), write-protect flag (byte 8)
- Return formatted report with magic, image size, sector size, sector count, density, write protect

### Todo 4: Add `list_atr_directory` tool

**File:** [`Tools/AtrTools.cs`](Tools/AtrTools.cs) — add new method

**Method:** `ListAtrDirectory(string filePath)`
- Read file bytes, validate with `IsAtr()`
- Guard with `HasDosFilesystem()` — return helpful error if not DOS-formatted
- Parse geometry, read directory via `AtrParser.ReadDirectory()`
- Filter into active/deleted entries
- Format directory listing with flags (binary, locked)
- Compute free sectors via `FreeSegmentCount()`
- Return formatted report

### Todo 5: Add `analyze_boot_sector` tool

**File:** [`Tools/AtrTools.cs`](Tools/AtrTools.cs) — add new method

**Method:** `AnalyzeBootSector(string filePath)`
- Read file bytes, validate with `IsAtr()`
- Extract boot sectors via `AtrParser.ExtractBootSectors()`
- Decode 6-byte header: boot flag, sector count, load address (LE), init address (LE)
- Implement `isDosBoot` heuristic: init address in $0700–$07FF range
- Return formatted report with all decoded fields

---

## Phase 3: Deeper Tools (items 6–8)

### Todo 6: Change `SectorFileOffset` to `internal` visibility

**File:** [`Atari/AtrParser.cs`](Atari/AtrParser.cs), line 203

**Change:**
- `private static int SectorFileOffset(...)` → `internal static int SectorFileOffset(...)`

### Todo 7: Add `sector_dump` tool

**File:** [`Tools/AtrTools.cs`](Tools/AtrTools.cs) — add new method

**Method:** `SectorDump(string filePath, string sector, int count = 1)`
- Read file bytes, validate with `IsAtr()`
- Parse geometry
- Parse sector number (1-based), validate range
- Clamp `count` to available sectors
- Build contiguous byte buffer from requested sectors using `ReadSector()`
- Compute starting file offset via `AtrParser.SectorFileOffset()`
- Return hex dump with sector-relative address column

**Requires** a new overload of `GenerateHexDump` that accepts a custom address label formatter.

### Todo 8: Add `search_boot_sector` tool

**File:** [`Tools/AtrTools.cs`](Tools/AtrTools.cs) — add new method

**Method:** `SearchBootSector(string[] filePaths, string? pattern = null, string compareMode = "pattern")`
- Validate each path is ATR; skip non-ATR with warnings
- Extract boot sectors from each
- Mode `"pattern"`: run byte pattern matching on each boot block using the `FindPattern`-style logic
- Mode `"diff"`: pairwise byte comparison, report identical percentage
- Return formatted results per design doc

---

## Phase 4: Polish (items 9–15)

### Todo 9: Create shared `AtasciiDecoder` helper

**New file:** [`Helpers/AtasciiDecoder.cs`](Helpers/AtasciiDecoder.cs)

- `internal static class AtasciiDecoder`
- `DecodeByte(byte b) → char` — single-byte ATASCII decode (extracted from `StringSearchTool.TryDecode`)
- `Decode(ReadOnlySpan<byte> bytes) → string` — span decode
- For inverse characters (bit 7 set), prefix with `~` same as current behavior

### Todo 10: Refactor `StringSearchTool` to use shared `AtasciiDecoder`

**File:** [`Tools/StringSearchTool.cs`](Tools/StringSearchTool.cs)

- Remove the inline `TryDecode` method
- Replace with calls to `AtasciiDecoder.DecodeByte()` in the main loop
- Keep the run-flush logic unchanged

### Todo 11: Add ATASCII filename support to `AtrParser.ReadPaddedString`

**File:** [`Atari/AtrParser.cs`](Atari/AtrParser.cs), method `ReadPaddedString`

- Add optional `bool atascii = false` parameter
- When `atascii` is `true`, use `AtasciiDecoder.Decode()` instead of `Encoding.ASCII.GetString()`
- Plumb through `ReadDirectory()` with a heuristic: if `HasDosFilesystem` is false and the current entries look ATASCII-ish, try ATASCII decoding

### Todo 12: Update `RomSession` with ATR context fields

**File:** [`State/RomSession.cs`](State/RomSession.cs)

**Add fields:**
- `public string? SourceAtrPath { get; set; }`
- `public BootHeader? BootHeader { get; set; }`

**Add record** (either in same file or a separate file):
- `public record BootHeader(byte Flag, byte SectorCount, ushort LoadAddress, ushort InitAddress);`

**Update `Load()` to clear these fields** (alongside existing `ClearMetadata()` calls):
- Set `SourceAtrPath = null; BootHeader = null;`

### Todo 13: Update `LoadAtrFile` and `LoadAtrBoot` to set ATR context

**File:** [`Tools/AtrTools.cs`](Tools/AtrTools.cs)

**`LoadAtrFile`:**
- After successful extraction, set `session.SourceAtrPath = resolvedPath`

**`LoadAtrBoot`:**
- After boot sector extraction, set `session.SourceAtrPath = resolvedPath`
- Decode the 6-byte header and set `session.BootHeader = new BootHeader(...)`

### Todo 14: Enhance `Disassemble` with address mapping advisory

**File:** [`Tools/DisassemblerTool.cs`](Tools/DisassemblerTool.cs)

- Update the `[Description]` attribute with the improved text (§6.1)
- At the start of the method, if `startAddress` is not provided AND no XEX segments AND no `BaseAddress`, prepend the advisory note to the output

### Todo 15: Enhance `TraceControlFlow` with BRK hint

**File:** [`Tools/ControlFlowTool.cs`](Tools/ControlFlowTool.cs)

- When the first instruction at the starting address is `BRK` (opcode `$00`):
  - Append a hint note about boot sector entry points to the output
  - Mention the `analyze_boot_sector` tool and `startAddress=$0706`

---

## Implementation Order

The work should be done in the order listed above (Phase 1 → Phase 2 → Phase 3 → Phase 4), but each phase can be implemented sequentially. The phases are designed to be incrementally testable — each phase adds value independently.

## Testing Notes

- After Phase 1: `atr_info` should no longer crash on non-DOS disks
- After Phase 2: New standalone tools (`atr_header`, `list_atr_directory`, `analyze_boot_sector`) should work independently
- After Phase 3: `sector_dump` and `search_boot_sector` should produce correct sector-level dumps
- After Phase 4: Disassembly and control flow should show helpful hints in ambiguous scenarios