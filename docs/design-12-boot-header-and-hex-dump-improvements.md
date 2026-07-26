# Boot Header Structural Recognition and Hex-Dump Sector Awareness

## Overview

Improve the disassembly analyzer to recognize the Atari boot sector header as a single structural unit, and enhance `hex-dump` to show sector boundaries when dumping from ATR images.

## Features

### 1. Boot Header Structural Recognition

**Purpose:** The analyzer should recognize the 6-byte boot header pattern (flag, sectors, load address, init address) and treat it as a single structural unit in disassembly output, rather than individual bytes with misleading labels.

**Current output (broken):**
```ca65
data_0700:
    .byte   $D0
data_0702:
    .byte   $00
data_0703:
    .byte   $07
data_0704:
    .byte   "@"
; CBAUD comments on $4C which is actually JMP opcode
```

**Desired output:**
```ca65
; Boot header
    .byte   $D0             ; Boot flag: $D0 = stop/run
    .byte   $03             ; Sectors to load: 3
    .word   $0700           ; Load address: $0700
    .word   $1540           ; Init address: $1540
```

**Implementation:**
1. Define a `BootHeaderPattern` structure that describes the 6-byte header
2. In `DisassemblyAnalyzer.Analyze`, when the boot header pattern is detected (byte 0 is `$00` or `$D0`), mark all 6 bytes as data AND add a special comment
3. In `FormatCa65Analyzed`, when emitting data bytes at the boot header address range, group them as a boot header annotation rather than individual `.byte` directives
4. Add a new label type `BootHeader` to distinguish from regular data labels

```csharp
public sealed record BootHeaderInfo(
    byte Flag,
    byte SectorCount,
    ushort LoadAddress,
    ushort InitAddress,
    string? Description = null);
```

### 2. Hex-Dump with ATR Sector Awareness

**Purpose:** When dumping from an ATR, `hex-dump` should show sector boundaries in the output, making it easier to understand the disk layout.

**Current output:**
```
Offset    Address   00 01 02 03 ...
--------  --------  ----------------
00000010  --------  D0 03 00 07 ...
```

**Improved output:**
```
Offset    Sector    Address   00 01 02 03 ...
--------  --------  --------  ----------------
00000010  Sctr 001  --------  D0 03 00 07 ...  (ATR header ends, sector data starts)
00000090  Sctr 002  --------  03 03 A9 31 ...
00000110  Sctr 003  --------  29 13 9D 31 ...
```

**Implementation:**
1. Add a new overload of `GenerateHexDump` that accepts `AtrGeometry` parameter
2. When geometry is provided, calculate sector boundaries and show them in the output
3. The sector column replaces or augments the address column
4. Add an optional `--sector-aware` flag to `hex-dump` command

```csharp
internal static string GenerateHexDump(
    ReadOnlySpan<byte> span,
    int fileOffset,
    int count,
    ushort? startAddress = null,
    AtrGeometry? geometry = null)
```

When `geometry` is not null:
- Calculate which sector each row belongs to
- Show sector number in the output
- Annotate sector boundaries (e.g., "ATR header ends, sector data starts")

## Implementation Plan

### Boot Header Recognition

1. Add `BootHeaderInfo` record to `Atari` namespace
2. Update `DisassemblyAnalyzer.Analyze` to detect boot header and store metadata
3. Update `FormatCa65Analyzed` to emit boot header as a structured block
4. Add boot header comment generation with decoded field descriptions
5. Update the `ReferenceGraph` or create a new data structure to hold boot header info

### Hex-Dump Sector Awareness

1. Add `GenerateHexDump` overload with `AtrGeometry` parameter
2. Modify `HexDumpTool.HexDump` to accept optional geometry
3. Add sector awareness flag to hex-dump command in `Program.cs`
4. Calculate sector numbers from file offsets using `AtrParser.SectorFileOffset`

## Testing

1. Unit tests for boot header detection in `DisassemblyAnalyzer`
2. Unit tests for boot header formatting in `DisassemblerTool`
3. Unit tests for `GenerateHexDump` with geometry parameter
4. Integration tests: verify hex-dump shows correct sector boundaries
5. Edge cases: no boot header, partial boot header, non-standard boot flags