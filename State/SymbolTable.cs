namespace AtariHacker.State;

[Flags]
public enum SymbolGroup
{
    None = 0,
    Hardware = 1 << 0,
    OsVariables = 1 << 1,
    OsRom = 1 << 2,
    UserLabels = 1 << 3,
    All = Hardware | OsVariables | OsRom | UserLabels
}

public sealed record SymbolEntry(
    string Label,
    string? Comment = null,
    bool IsHardware = false,
    bool IsUserDefined = false,
    SymbolGroup Group = SymbolGroup.UserLabels);

public sealed class SymbolTable : Dictionary<ushort, SymbolEntry>
{
    /// <summary>
    /// Gets or sets the enabled symbol groups. Disabled group symbols are not
    /// emitted in disassembly output.
    /// </summary>
    public SymbolGroup EnabledGroups { get; set; } = SymbolGroup.All;

    /// <summary>
    /// Returns true if the symbol at the given address should be visible
    /// based on the current enabled groups.
    /// </summary>
    public bool IsSymbolEnabled(ushort address)
    {
        if (!TryGetValue(address, out var entry))
        {
            return false;
        }

        // User-defined symbols are always enabled if the UserLabels group is enabled
        if (entry.IsUserDefined)
        {
            return EnabledGroups.HasFlag(SymbolGroup.UserLabels);
        }

        // Hardware symbols are enabled if their group is enabled
        return EnabledGroups.HasFlag(entry.Group);
    }
}
