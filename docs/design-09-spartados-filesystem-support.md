# SpartaDOS Filesystem Support

## Overview

Add support for SpartaDOS filesystem detection and parsing to the `atr` command family. SpartaDOS is a well-known standard filesystem used on Atari 8-bit computers with hard drives and floppy disks. Currently, the tool only supports DOS 2.x filesystems and rejects all others with a misleading error message.

## Background

SpartaDOS uses a fundamentally different disk layout than DOS 2.x:

- **VTOC**: Bitmap-based sector allocation (not the simple bitmap of DOS 2.x)
- **Directory**: Linked list of sectors, each holding multiple 16-byte directory entries
- **Boot method**: Standard Atari boot header (compatible with DOS 2.x boot process)

## Design

### Filesystem Detection

A new method `AtrParser.HasSpartaDosFilesystem()` will detect SpartaDOS format. SpartaDOS can be identified by:

1. Checking boot sector flag/value patterns
2. Checking for SpartaDOS-specific sector allocation bitmap markers
3. Looking for the SpartaDOS volume label convention

```csharp
public static bool HasSpartaDosFilesystem(byte[] data)
{
    // SpartaDOS uses a bitmap-based VTOC typically at sector 4
    // The bitmap has specific header bytes that distinguish it from DOS 2.x
}
```

### Directory Parsing

SpartaDOS directory format (16-byte entries in linked-list sectors):

| Offset | Size | Field       | Description                              |
|--------|------|-------------|------------------------------------------|
| 0      | 1    | Flag        | Entry flags (deleted, locked, etc.)      |
| 1      | 2    | Time        | Time of last modification                |
| 3      | 1    | Date        | Date of last modification                |
| 4      | 1    | Name length | Length of filename                       |
| 5      | 8+3  | Filename    | Short filename (8.3 format)              |
| 13     | 2    | Start sector| First sector of the file                 |
| 15     | 1    | (reserved)  |                                          |

### Sector Chain Following

SpartaDOS uses the same sector chain format as DOS 2.x (last 3 bytes of each sector: next sector link and byte count), so the existing `ExtractFile` and `GetSectorChain` methods can be reused.

### Affected Commands

All commands that currently check `HasDosFilesystem()` need to be updated:

- `atr directory` (`ListAtrDirectory`)
- `atr extract` / `atr extract-all` (`LoadAtrFile`, `ExtractAll`)
- `atr inject` / `atr inject-all` (`InjectAll`)
- `atr vtoc` (`ShowVtoc`)
- `atr sector-map` (`SectorMap`)
- `atr file-frag` (`FileFragmentation`)
- `atr info` (`AtrInfo`)

### Implementation Plan

1. Add `HasSpartaDosFilesystem()` to `AtrParser`
2. Add `ReadSpartaDirectory()` to `AtrParser`
3. Update `ListAtrDirectory` to try SpartaDOS if DOS 2.x fails
4. Update `BuildSectorInfo` in `AtrForensicTools` to handle SpartaDOS VTOC
5. Update `ShowVtoc` to handle SpartaDOS bitmap
6. Update `AtrInfo` to show SpartaDOS filesystem info
7. Update `ExtractAll` and `InjectAll` to support SpartaDOS

### New Types

```csharp
public sealed record SpartaDirectorySector(
    int SectorNumber,
    List<SpartaDirEntry> Entries,
    int NextSector
);

public sealed record SpartaDirEntry(
    byte Flags,
    ushort Time,
    byte Date,
    byte NameLength,
    string FileName,
    int StartSector,
    bool IsDeleted
);
```

## Testing

1. Unit tests for SpartaDOS detection on known SpartaDOS disk images
2. Unit tests for SpartaDOS directory parsing
3. Integration tests for `atr directory` command on SpartaDOS disks
4. Tests for VTOC and sector map on SpartaDOS disks