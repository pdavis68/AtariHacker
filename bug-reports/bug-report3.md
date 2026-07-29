# AtariHacker Bug Report

## Bug 1: `coverage` and `probe` commands have inconsistent address interpretation

**Severity:** Medium (confusing, produces wrong results)

**Description:**
The `coverage` and `probe` commands accept what their help text calls "offsets" but interpret them inconsistently depending on whether the user supplies raw numbers or hex-prefixed values. When raw numbers are given (e.g., `0` and `384`), they're treated as file offsets but displayed as memory addresses. When hex values are given (e.g., `0x0700` and `0x087E`), they're treated as memory addresses.

This means the same command with different argument styles produces completely different results, and the output display doesn't clarify which interpretation was used.

**Steps to reproduce:**

```bash
# Using raw file offsets — produces wrong/empty result
$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr coverage 0 384
Coverage Analysis: $0000–$0180
  $0000–$0180: 0% code, 0% data (orphaned)
  ---
  Total: 0% code, 0% data
  Orphaned code: 385 bytes (100.0%)
  Embedded data: 0 bytes

# Using memory addresses — produces correct result
$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr coverage 0x0700 0x087E
Coverage Analysis: $0700–$087E
  $0700–$072E: 0% code, 100% data (data)
  $072F–$07CA: 100% code, 0% data (code)
  $07CB–$07F1: 0% code, 100% data (code)
  $07F2–$087E: 100% code, 0% data (code)
  ---
  Total: 78% code, 22% data
  Orphaned code: 0 bytes (0.0%)
  Embedded data: 0 bytes
```

**Expected behavior:**
The `coverage` and `probe` commands should consistently accept memory addresses (since the loaded data has a base address like `$0700`). If file offsets are also accepted, the documentation and parameter names should clearly distinguish between the two, and the output display should match the input type.

**Additional evidence — `probe` command:**
```bash
$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr probe 0 384
ERROR: Address range $0000–$0180 is not in the loaded data.

$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr probe 0x0700 0x087E
Invalid range
  Confidence: Low
  Range exceeds available data.
```

The `probe` command rejects file offsets (0-384) as "not in loaded data" but also rejects memory addresses (0x0700-0x087E) as "exceeds available data." Neither invocation works.

---

## Bug 2: `stack-analyze` checks address against file size instead of memory-mapped range

**Severity:** Medium (produces misleading error)

**Description:**
The `stack-analyze` command validates its address argument against the raw file size (384 bytes = `$0180`) rather than the memory-mapped address range (`$0700–$087E`). A valid memory address like `$0706` is rejected as "beyond data length" even though it's well within the loaded data.

**Steps to reproduce:**

```bash
$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr stack-analyze 0x0706
Stack analysis for $0706:
  Entry stack depth: 0 (return address on stack)
  Maximum depth: 0
  Minimum depth: 0
  Exit stack depth: 0 (balanced)

  Warnings:
      - ERROR: Address $0706 is beyond data length.
```

**Expected behavior:**
The address `$0706` is valid — it's 6 bytes into the loaded data (base `$0700` + offset 6). The validation should check against the memory-mapped range (`$0700` to `$0700 + data_length`), not the raw file size.

---

## Bug 3: `analyze-disassemble` misses code at entry point (doesn't follow initial `JMP`)

**Severity:** High (produces incorrect disassembly)

**Description:**
The multi-pass analysis engine does not follow the initial `JMP` instruction at the boot entry point (`$0706`). The instruction `JMP $0714` at `$0706` jumps to `$0714`, but the analysis marks `$0706–$072E` as data instead of code. Code recognition only starts at `$072F` (labeled `jmp_072F`), which is the first reachable label after the data block at `$0709–$0713`.

**Steps to reproduce:**

```bash
$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr analyze-disassemble 0 384
```

**Actual output (excerpt):**
```asm
$0706  4C         .db          ; JMP $0714 — should be code!
$0707  14         .db
$0708  07         .db
$0709  03         .db          ; data byte (loader parameter)
$070A  03         .db          ; data byte (loader parameter)
$070B  00         .db          ; data byte
$070C  7C         .db          ; data byte
$070D  1A         .db          ; data byte
$070E  01         .db          ; data byte
$070F  04         .db          ; data byte
$0710  00         .db          ; data byte
$0711  7D         .db          ; data byte
$0712  CB         .db          ; data byte
$0713  07         .db          ; data byte
$0714  AC         .db          ; LDY $070E — should be code!
$0715  0E         .db
$0716  07         .db
$0717  F0         .db          ; BEQ $074F — should be code!
$0718  36         .db
...
$072F  18         CLC          ; <-- code recognition starts here
```

**Expected output:**
```asm
$0706  4C 14 07   JMP $0714    ; entry point
$0709  03         .db          ; data (loader parameter)
$070A  03         .db          ; data (loader parameter)
$070B  00         .db          ; data
$070C  7C 1A      .dw $1A7C    ; data (16-bit value)
$070E  01 04      .dw $0401    ; data (16-bit value)
$0710  00 7D CB 07             ; data
$0714  AC 0E 07   LDY $070E    ; code continues here
$0717  F0 36      BEQ $074F
...
```

**Root cause:**
The analysis engine starts scanning from the entry point but doesn't follow unconditional jumps (`JMP`) to discover additional code regions. It only follows `JSR` calls and branch instructions. The `JMP $0714` at `$0706` is treated as a terminal instruction, so `$0714` is never added to the worklist of addresses to analyze.

**Suggested fix:**
When the analysis engine encounters a `JMP` instruction at a known entry point, it should add the jump target to the worklist for further analysis, similar to how it handles `JSR` targets.

---

## Bug 4: `segment list` shows no segments after `analyze-full`

**Severity:** Medium (lost functionality)

**Description:**
The `analyze-full` command reports "Segments created: 19" but the `segment list` command shows "No segments defined." The segments created during analysis are not persisted to the segment manager, making them inaccessible for subsequent operations.

**Steps to reproduce:**

```bash
$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr analyze-full
=== Full Analysis Complete ===
  Code entry points: 23
  Subroutines: 5
  Jump targets: 3
  Branch targets: 15
  Data references: 43
  Code bytes: 298 (72.7%)
  Data bytes: 112 (27.3%)
  Labels generated: 177
  Segments created: 19

$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr segment list
No segments defined.
```

**Expected behavior:**
The 19 segments reported as "created" should be visible via `segment list` and usable for segment-aware disassembly.

---

## Bug 5: `symbol list` shows no symbols after `analyze-full`

**Severity:** Medium (lost functionality)

**Description:**
Same root cause as Bug 4. The `analyze-full` command reports "Labels generated: 177" but `symbol list` shows "No symbols matched the current filter." The generated labels are not accessible via the symbol management commands.

**Steps to reproduce:**

```bash
$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr analyze-full
=== Full Analysis Complete ===
  Labels generated: 177

$ atarihacker --target Agent_USA_1984_Scholastic_Wizware_US.atr symbol list
No symbols matched the current filter.
```

**Expected behavior:**
The 177 generated labels should be visible via `symbol list` and usable for subsequent disassembly and analysis.

---


---

## Summary

| # | Bug | Severity | Area |
|---|-----|----------|------|
| 1 | `coverage`/`probe` inconsistent address interpretation | Medium | CLI / Address handling |
| 2 | `stack-analyze` checks address against file size, not memory range | Medium | Address validation |
| 3 | `analyze-disassemble` doesn't follow initial `JMP` at entry point | High | Analysis engine |
| 4 | `segment list` shows nothing after `analyze-full` | Medium | Session persistence |
| 5 | `symbol list` shows nothing after `analyze-full` | Medium | Session persistence |
