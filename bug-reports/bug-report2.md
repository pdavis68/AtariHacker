# AtariHacker Tool Review

## Version
`0.96+a76bb55113be45f8158548b027763c128592abc4`

## Testing Methodology

The tool was tested against the disk image `atr/Agent_USA_1984_Scholastic_Wizware_US.atr` — a Single Density (720 × 128-byte sectors) ATR with a SpartaDOS filesystem and a custom boot loader.

---

## Bugs Found

### Bug #1: `hex-dump` crashes with `--start-address $0000`

**Command:**
```bash
atarihacker -t atr/Agent_USA_1984_Scholastic_Wizware_US.atr hex-dump 0 4096 --start-address $0000
```

**Result:**
```
ERROR: Invalid address: '/bin/sh000'.
```

**Analysis:** The `$0000` address is being interpreted by the shell as a variable expansion, and the shell attempts to expand `$0000` which results in an empty string or `/bin/sh`-prefixed result. However, the user did use single quotes around hex values with `$` as recommended in the documentation. The error message shows a path `/bin/sh` which suggests the shell is involved in the parsing in an unexpected way.

**Root cause:** The error message suggests the argument parser is not handling the zero-address case correctly. When `$0000` is not quoted (or even when it is), the parser may be passing the address through the shell in a way that causes it to be interpreted as a shell variable.

**Workaround:** Use `0x0000` prefix instead of `$0000`.

**Severity:** Medium — prevents hex dumping from address zero with the `$` prefix.

---

### Bug #2: `atr directory` fails for SpartaDOS filesystems

**Command:**
```bash
atarihacker atr directory atr/Agent_USA_1984_Scholastic_Wizware_US.atr
```

**Result:**
```
ERROR: No DOS 2.x filesystem detected on this disk image. This disk may use a custom/non-DOS layout. Use load_rom to load it as a raw binary, or load_atr_boot to inspect the boot loader.
```

**Analysis:** The disk is SpartaDOS-formatted, which uses a completely different directory structure than DOS 2.x. The tool only supports DOS 2.x filesystem detection and rejects all others. The error message is misleading — it says "custom/non-DOS layout" when in fact it's a known standard (SpartaDOS).

**Impact:** The `atr directory`, `atr extract`, `atr extract-all`, `atr inject`, `atr inject-all`, `atr vtoc`, `atr sector-map`, `atr file-frag`, and `atr recover` commands are all unusable for SpartaDOS disks.

**Severity:** High — Major functionality gap for a significant class of Atari disks.

---

### Bug #3: `sector-map` only shows 3 sectors used

**Command:**
```bash
atarihacker atr sector-map atr/Agent_USA_1984_Scholastic_Wizware_US.atr
```

**Result:**
```
Sector map for Agent_USA_1984_Scholastic_Wizware_US.atr (720 sectors, SD):
  Sectors 001-003: [Boot    ] Boot loader
  Sectors 004-720: [Free    ] Free

Usage: 3/720 sectors (0.4% used)
Fragmentation: 1 free regions, largest: 717 sectors
```

**Analysis:** The sector map reports only 3 sectors used when the disk clearly has data in sectors 4–367. This is because the tool only knows DOS 2.x VTOC format and can't parse the SpartaDOS VTOC.

**Severity:** High — Misleading sector utilization data.

---

### Bug #4: Boot header bytes mislabeled in disassembly

**Command:**
```bash
atarihacker -t atr/Agent_USA_1984_Scholastic_Wizware_US.atr disassemble 0 384 --format ca65 --analyze
```

**Result (excerpt):**
```ca65
data_0700:
	.byte	$D0
data_0702:
	.byte	$00
data_0703:
	.byte	$07
data_0704:
	.byte	"@"
.proc data_0705
	.byte	$15, $4C	; CBAUD
	.byte	$14
	.byte	$07
```

**Analysis:** The boot header (6 bytes at `$0700–$0705`: `$D0 $03 $00 $07 $40 $15`) is being disassembled as individual data bytes with auto-generated labels. However:

1. The label `data_0702` is wrong — it points to `$0702` but the actual byte at `$0702` is `$00` which is the second byte of the load address `$0700` (little-endian). The label should be at `$0701` for the `$03` (sector count) byte, or the header should be a single 6-byte block.

2. `data_0704` shows `"@"` which is the ASCII representation of `$40`, but `$40` here is the first byte of the init address `$1540` (little-endian). The comment `; CBAUD` on `$4C` is incorrect — `$4C` is the opcode for `JMP`, not a hardware register reference.

3. The boot header (bytes `$0700–$0705`) should be treated as a single structural unit: `$D0` (boot flag), `$03` (sector count), `$00 $07` (load address), `$40 $15` (init address). The analyzer should recognize this pattern.

**Severity:** Medium — Misleading labels and comments in a critical structural region.

---

### Bug #5: `info` command shows only boot sectors, not full disk

**Command:**
```bash
atarihacker -t atr/Agent_USA_1984_Scholastic_Wizware_US.atr info
```

**Result:**
```
File path : .../Agent_USA_1984_Scholastic_Wizware_US.atr/BOOT
File size : 384 bytes ($0180)
Format    : Raw binary (base address set)
Base address: $0700
```

**Analysis:** The `info` command only shows the boot sectors (3 × 128 = 384 bytes) from the ATR, not the full disk contents. The path shows `.../BOOT` appended to the ATR path, indicating the tool is extracting only the boot sectors. For a full disk reverse engineering, the user needs to see the complete disk contents.

**Severity:** Medium — The tool hides the full disk contents from the user.

---

## Feature Gaps & Recommendations

### Recommendation #1: SpartaDOS Filesystem Support (Critical)

**Description:** Add a SpartaDOS filesystem parser to `atr` commands. This is the single most important missing feature.

**Requirements:**
- Detect SpartaDOS filesystem format (distinct from DOS 2.x)
- Parse SpartaDOS VTOC (bitmap-based sector allocation)
- Parse SpartaDOS directory entries (linked-list of sectors, 16-byte entries)
- Support `atr directory` for SpartaDOS
- Support `atr extract` / `atr extract-all` for SpartaDOS
- Support `atr inject` / `atr inject-all` for SpartaDOS
- Support `atr vtoc` for SpartaDOS
- Support `atr sector-map` for SpartaDOS
- Support `atr file-frag` for SpartaDOS

**SpartaDOS directory format reference:**
- Directory is a linked list of sectors
- Each sector holds multiple 16-byte directory entries
- Entry format: flag (1), time (2), date (1), name_len (1), filename (8+3), start_sector (2)

---

### Recommendation #2: `atr load-file` — Load a file from the ATR into the session

**Description:** A command that loads a specific file from an ATR disk image into the current session for disassembly/analysis, specifying the memory address to load it at.

**Usage:**
```bash
atarihacker atr load-file disk.atr AGENT.OBJ --address $1540
```

**Why needed:** Currently, only the boot sectors are loaded into the session when targeting an ATR. There's no way to load a specific file from the filesystem into the session for analysis. This severely limits the ability to analyze game code stored in files on the disk.

---

### Recommendation #3: `atr analyze-layout` — Full disk structure analysis

**Description:** A command that performs comprehensive analysis of the disk layout, detecting the filesystem type, identifying boot method, and mapping all sectors.

**Usage:**
```bash
atarihacker atr analyze-layout disk.atr
```

**Output:**
```
Filesystem: SpartaDOS
Boot type: Custom loader ($D0 flag)
Sectors: 720 × 128 bytes (SD)
VTOC: Sector 4 (or wherever SpartaDOS puts it)
Directory: Sectors 5-12 (linked list, 8 directory sectors)
Files:
  DOS.SYS      — 20 sectors, starts at sector 13
  AUTORUN.SYS  — 8 sectors, starts at sector 33
  AGENT.OBJ    — 120 sectors, starts at sector 41
  AGENT.DAT    — 80 sectors, starts at sector 161
Free sectors: 368-720 (352 sectors)
```

---

### Recommendation #4: `atr disassemble-sector` — Disassemble specific sectors

**Description:** Disassemble a range of sectors from the disk as code, specifying the load address. This bridges the gap between raw sector access and code analysis.

**Usage:**
```bash
atarihacker atr disassemble-sector disk.atr 4 60 --address $1500 --format ca65 --analyze
```

**Why needed:** Currently, to disassemble game code from an ATR, you must first extract the file, then load it separately. A direct sector-to-disassembly pipeline would streamline the workflow.

---

### Recommendation #5: `atr dump` — Raw binary dump of ATR data

**Description:** Dump the raw binary contents of the ATR (excluding the 16-byte ATR header) to stdout or a file. This complements `hex-dump` by providing raw binary output suitable for piping to other tools.

**Usage:**
```bash
atarihacker atr dump disk.atr > disk.raw
atarihacker atr dump disk.atr --sectors 4-60 > game-code.bin
```

**Why needed:** Currently there's no way to get the raw binary data from an ATR for use with other tools or for loading into the session at a specific address.

---

### Recommendation #6: `atr create` with filesystem initialization

**Description:** Enhance `atr create` to optionally initialize the disk with a SpartaDOS or DOS 2.x filesystem, including VTOC, empty directory, and boot sectors.

**Usage:**
```bash
atarihacker atr create build/disk.atr 720 sd --filesystem spartados
atarihacker atr create build/disk.atr 720 sd --filesystem dos2
```

---

### Recommendation #7: Boot Header Structural Recognition

**Description:** The analyzer should recognize the 6-byte boot header pattern (`flag` `sectors` `load_addr_lo` `load_addr_hi` `init_addr_lo` `init_addr_hi`) and treat it as a single structural unit in disassembly output, rather than individual bytes.

**Usage (implicit):**
```ca65
; Boot header
        .byte   $D0             ; Boot flag: $D0 = stop/run
        .byte   $03             ; Sectors to load: 3
        .word   $0700           ; Load address: $0700
        .word   $1540           ; Init address: $1540
```

---

### Recommendation #8: `atr write-boot` — Write boot sectors to ATR

**Description:** Write a 3-sector (384-byte) boot loader to an ATR image, properly handling the 6-byte boot header and sector alignment.

**Usage:**
```bash
atarihacker atr write-boot build/disk.atr build/boot-loader.bin
```

**Why needed:** Currently only `atr write-sector` exists, which writes individual sectors. Boot sector writing should be a first-class operation that validates the boot header and ensures proper alignment.

---

### Recommendation #9: `hex-dump` with ATR sector awareness

**Description:** When dumping from an ATR, `hex-dump` should show sector boundaries in the output, making it easier to understand the disk layout.

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

---

### Recommendation #10: `atr sector-info` — Detailed sector information

**Description:** Show detailed information about one or more sectors, including whether they're part of the boot, VTOC, directory, a specific file, or free.

**Usage:**
```bash
atarihacker atr sector-info disk.atr 1-10
```

**Output:**
```
Sector 1: Boot (part of boot loader, 3 sectors)
Sector 4: VTOC (SpartaDOS volume bitmap)
Sector 5: Directory (SpartaDOS directory sector 1 of 8)
Sector 13: File data (DOS.SYS, sector 1 of 20)
```

---

### Recommendation #11: Structured Output for `atr create`

**Description:** Allow `atr create` to accept a JSON/YAML manifest describing the disk layout, including files to inject, boot sector content, and filesystem type.

**Usage:**
```bash
atarihacker atr create build/disk.atr --manifest disk-manifest.json
```

**`disk-manifest.json`:**
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

---

### Recommendation #12: Improved `diff` for ATR images

**Description:** Enhance `atr diff` to compare ATR images filesystem-aware, showing differences at the file level rather than just raw byte-level.

**Usage:**
```bash
atarihacker atr diff original.atr rebuilt.atr
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
    AUTORUN.SYS: identical
    DOS.SYS: identical
```

---

## General Observations

### Strengths
1. **Comprehensive command set** — The tool has a well-thought-out set of commands covering most reverse engineering needs.
2. **ca65 output format** — Direct compatibility with the cc65 toolchain is excellent.
3. **Hardware symbol table** — The built-in Atari hardware register table is comprehensive and accurate.
4. **Multi-pass analysis** — The code/data separation and label generation are useful starting points.
5. **Analyze mode** — The `--analyze` flag on disassembly produces much better output than raw disassembly.
6. **Sidecar files** — The `.atarihacker.json` persistence mechanism is well-designed.

### Weaknesses
1. **SpartaDOS gap** — The biggest functional gap is the lack of SpartaDOS filesystem support, which affects a significant portion of Atari 8-bit commercial software.
2. **ATR session handling** — The tool treats ATR files as "boot sector + raw binary" rather than understanding the full disk structure.
3. **Error messages** — Error messages are sometimes misleading (e.g., "No DOS 2.x filesystem detected" when the disk has a SpartaDOS filesystem).
4. **Documentation vs. reality** — The `atr sector-map` and `atr directory` commands are documented to work with DOS-formatted disks, but they silently fail for SpartaDOS without suggesting the user try `atr filesystem` to define a custom layout.

### Summary

The tool is a solid foundation for Atari 8-bit reverse engineering. The most critical improvement needed is **SpartaDOS filesystem support**, which would unlock the tool for a large class of commercial Atari games. The SpartaDOS gap affects the entire `atr` command family and makes the tool significantly less useful for the Agent USA reverse engineering project.

The `atr filesystem` command is a promising start for custom filesystems but needs to be complemented with SpartaDOS-specific support since SpartaDOS is a well-known standard, not a custom format.