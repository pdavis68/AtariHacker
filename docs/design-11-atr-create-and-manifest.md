# ATR Create with Filesystem Initialization and Manifest Support

## Overview

Enhance `atr create` to support filesystem initialization (SpartaDOS and DOS 2.x) and JSON/YAML manifest-based disk creation. This enables building complete disk images from a declarative specification.

## Features

### 1. Filesystem Initialization

**Purpose:** Create a new ATR image with an initialized filesystem, including VTOC, empty directory, and boot sectors.

**Signature:**
```csharp
public static string CreateAtr(
    string outputPath,
    int sectorCount,
    string density,
    string? filesystem = null,
    string? manifest = null)
```

**Usage:**
```bash
atarihacker atr create build/disk.atr 720 sd --filesystem spartados
atarihacker atr create build/disk.atr 720 sd --filesystem dos2
```

### 2. Manifest-Based Creation

**Purpose:** Allow `atr create` to accept a JSON manifest describing the disk layout, including files to inject, boot sector content, and filesystem type.

**Manifest Schema:**

```json
{
  "sectors": 720,
  "density": "sd",
  "filesystem": "spartados",
  "boot": {
    "flag": "$D0",
    "sectors": 3,
    "load_address": "$0700",
    "init_address": "$1540",
    "file": "build/boot-loader.bin"
  },
  "files": [
    { "name": "DOS.SYS", "file": "build/dos.sys" },
    { "name": "AUTORUN.SYS", "file": "build/autorun.sys" },
    { "name": "AGENT.OBJ", "file": "build/agent.obj", "load_address": "$1540" },
    { "name": "AGENT.DAT", "file": "build/agent.dat" }
  ]
}
```

## Implementation

### Filesystem Initialization

**DOS 2.x initialization:**
1. Write ATR header (16 bytes with correct geometry)
2. Write boot sectors (3 sectors, minimal boot loader)
3. Write VTOC sector (sector 360) with empty bitmap
4. Write directory sectors (sectors 361-368) with empty entries

**SpartaDOS initialization:**
1. Write ATR header (16 bytes with correct geometry)
2. Write boot sectors (3 sectors, SpartaDOS boot loader)
3. Write VTOC/bitmap sector (sector 4) with empty bitmap
4. Write directory sector (sector 5+) with empty linked list

### Manifest Parsing

```csharp
public sealed record DiskManifest
{
    public int Sectors { get; init; }
    public string Density { get; init; } = "sd";
    public string? Filesystem { get; init; }
    public BootManifest? Boot { get; init; }
    public List<FileManifest> Files { get; init; } = [];
}

public sealed record BootManifest
{
    public string? Flag { get; init; }
    public int Sectors { get; init; } = 3;
    public string? LoadAddress { get; init; }
    public string? InitAddress { get; init; }
    public string? File { get; init; }
}

public sealed record FileManifest
{
    public string Name { get; init; } = "";
    public string File { get; init; } = "";
    public string? LoadAddress { get; init; }
}
```

### File Injection During Creation

After initializing the filesystem, inject each file from the manifest:
1. For DOS 2.x: Use existing `InjectFile` logic
2. For SpartaDOS: Write file data to free sectors, create directory entry
3. Update VTOC/bitmap to mark allocated sectors

## Testing

1. Unit tests for manifest parsing (valid/invalid JSON)
2. Unit tests for DOS 2.x filesystem initialization
3. Unit tests for SpartaDOS filesystem initialization
4. Integration tests: create disk → verify with `atr info`
5. Integration tests: create disk with files → verify with `atr directory`
6. Integration tests: create disk → extract files → verify content