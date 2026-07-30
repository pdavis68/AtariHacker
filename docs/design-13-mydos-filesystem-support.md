# MyDOS Filesystem Support

## Overview

Add support for MyDOS extended disk detection and parsing to the `atr` command family. MyDOS is an enhanced, backward-compatible evolution of Atari DOS 2.0 created by Wordmark, supporting up to 65,535 sectors while maintaining the same fundamental 3-byte sector link metadata structure.

**Current state:** The app supports DOS 2.x (standard 720/1040 sector disks) and SpartaDOS, but has **zero MyDOS support**. A search for "MyDOS" in the codebase returns no results.

---

## Background: MyDOS vs. DOS 2.0

MyDOS extended DOS 2.0 in three key areas:

### 1. 16-Bit Sector Links (Bytes 125-126)

In standard DOS 2.0, the last 3 bytes of every 128-byte data sector contain:

| Byte | DOS 2.0 Usage | MyDOS Usage |
|------|--------------|-------------|
| 125  | 6 bits File ID + **2 bits** Next Sector (upper) | **8 bits** Next Sector (upper) |
| 126  | 8 bits Next Sector (lower) | 8 bits Next Sector (lower) |
| 127  | Byte count for this sector | Byte count for this sector |

**DOS 2.0** extracts the next sector pointer as:
```
nextSector = (rawSector[125] & 0x03) << 8 | rawSector[126]
```
This yields a **10-bit pointer** (max sector 1023).

**MyDOS** extracts the next sector pointer as:
```
nextSector = rawSector[125] << 8 | rawSector[126]
```
This yields a **full 16-bit pointer** (max sector 65535).

For sector numbers < 1024, both interpretations produce identical results (the upper 6 bits of byte 125 are zero), so the existing code works for small disks. For disks with sector numbers >= 1024, the current `& 0x03` mask in `ExtractFile()` and `GetSectorChain()` truncates the pointer.

### 2. Multi-Sector VTOC (Chainable Bitmap)

DOS 2.0 uses a single VTOC at **sector 360** with a 90-byte bitmap (tracking 720 sectors). DOS 2.5 added a second hard-coded VTOC at sector 1024.

MyDOS uses a **chainable multi-sector VTOC** starting at sector 360:

| Offset | Size | Field | Description |
|--------|------|-------|-------------|
| 0x00   | 1    | DOS Code | DOS version marker |
| 0x01-0x02 | 2 | Total Sectors | 16-bit LE total sector count |
| 0x03-0x04 | 2 | Free Sectors | 16-bit LE free sector count |
| **0x05** | **1** | **VTOC Flag** | **$02 = extended MyDOS VTOC** |
| **0x06-0x07** | **2** | **Next VTOC Sector** | **16-bit LE pointer to next VTOC sector (0 = last)** |
| 0x08-0x09 | 2 | (reserved) | |
| 0x0A-0x7F | 118 | Bitmap | Free sector bitmap (118 bytes × 8 = 944 sectors) |

**Secondary VTOC sectors** have a simpler layout:

| Offset | Size | Field | Description |
|--------|------|-------|-------------|
| 0x00-0x01 | 2 | Next VTOC Sector | 16-bit LE pointer (0 = last) |
| 0x02-0x7F | 126 | Bitmap | Free sector bitmap |

### 3. Subdirectory Support

MyDOS supports subdirectories using the same 16-byte directory entry format as DOS 2.0, but with a special flag. When a directory entry's status byte has the **subdirectory bit** set, the referenced 8-sector block acts as a nested directory rather than a file.

---

## Detection Strategy

### `HasMyDosFilesystem()`

MyDOS detection should be attempted **after** standard DOS 2.x detection fails or when the disk has more than 1024 sectors (which is beyond DOS 2.x's 10-bit addressing limit).

Detection heuristics:

1. **Sector count check**: If `geometry.SectorCount > 1024`, the disk is too large for standard DOS 2.x and likely MyDOS or SpartaDOS. If SpartaDOS detection fails, try MyDOS.

2. **VTOC byte 5 check**: Read sector 360. If byte 5 == `$02`, this is a MyDOS extended VTOC.

3. **VTOC chain validation**: If byte 5 == `$02`, validate that bytes 6-7 form a valid next-VTOC sector pointer (either 0 or a valid sector number within range).

4. **Bitmap consistency**: Verify that the bitmap data in the VTOC chain has reasonable entropy (not all zeros or all ones).

5. **Directory format compatibility**: Since MyDOS uses the same directory format as DOS 2.0 (sectors 361-368, 16-byte entries), the existing `ReadDirectory()` method can be reused. However, we should verify that directory entries don't reference sectors beyond the disk's capacity.

```csharp
public static bool HasMyDosFilesystem(byte[] data)
{
    var geometry = ParseGeometry(data);
    if (geometry.SectorCount < 360) return false;

    // Read VTOC at sector 360
    var vtoc = ReadSector(data, geometry, 360);
    if (vtoc.Length < 8) return false;

    // Check for MyDOS extended VTOC marker
    if (vtoc[5] != 0x02) return false;

    // Validate next-VTOC sector pointer (if non-zero)
    var nextVtoc = vtoc[6] | (vtoc[7] << 8);
    if (nextVtoc != 0 && (nextVtoc < 1 || nextVtoc > geometry.SectorCount))
        return false;

    // Validate total sectors field
    var totalSectors = vtoc[1] | (vtoc[2] << 8);
    if (totalSectors < 360 || totalSectors > geometry.SectorCount)
        return false;

    return true;
}
```

---

## Implementation Plan

### Phase 1: Core Detection and Parsing (`AtrParser.cs`)

#### 1.1 Add `HasMyDosFilesystem()` method

New method that detects MyDOS extended format by checking:
- VTOC sector 360 exists and is readable
- Byte 5 == `$02` (MyDOS extended VTOC flag)
- VTOC chain pointer is valid
- Total sectors field is reasonable

#### 1.2 Add `HasMyDosDirectory()` / `ReadMyDosDirectory()` method

MyDOS uses the same directory format as DOS 2.0 (sectors 361-368, 16-byte entries), so the existing `ReadDirectory()` method can be reused directly. However, we need to add:

- Recognition of the **subdirectory flag** in directory entries
- A new record type or extended entry type to convey subdirectory status

```csharp
// Extended entry for MyDOS with subdirectory support
public sealed record MyDosDirectoryEntry(
    int Index,
    string FileName,
    string Extension,
    int StartSector,
    int SectorCount,
    bool IsDeleted,
    bool IsLocked,
    bool IsBinary,
    bool IsSubdirectory  // NEW: MyDOS subdirectory flag
);
```

#### 1.3 Add `GetMyDosVtocChain()` method

Returns the list of VTOC sector numbers in the MyDOS VTOC chain, starting at sector 360 and following the next-VTOC pointers.

```csharp
public static List<int> GetMyDosVtocChain(byte[] data, AtrGeometry geometry)
```

#### 1.4 Add `GetMyDosBitmap()` method

Builds a complete free-sector bitmap by reading all VTOC sectors in the chain and concatenating their bitmap portions.

```csharp
public static bool[] GetMyDosBitmap(byte[] data, AtrGeometry geometry)
```

#### 1.5 Add `GetMyDosFreeSectorCount()` method

Returns the free sector count from the VTOC header (bytes 3-4 of sector 360), with a fallback to counting bits in the bitmap.

#### 1.6 Modify `ExtractFile()` and `GetSectorChain()` for 16-bit links

**Critical change:** The existing `ExtractFile()` and `GetSectorChain()` methods use `rawSector[^3] & 0x03` to extract the upper bits of the next sector pointer. This masks out the upper 6 bits that MyDOS uses.

The current code:
```csharp
var nextHi = rawSector[^3] & 0x03;  // DOS 2.0: only 2 bits
var nextLo = rawSector[^2];
sector = (nextHi << 8) | nextLo;
```

For MyDOS, this should be:
```csharp
var nextHi = rawSector[^3];           // MyDOS: full 8 bits
var nextLo = rawSector[^2];
sector = (nextHi << 8) | nextLo;
```

**Strategy:** Since the existing methods are used by both DOS 2.x and SpartaDOS, we need to be careful. For DOS 2.x disks, the upper 6 bits of byte 125 are always 0 (since they're used for the File ID), so the `& 0x03` mask is redundant but harmless to remove. For SpartaDOS, the sector chain format is the same as DOS 2.0 (10-bit pointers), so the same applies.

**Recommendation:** Remove the `& 0x03` mask entirely from `ExtractFile()` and `GetSectorChain()`. This will:
- Preserve full backward compatibility with DOS 2.0 (upper bits are 0)
- Preserve full backward compatibility with SpartaDOS (upper bits are 0)
- Enable MyDOS with sector numbers >= 1024

**Validation:** Add a test that verifies a DOS 2.0 disk with sector chain uses the same values with and without the mask.

#### 1.7 Add `ReadMyDosSubdirectory()` method

For reading subdirectory contents (an 8-sector block treated as a nested directory with 64 16-byte entries).

```csharp
public static List<MyDosDirectoryEntry> ReadMyDosSubdirectory(
    byte[] data, AtrGeometry geometry, int startSector)
```

### Phase 2: Tool Integration (`AtrTools.cs`)

The following commands need to be updated to handle MyDOS:

#### 2.1 `AtrInfo()` — Show disk info

Currently falls through to "No DOS 2.x or SpartaDOS filesystem detected" if neither DOS 2.x nor SpartaDOS is detected. Add a MyDOS detection path:

```csharp
if (AtrParser.HasDosFilesystem(bytes))
    return FormatAtrInfoDos(bytes, geometry, resolvedPath, lines);
if (AtrParser.HasSpartaDosFilesystem(bytes))
    return FormatAtrInfoSparta(bytes, geometry, resolvedPath);
if (AtrParser.HasMyDosFilesystem(bytes))
    return FormatAtrInfoMyDos(bytes, geometry, resolvedPath, lines);
```

#### 2.2 `ListAtrDirectory()` — List directory

Add MyDOS detection before the "No DOS 2.x or SpartaDOS" error:

```csharp
if (AtrParser.HasMyDosFilesystem(bytes))
    return FormatListDirectoryMyDos(bytes, filePath);
```

#### 2.3 `LoadAtrFile()` — Extract file into session

Add MyDOS fallback after DOS 2.x and SpartaDOS:

```csharp
if (dosMatch is null && AtrParser.HasMyDosFilesystem(bytes))
{
    var directory = AtrParser.ReadDirectory(bytes);  // Same format as DOS 2.0
    dosMatch = MatchEntry(directory, fileName);
}
```

#### 2.4 `AnalyzeLayout()` — Layout analysis

Add MyDOS branch after SpartaDOS:

```csharp
if (hasMyDos)
{
    // Show VTOC chain info
    var vtocChain = AtrParser.GetMyDosVtocChain(bytes, geometry);
    lines.Add("VTOC: Sector 360 (MyDOS extended, chain: ...)");
    // Show directory (same as DOS 2.0)
    // Show files with full 16-bit sector chain
    // Show free sectors from MyDOS bitmap
}
```

#### 2.5 `ShowVtoc()` — VTOC display

Add a MyDOS-specific VTOC display showing:
- Primary VTOC (sector 360) header fields
- Total and free sector counts
- VTOC flag ($02) and chain pointer
- All secondary VTOC sectors in the chain
- Full bitmap (all VTOC sectors concatenated)

#### 2.6 `SectorMap()` and `FileFragmentation()` — Forensic tools

Update `BuildSectorInfo()` in `AtrForensicTools.cs` to recognize MyDOS:
- Sector 360 = VTOC (primary)
- Secondary VTOC sectors = VTOC type
- Sectors 361-368 = Directory (same as DOS 2.0)

#### 2.7 File extraction, injection, and batch operations

- `ExtractAtrFile()` — Already uses `ExtractFile()` which will work after the 16-bit link fix
- `InjectAtrFile()` — Need to ensure 16-bit links are written correctly
- `ExtractAll()` / `InjectAll()` — Same considerations

### Phase 3: Write Support (`AtrWriteTools.cs`)

#### 3.1 `InjectAtrFile()` — Write path for 16-bit links

The inject code writes sector data directly. The sector link format needs to use full 16-bit pointers when writing to a MyDOS disk.

Current code in `InjectAtrFile()` (and related methods):
```csharp
// Write chain link (last 3 bytes)
modifiedSector[^3] = (byte)(nextSector >> 8);  // Upper byte
modifiedSector[^2] = (byte)nextSector;          // Lower byte
```

This already writes the full upper byte (no `& 0x03` mask), so it's compatible with MyDOS.

#### 3.2 VTOC chain management

For writing operations that need to allocate new sectors, the MyDOS VTOC chain must be updated:
- Mark sectors as used in the appropriate VTOC bitmap sector
- Update free sector count in the primary VTOC header
- Allocate new VTOC sectors if the bitmap runs out of space

### Phase 4: Subdirectory Support

#### 4.1 Subdirectory navigation

Add a command to list contents of a MyDOS subdirectory:
```
atr subdir <disk> <path>
```

This would:
1. Read the parent directory
2. Find the subdirectory entry
3. Read the 8-sector subdirectory block
4. Display the nested directory entries

#### 4.2 Recursive file extraction

Support `atr extract-all` with MyDOS subdirectories, recursively extracting files from nested directories.

---

## Affected Files

| File | Changes |
|------|---------|
| `AtariHacker/Atari/AtrParser.cs` | Add `HasMyDosFilesystem()`, `GetMyDosVtocChain()`, `GetMyDosBitmap()`, `GetMyDosFreeSectorCount()`, `ReadMyDosSubdirectory()`; modify `ExtractFile()` and `GetSectorChain()` to remove `& 0x03` mask; add `MyDosDirectoryEntry` record |
| `AtariHacker/Tools/AtrTools.cs` | Add `AtrInfo` MyDOS branch, `ListAtrDirectory` MyDOS branch, `LoadAtrFile` MyDOS fallback, `AnalyzeLayout` MyDOS branch, `ShowVtoc` MyDOS branch |
| `AtariHacker/Tools/AtrForensicTools.cs` | Update `BuildSectorInfo()` to recognize MyDOS VTOC chain and directory sectors |
| `AtariHacker/Tools/AtrWriteTools.cs` | Verify inject path works with 16-bit links; add VTOC chain update logic |
| `AtariHacker.Test/AtrParserTests.cs` | Add MyDOS detection, VTOC chain, and bitmap tests |
| `AtariHacker.Test/AtrToolsTests.cs` | Add MyDOS directory listing, info, and extract tests |

---

## New Types

```csharp
public sealed record MyDosDirectoryEntry(
    int Index,
    string FileName,
    string Extension,
    int StartSector,
    int SectorCount,
    bool IsDeleted,
    bool IsLocked,
    bool IsBinary,
    bool IsSubdirectory     // NEW: MyDOS subdirectory flag
);
```

---

## Affected Commands

All commands that currently check `HasDosFilesystem()` or `HasSpartaDosFilesystem()` need to be updated to also try MyDOS:

| Command | Method | Priority |
|---------|--------|----------|
| `atr info` | `AtrInfo()` | High |
| `atr directory` | `ListAtrDirectory()` | High |
| `atr extract` | `LoadAtrFile()` | High |
| `atr extract-all` | `ExtractAll()` | Medium |
| `atr inject` | `InjectAtrFile()` | Medium |
| `atr inject-all` | `InjectAll()` | Medium |
| `atr vtoc` | `ShowVtoc()` | High |
| `atr sector-map` | `SectorMap()` | High |
| `atr file-frag` | `FileFragmentation()` | High |
| `atr layout` | `AnalyzeLayout()` | High |
| `atr diff` | `CompareDisks()` | Low |
| `atr create` | `CreateAtr()` | Low |

---

## Detection Order

The detection order should be:

1. **DOS 2.x** — Check first (most common, most restrictive format)
2. **SpartaDOS** — Check second (distinct bitmap-based format)
3. **MyDOS** — Check third (backward-compatible extension of DOS 2.0)
4. **Custom/Non-DOS** — Fallthrough

For MyDOS specifically, detection should be attempted when:
- `HasDosFilesystem()` returns false (but the disk has a DOS-compatible layout)
- OR `geometry.SectorCount > 1024` (beyond DOS 2.x addressing range)
- AND `HasSpartaDosFilesystem()` returns false

---

## 16-Bit Link Migration Strategy

The `& 0x03` mask in `ExtractFile()` and `GetSectorChain()` is currently used in these locations:

| Location | Method | Line |
|----------|--------|------|
| `AtrParser.cs` | `ExtractFile()` | 215 |
| `AtrParser.cs` | `GetSectorChain()` | 346 |
| `AtrParser.cs` | `ReadSpartaDirectory()` | 442 |

**Migration:**

1. Remove `& 0x03` from `ExtractFile()` and `GetSectorChain()` — these are the primary sector chain followers
2. Keep `& 0x03` in `ReadSpartaDirectory()` — SpartaDOS directory sectors use the same chain format as DOS 2.0 (10-bit), but this is also safe to remove since the upper bits would be 0 for SpartaDOS

**Safety analysis:** The mask is redundant for all existing formats:
- **DOS 2.0/2.5:** Upper 6 bits of byte 125 are the File ID (always 0 for valid chains), so `rawSector[^3]` without mask gives the same value as `rawSector[^3] & 0x03`
- **SpartaDOS:** Same chain format, upper bits are 0
- **MyDOS:** Upper bits are meaningful (part of 16-bit pointer), so the mask would truncate

**Removing the mask breaks nothing and enables MyDOS.**

---

## Testing

### Unit Tests

1. **MyDOS detection**: Test `HasMyDosFilesystem()` on known MyDOS disk images, DOS 2.0 disks (should return false), SpartaDOS disks (should return false), and edge cases (empty disk, corrupted VTOC, etc.)

2. **VTOC chain**: Test `GetMyDosVtocChain()` on disks with single VTOC, multiple VTOC sectors, and truncated chains

3. **MyDOS bitmap**: Test `GetMyDosBitmap()` against known free sector counts

4. **16-bit sector links**: Test `GetSectorChain()` and `ExtractFile()` with sector numbers >= 1024 to verify the full 16-bit pointer is used

5. **Subdirectory parsing**: Test `ReadMyDosSubdirectory()` on a MyDOS disk with subdirectories

6. **Regression**: Test that DOS 2.0 and SpartaDOS disks still parse correctly after removing the `& 0x03` mask

### Integration Tests

1. `atr info` on a MyDOS disk shows correct filesystem type
2. `atr directory` on a MyDOS disk lists all files
3. `atr extract` on a MyDOS disk extracts files correctly
4. `atr vtoc` on a MyDOS disk shows the VTOC chain
5. `atr sector-map` correctly identifies MyDOS VTOC and directory sectors

### Test Images Needed

- MyDOS-formatted disk image (720 sectors, SD) — basic MyDOS with single VTOC
- MyDOS-formatted disk image (> 1024 sectors, DD) — MyDOS with multi-sector VTOC and 16-bit sector links
- MyDOS disk with subdirectories
- DOS 2.0 disk (for regression testing)
- SpartaDOS disk (for regression testing)

---

## Open Questions

1. **Test images**: Do we have access to MyDOS disk images for testing? If not, we may need to create synthetic MyDOS images or obtain them from Atari preservation archives.

2. **Subdirectory depth**: Should we support nested subdirectories (subdirectories within subdirectories), or just top-level subdirectories?

3. **Write support**: Should VTOC chain updates be supported for `atr inject` and `atr create`, or is read-only support sufficient for v1?

4. **Sector link ambiguity**: For disks with mixed DOS 2.0 and MyDOS usage (e.g., a DOS 2.0 disk that was extended), the `& 0x03` mask removal is safe but we should verify this with real-world images.