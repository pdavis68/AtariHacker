# Design Document: Structural Pattern Matching

**Issue:** Expanded Hacking Functionality — Advanced Pattern Analysis  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:27)  
**Status:** Draft**

---

## 1. Problem Statement

The current `DataProber` uses 7 heuristics (string, padding, charset, table, display list, sprite, map) to identify data types. However, these heuristics are hard-coded and limited to low-level data classification. Many Atari games share common **structural** patterns — level headers, sprite tables, music data formats, object state tables — that cannot be detected by generic byte analysis. Adding configurable structural templates would allow LLMs and users to define and detect these higher-level patterns.

## 2. Proposed Design

Introduce a **structural pattern template system** that allows defining reusable, configurable data structure descriptors. These templates describe the layout of a data structure at a given memory address, including field types, offsets, and validation rules.

### 2.1 Template Definition Format

Templates are defined in JSON and stored in the pattern library or in standalone `.atari-struct.json` files:

```json
{
  "name": "atari_level_header",
  "description": "Common Atari game level header structure",
  "version": 1,
  "fields": [
    { "name": "width", "offset": 0, "type": "byte", "description": "Level width in tiles" },
    { "name": "height", "offset": 1, "type": "byte", "description": "Level height in tiles" },
    { "name": "tile_map_ptr", "offset": 2, "type": "word_le", "description": "Pointer to tile map data" },
    { "name": "color_ptr", "offset": 4, "type": "word_le", "description": "Pointer to color data" },
    { "name": "enemy_count", "offset": 6, "type": "byte", "description": "Number of enemies" },
    { "name": "enemy_table_ptr", "offset": 7, "type": "word_le", "description": "Pointer to enemy table" },
    { "name": "music_ptr", "offset": 9, "type": "word_le", "description": "Pointer to music data" },
    { "name": "palette", "offset": 11, "type": "bytes", "length": 4, "description": "4-byte palette" }
  ],
  "validation": [
    { "field": "width", "min": 1, "max": 40 },
    { "field": "height", "min": 1, "max": 24 },
    { "field": "tile_map_ptr", "range": ["$A000", "$BFFF"] },
    { "field": "enemy_count", "min": 0, "max": 32 }
  ],
  "tags": ["level", "game-structure"],
  "category": "game-templates"
}
```

### 2.2 Field Types

| Type        | Description                          | Size     |
|-------------|--------------------------------------|----------|
| `byte`      | Unsigned 8-bit integer               | 1 byte   |
| `word_le`   | 16-bit little-endian address/value   | 2 bytes  |
| `word_be`   | 16-bit big-endian value              | 2 bytes  |
| `bytes`     | Raw byte sequence (requires `length`) | variable |
| `string`    | Null-terminated ATASCII string       | variable |
| `bitfield`  | Individual bit flags                 | 1 byte   |
| `skip`      | Padding/alignment gap                | variable |

### 2.3 Detection Algorithm

The `StructureMatcher` class scans a memory range and attempts to match templates:

1. **Load template** from library or file
2. **For each candidate address** in the range:
   a. Read each field at its offset
   b. Validate against constraints (min/max, range, etc.)
   c. If all fields pass validation, record a match
3. **Score matches** by:
   - Number of pointer fields that point to valid memory ranges
   - Number of validation rules satisfied
   - Byte entropy / consistency checks
4. **Return ranked matches** with confidence scores

### 2.4 CLI Commands

| Sub-command                  | Description                                      |
|------------------------------|--------------------------------------------------|
| `struct define`              | Define a new structural template                 |
| `struct list`                | List available templates                         |
| `struct show`                | Show template details                            |
| `struct match <start> <end>` | Scan memory range for template matches           |
| `struct remove`              | Delete a template                                |
| `struct import`              | Import templates from JSON file                  |
| `struct export`              | Export templates to JSON file                    |

## 3. Implementation Plan

### Phase 1: Template Engine

1. Create [`Analysis/StructureMatcher.cs`](../Analysis/StructureMatcher.cs) — core matching engine
2. Create [`State/StructureTemplate.cs`](../State/StructureTemplate.cs) — data model
3. Create [`Tools/StructureTools.cs`](../Tools/StructureTools.cs) — CLI command implementations

### Phase 2: Template Library Integration

1. Store templates in the pattern library file (`.atari-hacker-patterns.json`) or a separate `.atari-struct.json`
2. Add built-in templates for common Atari structures:
   - ANTIC display list (already partially detected by `DataProber`)
   - Standard level header
   - Sprite animation table
   - Music note table
   - Jump table (indirect)

### Phase 3: Integration with Probe

1. When `probe` runs and detects a data region, optionally run structural matching
2. Add `--struct` flag to `probe` to enable template matching

## 4. API/Syntax Changes

```bash
# Define a template from JSON file
atarihacker -- struct define level_header.json

# List templates
atarihacker -- struct list
atarihacker -- struct list --tag game-structure

# Show template details
atarihacker -- struct show atari_level_header

# Match against memory range
atarihacker -- struct match $0C00 $1CFF --template atari_level_header
atarihacker -- struct match $0C00 $1CFF  # matches all templates

# Remove template
atarihacker -- struct remove atari_level_header

# Import/Export
atarihacker -- struct import shared_templates.json
atarihacker -- struct export --tag game-structure > my_templates.json

# Probe with structural matching
atarihacker -- probe $0C00 $1CFF --struct
```

## 5. Data Structures

### New: `StructureField`

```csharp
public class StructureField
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
    public string Type { get; set; } = "byte";  // byte, word_le, word_be, bytes, string, bitfield, skip
    public int? Length { get; set; }             // For bytes type
    public string? Description { get; set; }
}
```

### New: `FieldValidation`

```csharp
public class FieldValidation
{
    public string Field { get; set; } = "";
    public int? Min { get; set; }
    public int? Max { get; set; }
    public string[]? Range { get; set; }  // e.g., ["$A000", "$BFFF"]
}
```

### New: `StructureTemplate`

```csharp
public class StructureTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<StructureField> Fields { get; set; } = new();
    public List<FieldValidation> Validation { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Category { get; set; } = "game-templates";
}
```

### New: `StructureMatch`

```csharp
public class StructureMatch
{
    public string TemplateName { get; set; } = "";
    public ushort Address { get; set; }
    public double Confidence { get; set; }
    public Dictionary<string, object> FieldValues { get; set; } = new();
    public List<string> ValidationResults { get; set; } = new();
}
```

## 6. Affected Files

| File                              | Change                                                |
|-----------------------------------|-------------------------------------------------------|
| `State/StructureTemplate.cs`      | **New** — template data model                         |
| `Analysis/StructureMatcher.cs`    | **New** — matching engine                             |
| `Tools/StructureTools.cs`         | **New** — CLI command implementations                 |
| `Program.cs`                      | Register `struct` command tree                        |
| `Tools/AnalysisTools.cs`          | Optional `--struct` flag on `probe`                   |
| `State/PatternLibrary.cs`         | Optional storage for templates                        |

## 7. Testing Considerations

- Template validation: invalid field types, missing required fields, circular references
- Match accuracy: known structures should be detected with high confidence
- False positives: random data should produce few/no matches
- Performance: scanning a 16KB range with 10 templates should complete in < 1 second
- Built-in templates: verify ANTIC display list template matches known display lists
- Edge cases: zero-length fields, overlapping templates, templates with only `skip` fields
