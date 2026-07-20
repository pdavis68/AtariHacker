using AtariHacker.State;

namespace AtariHacker.Helpers;

internal static class SymbolResolver
{
    public static string? Resolve(ushort address, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        if (symbols.TryGetValue(address, out var symbol) && symbols.IsSymbolEnabled(address))
        {
            return symbol.Label;
        }

        if (address <= 0xFF && zeroPageMap.TryGetValue((byte)address, out var zpSymbol))
        {
            // Zero page symbols are always enabled if the OsVariables group is enabled
            if (symbols.EnabledGroups.HasFlag(SymbolGroup.OsVariables))
            {
                return zpSymbol.Label;
            }
        }

        return null;
    }

    public static SymbolEntry? ResolveEntry(ushort address, SymbolTable symbols, ZeroPageMap zeroPageMap)
    {
        if (symbols.TryGetValue(address, out var symbol) && symbols.IsSymbolEnabled(address))
        {
            return symbol;
        }

        if (address <= 0xFF && zeroPageMap.TryGetValue((byte)address, out var zpSymbol))
        {
            if (symbols.EnabledGroups.HasFlag(SymbolGroup.OsVariables))
            {
                return zpSymbol;
            }
        }

        return null;
    }
}
