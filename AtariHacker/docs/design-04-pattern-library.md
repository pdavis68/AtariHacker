# Design Document: Pattern Library

**Issue:** Expanded Hacking Functionality — Advanced Pattern Analysis  
**Ref:** [`docs/application-refactor-1_plan.md`](application-refactor-1_plan.md:25)  
**Status:** Draft

---

## 1. Problem Statement

Currently, `find-pattern` accepts inline byte patterns with `??` wildcards but has no mechanism to save, name, or reuse patterns. Reverse engineers often need to search for the same signatures across multiple ROMs (e.g., the same jump table pattern, the same initialization sequence). An LLM cannot build or share a library of known patterns across sessions.

## 2. Proposed Design

Introduce a `patterns` command with sub-commands for managing a persistent pattern library stored as a JSON file alongside the project configuration.

### 2.1 Pattern Library File

Stored as `.atari-hacker-patterns.json` in the project directory (same discovery logic as `.atari-hacker.config`):

```json
{
  "version": 1,
  "patterns": [
    {
      "name": "jsr_rts_stub",
      "description": "Common 6502 stub: JSR to address, RTS",
      "hex": "20 ?? ?? 60",
      "tags": ["stub", "common"],
      "category": "code-patterns",
      "created": "2026-07-25T12:00:00Z"
    },
    {
      "name": "antic_dlist_lms",
      "description": "ANTIC display list with LMS (Load Memory Scan)",
      "hex": "42 ?? ?? 4F ?? ?? 41 ?? ?? 02 01",
      "tags": ["antic", "display-list"],
      "category": "hardware",
      "created": "2026-07-25T12:00:00Z"
    },
    {
      "name": "pokey_init_audctl",
      "description": "POKEY audio initialization sequence",
      "hex": "A9 03 8D 08 D2 A9 00 8D 00 D2",
      "tags": ["pokey", "audio", "init"],
      "category": "hardware",
      "created": "2026-07-25T12:00:00Z"
    }
  ]
}
```

### 2.2 Pattern Data Model

```csharp
public class PatternEntry
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Hex { get; set; } = "";        // Byte pattern with ?? wildcards
    public List<string> Tags { get; set; } = new();
    public string Category { get; set; } = "uncategorized";
    public DateTime Created { get; set; }
    public DateTime? Modified { get; set; }
    public int? MatchCount { get; set; }          // Populated on search
}
```

### 2.3 Pattern Library Manager

New class [`State/PatternLibrary.cs`](../State/PatternLibrary.cs):

```csharp
public class PatternLibrary
{
    public int Version { get; set; } = 1;
    public List<PatternEntry> Patterns { get; set; } = new();

    public static PatternLibrary Load(string? directory = null);
    public void Save(string? directory = null);
    public void Add(PatternEntry entry);
    public bool Remove(string name);
    public PatternEntry? Find(string name);
    public List<PatternEntry> Search(string? tag = null, string? category = null, string? query = null);
}
```

### 2.4 CLI Commands

| Sub-command         | Description                                          |
|---------------------|------------------------------------------------------|
| `patterns list`     | List all saved patterns (with optional `--tag`, `--category`, `--query` filters) |
| `patterns add`      | Save a new pattern from inline hex                   |
| `patterns remove`   | Delete a pattern by name                             |
| `patterns show`     | Display full details of a named pattern              |
| `patterns search`   | Search the loaded binary using a saved pattern       |
| `patterns import`   | Import patterns from a JSON file                     |
| `patterns export`   | Export patterns to a JSON file                       |

### 2.5 Pattern Search Integration

The `patterns search <name>` command loads the pattern from the library and runs `FindPatternTool.FindPattern` with it:

```bash
atarihacker -- patterns search jsr_rts_stub
```

This is equivalent to:
```bash
atarihacker -- find-pattern "20 ?? ?? 60"
```

But with the advantage of documentation, tagging, and reusability.

## 3. Implementation Plan

### Phase 1: Core Library

1. Create [`State/PatternLibrary.cs`](../State/PatternLibrary.cs) — data model, load/save, CRUD
2. Create [`Tools/PatternTools.cs`](../Tools/PatternTools.cs) — CLI command implementations
3. Register `patterns` command in [`Program.cs`](../Program.cs)

### Phase 2: Search Integration

1. Wire `patterns search` to reuse `FindPatternTool.FindPattern`
2. Add `--max-results` option to limit output

### Phase 3: Import/Export

1. Implement `patterns import` — merge patterns from external file
2. Implement `patterns export` — subset or full export

## 4. API/Syntax Changes

```bash
# List patterns
atarihacker -- patterns list
atarihacker -- patterns list --tag antic
atarihacker -- patterns list --category hardware

# Add a pattern
atarihacker -- patterns add name=jsr_rts_stub hex="20 ?? ?? 60" \
    --description "Common 6502 stub" --tag stub --tag common

# Remove a pattern
atarihacker -- patterns remove jsr_rts_stub

# Show pattern details
atarihacker -- patterns show jsr_rts_stub

# Search binary using a saved pattern
atarihacker -- patterns search jsr_rts_stub --max-results 20

# Import/Export
atarihacker -- patterns import shared_patterns.json
atarihacker -- patterns export --tag antic > antic_patterns.json
```

## 5. Data Structures

### New: `PatternEntry`

```csharp
public class PatternEntry
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Hex { get; set; }
    public List<string> Tags { get; set; }
    public string Category { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Modified { get; set; }
    [JsonIgnore] public int? MatchCount { get; set; }
}
```

### New: `PatternLibrary`

```csharp
public class PatternLibrary
{
    public int Version { get; set; }
    public List<PatternEntry> Patterns { get; set; }
    
    public static PatternLibrary Load(string? directory);
    public void Save(string? directory);
    public void Add(PatternEntry entry);
    public bool Remove(string name);
    public PatternEntry? Find(string name);
    public List<PatternEntry> Query(string? tag, string? category, string? query);
}
```

### New: `PatternTools`

```csharp
public static class PatternTools
{
    public static void ListPatterns(CliSession session, string? tag, string? category, string? query, string format);
    public static void AddPattern(CliSession session, string name, string hex, string? description, string[]? tags, string? category);
    public static void RemovePattern(CliSession session, string name);
    public static void ShowPattern(CliSession session, string name);
    public static void SearchPattern(CliSession session, string name, int maxResults);
    public static void ImportPatterns(CliSession session, string path);
    public static void ExportPatterns(CliSession session, string? tag, string? category, string output);
}
```

## 6. Affected Files

| File                          | Change                                                |
|-------------------------------|-------------------------------------------------------|
| `State/PatternLibrary.cs`     | **New** — pattern data model and persistence           |
| `Tools/PatternTools.cs`       | **New** — CLI command implementations                  |
| `Program.cs`                  | Register `patterns` command tree                       |
| `Tools/FindPatternTool.cs`    | No changes (reused by pattern search)                  |

## 7. Testing Considerations

- Pattern file discovery: should use same upward-search logic as `.atari-hacker.config`
- Pattern validation: hex strings must be valid (even number of hex chars, `??` for wildcards)
- Duplicate names: `patterns add` with existing name should prompt or require `--force`
- Search integration: `patterns search` should produce identical results to `find-pattern` with the same hex
- Import merge: importing patterns with duplicate names should not overwrite without `--force`
- Empty library: `patterns list` on empty library should show helpful message
