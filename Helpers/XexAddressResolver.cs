using AtariHackerMCP.Atari;
using AtariHackerMCP.State;

namespace AtariHackerMCP.Helpers;

internal static class XexAddressResolver
{
    public static ushort? ResolveFileOffset(RomSession session, int fileOffset, ushort? overrideStartAddress = null)
    {
        if (overrideStartAddress is not null)
        {
            return (ushort)(overrideStartAddress.Value + fileOffset);
        }

        if (session.Segments is { Count: > 0 })
        {
            return XexParser.FileOffsetToMemoryAddress(session.Segments, fileOffset);
        }

        if (session.BaseAddress is not null)
        {
            return (ushort)(session.BaseAddress.Value + fileOffset);
        }

        return fileOffset <= 0xFFFF ? (ushort)fileOffset : null;
    }

    public static int? ResolveMemoryAddress(RomSession session, ushort memoryAddress)
    {
        if (session.Segments is { Count: > 0 })
        {
            return XexParser.MemoryAddressToFileOffset(session.Segments, memoryAddress);
        }

        if (session.BaseAddress is not null)
        {
            var offset = memoryAddress - session.BaseAddress.Value;
            return offset >= 0 && offset < session.Length ? offset : null;
        }

        return memoryAddress < session.Length ? memoryAddress : null;
    }
}
