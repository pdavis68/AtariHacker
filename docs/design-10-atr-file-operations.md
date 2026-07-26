# ATR File Operations (load-file, analyze-layout, disassemble-sector, dump, write-boot, sector-info, diff)

## Overview

Add a suite of new `atr` subcommands that enable file-level operations on ATR disk images, bridging the gap between raw sector access and code analysis. These commands address the current limitation where only boot sectors are loaded into the session.

## Commands

### 1. `atr load-file` — Load a specific file from the ATR into the session

**Purpose:** Load a specific file from an ATR disk image into the current session for disassembly/analysis.

**Signature:**
```csharp
public static string LoadAtrFile(
    RomSession session,
    SymbolTable symbols,
    ZeroPageMap zeroPageMap,
    SessionPersistence persistence,
    string filePath,
    string fileName,
    ushort? loadAddress = null)
```

**Behavior:**
- Parse the ATR filesystem (DOS 2.x or SpartaDOS)
- Find the file by name in the directory
- Extract the file data
- Load the data into the session at the specified address (or raw)
- Return info about the loaded file

### 2. `atr analyze-layout` — Full disk structure analysis

**Purpose:** Perform comprehensive analysis of the disk layout, detecting the filesystem type, identifying boot method, and mapping all sectors.

**Signature:**
```csharp
public static string AnalyzeLayout(string filePath)
```

**Output:**
```
Filesystem: SpartaDOS
Boot type: Custom loader ($D0 flag)
Sectors: 720 × 128 bytes (SD)
VTOC: Sector 4
Directory: Sectors 5-12 (linked list, 8 directory sectors)
Files:
  DOS.SYS      — 20 sectors, starts at sector 13
  AUTORUN.SYS  — 8 sectors, starts at sector 33
  ...
Free sectors: 368-720 (352 sectors)
```

### 3. `atr disassemble-sector` — Disassemble specific sectors

**Purpose:** Disassemble a range of sectors from the disk as code, specifying the load address. Bridges the gap between raw sector access and code analysis.

**Signature:**
```csharp
public static string DisassembleSector(
    RomSession session,
    SymbolTable symbols,
    ZeroPageMap zeroPageMap,
    string filePath,
    int startSector,
    int sectorCount,
    ushort? loadAddress = null,
    string format = "listing",
    bool analyze = false)
```

### 4. `atr dump` — Raw binary dump of ATR data

**Purpose:** Dump the raw binary contents of the ATR (excluding the 16-byte ATR header) to stdout or a file.

**Signature:**
```csharp
public static string DumpAtrData(
    string filePath,
    int? startSector = null,
    int? endSector = null,
    string? outputFile = null)
```

### 5. `atr write-boot` — Write boot sectors to ATR

**Purpose:** Write a 3-sector (384-byte) boot loader to an ATR image, properly handling the 6-byte boot header and sector alignment.

**Signature:**
```csharp
public static string WriteBootSectors(
    string filePath,
    string bootFilePath,
    byte? bootFlag = null,
    ushort? loadAddress = null,
    ushort? initAddress = null)
```

### 6. `atr sector-info` — Detailed sector information

**Purpose:** Show detailed information about one or more sectors, including whether they're part of the boot, VTOC, directory, a specific file, or free.

**Signature:**
```csharp
public static string SectorInfo(
    string filePath,
    string sectorRange)
```

**Output:**
```
Sector 1: Boot (part of boot loader, 3 sectors)
Sector 4: VTOC (SpartaDOS volume bitmap)
Sector 5: Directory (SpartaDOS directory sector 1 of 8)
Sector 13: File data (DOS.SYS, sector 1 of 20)
```

### 7. `atr diff` (filesystem-aware) — Compare ATR images

**Purpose:** Enhance `atr diff` to compare ATR images filesystem-aware, showing differences at the file level rather than just raw byte-level.

**Signature:**
```csharp
public static string DiffAtrImages(
    string filePath1,
    string filePath2)
```

**Output:**
```
Comparing ATR images:
  ATR headers: match
  Boot sectors: match
  Filesystem: SpartaDOS vs SpartaDOS

  File differences:
    AGENT.OBJ: 12 bytes differ at offsets $45, $67, ...
    AGENT.DAT: identical
```

## Implementation Plan

1. Add `LoadAtrFile` with `loadAddress` parameter to `AtrTools`
2. Add `AnalyzeLayout` to `AtrTools`
3. Add `DisassembleSector` to `AtrTools`
4. Add `DumpAtrData` to `AtrTools`
5. Add `WriteBootSectors` to `AtrTools`
6. Add `SectorInfo` to `AtrTools`
7. Add `DiffAtrImages` to `AtrTools`
8. Register all new commands in `Program.cs`

## Testing

- Unit tests for each new command
- Integration tests with known ATR images
- Test error handling for invalid ATR files
- Test edge cases (empty disks, corrupted directories)