# atarihacker Bug Report

## Bug 1: Script format does not parse `$` prefix as hex for `offset`

**Severity:** High (produces wrong disassembly)

**Description:**
When using the batch script format (`disassemble_all.txt`), the `offset` parameter does not recognize the `$` prefix as hexadecimal. The value `$0700` is parsed as decimal 700 instead of hex `0x0700`.

**Steps to reproduce:**
1. Create a script file with: `disassemble offset=$0700 numBytes=384 format=ca65`
2. Run: `atarihacker script disassemble_all.txt`
3. The output file starts at file offset 700 (decimal), not offset `$0700` (1792 decimal)

**Evidence:**
The boot sector header bytes at file offset 0 are: `d0 03 00 07 40 15`
The bytes at file offset 700 (decimal) are: `03 03 ae fe 12 a9 31...`

The generated `boot_loader.s` from the script showed:
```ca65
	.org	$0700
boot_start:
	.byte	$03
	.byte	$03
	LDX $12FE
```

The first bytes `$03 $03` match file offset 700 (decimal), not offset 0. The correct output should show the boot header bytes `$D0 $03`.

**Expected behavior:**
In the script format, `$0700` should be treated as hex `0x0700` = 1792 decimal, just like the CLI parser does.

**Additional context:**
The CLI correctly parses `$0700` as hex. The issue is isolated to the script parser. This means every script command using `offset=$XXXX` with hex values would produce wrong results.

---

## Bug 2: `--start-address` value is not applied to `.org` directive in ca65 output

**Severity:** High (incorrect memory addresses)

**Description:**
The `--start-address` parameter is documented as "Override the memory start address" but is not applied to the `.org` directive in the ca65 output format. The `.org` always shows `$0000` regardless of the `--start-address` value.

**Steps to reproduce:**
```bash
atarihacker -t raw_disk.bin disassemble 0 384 --format ca65 --start-address='$0700'
```

**Expected output:**
```ca65
	.org	$0700
```

**Actual output:**
```ca65
	.org	$0000
```

**Workaround:**
This issue was eventually resolved by using single quotes around the value: `--start-address='$0700'`. However, earlier attempts without proper quoting or with different quoting styles also failed. The issue appears to be related to how the value is parsed versus how it's applied to the output.

---

## Bug 3: Boot sector header (6 bytes) disassembled as code despite `--analyze`

**Severity:** Medium (cosmetic for known data)

**Description:**
The 6-byte boot sector header (`$D0 $03 $00 $07 $40 $15`) is disassembled as 6502 code instead of being emitted as `.byte` data directives, even with the `--analyze` flag. The header bytes happen to decode as:

```
$D0 = BNE (relative branch)
$03 = BRK
$00 = BRK
$07 = .byte (data)
$40 = RTI
$15 = ORA zp,X
```

**Expected behavior:**
The `--analyze` flag should detect the boot header pattern (or the first few bytes of a boot segment) as data, similar to how it handles other data regions.

**Suggested fix:**
Boot sector headers follow the pattern: `[boot_flag] [sector_count] [load_addr_lo] [load_addr_hi] [init_addr_lo] [init_addr_hi]`. The analyzer could check for this pattern at the start of any segment loaded at `$0700` and mark the first 6 bytes as data.

---

## Bug 4: Zero-page OS variable labels applied to code addresses

**Severity:** Medium (confusing output)

**Description:**
OS zero-page variable labels (like `RUNADH`, `INITAD`, `MEMTOP`, `CBAUD`, etc.) are applied as code labels in the disassembly output. Since the code is running at addresses like `$0700+`, and the zero-page is at `$0000-$00FF`, these labels should not appear as code labels. They should only appear as operand comments when the code references those addresses.

**Example from `boot_loader.s`:**
```ca65
RUNADH:
	BEQ DINDEX	; DINDEX
INITADH:
	LDA RUNADH	; RUNADH
MEMTOP:
	TAY
MEMTOPH:
	JSR sub_0757
DVSTAT:
	PLA
CBAUD:
	JMP jmp_072F
```

These labels (`RUNADH`, `INITADH`, `MEMTOP`, `DVSTAT`, `CBAUD`) are zero-page OS variables at `$0042-$004D`. They should NOT appear as code labels. The code at `$0700+` is not at those addresses.

**Expected behavior:**
Zero-page OS variable symbols should only be used as comments in operand positions (e.g., `LDA RUNADH ; RUNADH`), never as code labels in the left column.

---

## Bug 5: Boot sector load address incorrectly parsed

**Severity:** Low

**Description:**
`atarihacker atr analyze-boot` shows the boot header bytes as `D0 03 00 07 40 15` but the header analysis shows:
- Load address: $0700
- Init address: $1540

The boot header bytes are `D0 03 00 07 40 15`:
- Bytes 0-1: $D0, $03 (boot flag, sector count)
- Bytes 2-3: $00, $07 (load address = $0700)
- Bytes 4-5: $40, $15 (init address = $1540)

This is actually correct. However, the documentation in `usage.md` shows an example where the header bytes are `00 03 00 07 00 07` with `DOS boot: Yes`. The pattern `$D0` for the boot flag means "stop/run" (custom loader), which is correctly reported. No actual bug here, just noting that the documentation example could be expanded to cover the `$D0` boot flag case.

---

## Summary

| # | Bug | Severity | Status |
|---|-----|----------|--------|
| 1 | Script format: `$` prefix not parsed as hex for `offset` | High | Unconfirmed |
| 2 | `--start-address` not applied to `.org` in ca65 output | High | Unconfirmed |
| 3 | Boot header disassembled as code despite `--analyze` | Medium | Unconfirmed |
| 4 | Zero-page OS labels applied as code labels | Medium | Unconfirmed |
| 5 | Documentation example could be expanded | Low | Enhancement |

---

## Environment

- **Tool:** atarihacker (Atari Hacker MCP v4)
- **Platform:** Linux 7.0, x86_64
- **Shell:** /bin/bash
- **Raw binary:** 92,160 byte disk image (720 sectors × 128 bytes)
- **ATR format:** Single Density, custom filesystem