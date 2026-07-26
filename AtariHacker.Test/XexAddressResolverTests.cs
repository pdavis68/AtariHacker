using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class XexAddressResolverTests
{
    [Fact]
    public void ResolveFileOffset_UsesOverrideStartAddressWhenProvided()
    {
        var session = new RomSession { Data = new byte[100] };
        var result = XexAddressResolver.ResolveFileOffset(session, 10, (ushort)0x8000);
        Assert.Equal((ushort)0x800A, result);
    }

    [Fact]
    public void ResolveFileOffset_UsesSessionBaseAddressWhenSet()
    {
        var session = new RomSession
        {
            Data = new byte[100],
            BaseAddress = (ushort)0x8000
        };

        var result = XexAddressResolver.ResolveFileOffset(session, 10);
        Assert.Equal((ushort)0x800A, result);
    }

    [Fact]
    public void ResolveFileOffset_FallsBackToFileOffsetAsMemoryAddress()
    {
        var session = new RomSession { Data = new byte[100] };
        var result = XexAddressResolver.ResolveFileOffset(session, 0x100);
        Assert.Equal((ushort)0x100, result);
    }

    [Fact]
    public void ResolveFileOffset_ReturnsNullForOffsetGreaterThan0xFFFFWithNoMapping()
    {
        var session = new RomSession { Data = new byte[200000] };
        var result = XexAddressResolver.ResolveFileOffset(session, 0x10000);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveMemoryAddress_UsesSessionBaseAddressWhenSet()
    {
        var session = new RomSession
        {
            Data = new byte[100],
            BaseAddress = (ushort)0x8000
        };

        var result = XexAddressResolver.ResolveMemoryAddress(session, (ushort)0x800A);
        Assert.Equal(10, result);
    }

    [Fact]
    public void ResolveMemoryAddress_FallsBackToDirectMapping()
    {
        var session = new RomSession { Data = new byte[100] };
        var result = XexAddressResolver.ResolveMemoryAddress(session, (ushort)0x50);
        Assert.Equal(0x50, result);
    }

    [Fact]
    public void ResolveMemoryAddress_ReturnsNullForOutOfRangeAddress()
    {
        var session = new RomSession { Data = new byte[100] };
        var result = XexAddressResolver.ResolveMemoryAddress(session, (ushort)0x100);
        Assert.Null(result);
    }
}
