using AtariHacker.Analysis;
using AtariHacker.Atari;

namespace AtariHacker.Test;

public sealed class DisassemblyAnalyzerTests
{
    [Fact]
    public void Analyze_ReturnsEmptyReferenceGraphForNullData()
    {
        var result = DisassemblyAnalyzer.Analyze(null!, null, null);
        Assert.Empty(result.SubroutineEntries);
        Assert.Empty(result.JumpTargets);
        Assert.Empty(result.BranchTargets);
        Assert.Empty(result.IndirectJumpTargets);
        Assert.Empty(result.AbsoluteDataReferences);
        Assert.Empty(result.IndirectDataReferences);
        Assert.Empty(result.CodeEntryPoints);
        Assert.Empty(result.DataAddresses);
        Assert.Empty(result.InstructionAddresses);
    }

    [Fact]
    public void Analyze_ReturnsEmptyReferenceGraphForEmptyData()
    {
        var result = DisassemblyAnalyzer.Analyze(Array.Empty<byte>(), null, null);
        Assert.Empty(result.SubroutineEntries);
    }

    [Fact]
    public void Analyze_DetectsJsrTargetsAsSubroutineEntries()
    {
        var data = new byte[] { 0x20, 0x00, 0x81, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x8000);

        Assert.Contains((ushort)0x8100, result.SubroutineEntries);
        Assert.Contains((ushort)0x8100, result.CodeEntryPoints);
    }

    [Fact]
    public void Analyze_DetectsJmpAbsoluteTargetsAsJumpTargets()
    {
        var data = new byte[] { 0x4C, 0x00, 0x82, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x8000);

        Assert.Contains((ushort)0x8200, result.JumpTargets);
        Assert.Contains((ushort)0x8200, result.CodeEntryPoints);
    }

    [Fact]
    public void Analyze_DetectsIndirectJmpTargets()
    {
        var data = new byte[] { 0x6C, 0x00, 0x83, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x8000);

        Assert.Contains((ushort)0x8300, result.IndirectJumpTargets);
    }

    [Fact]
    public void Analyze_CollectsAbsoluteDataReferences()
    {
        var data = new byte[] { 0xAD, 0x12, 0xD0, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x8000);

        Assert.Contains((ushort)0xD012, result.AbsoluteDataReferences);
    }

    [Fact]
    public void Analyze_CollectsIndirectDataReferences()
    {
        var data = new byte[] { 0xB1, 0x80, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x8000);

        Assert.Contains((byte)0x80, result.IndirectDataReferences);
    }

    [Fact]
    public void ReferenceGraph_Empty_ReturnsGraphWithAllEmptySets()
    {
        var empty = ReferenceGraph.Empty;
        Assert.Empty(empty.SubroutineEntries);
        Assert.Empty(empty.JumpTargets);
        Assert.Empty(empty.BranchTargets);
        Assert.Empty(empty.IndirectJumpTargets);
        Assert.Empty(empty.AbsoluteDataReferences);
        Assert.Empty(empty.IndirectDataReferences);
        Assert.Empty(empty.CodeEntryPoints);
        Assert.Empty(empty.DataAddresses);
        Assert.Empty(empty.InstructionAddresses);
        Assert.Null(empty.BootHeader);
    }

    // ─── Boot header detection tests ────────────────────────────────────

    [Fact]
    public void Analyze_DetectsBootHeaderWithD0Flag()
    {
        var data = new byte[] { 0xD0, 0x03, 0x00, 0x07, 0x40, 0x15, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x0700);

        Assert.NotNull(result.BootHeader);
        Assert.Equal(0xD0, result.BootHeader!.Flag);
        Assert.Equal(3, result.BootHeader.SectorCount);
        Assert.Equal(0x0700, result.BootHeader.LoadAddress);
        Assert.Equal(0x1540, result.BootHeader.InitAddress);
    }

    [Fact]
    public void Analyze_DetectsBootHeaderWith00Flag()
    {
        var data = new byte[] { 0x00, 0x03, 0x00, 0x07, 0x00, 0x07, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x0700);

        Assert.NotNull(result.BootHeader);
        Assert.Equal(0x00, result.BootHeader!.Flag);
    }

    [Fact]
    public void Analyze_DoesNotDetectBootHeaderForInvalidFlag()
    {
        var data = new byte[] { 0xFF, 0x03, 0x00, 0x07, 0x40, 0x15, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x0700);

        Assert.Null(result.BootHeader);
    }

    [Fact]
    public void Analyze_DoesNotDetectBootHeaderForShortData()
    {
        var data = new byte[] { 0xD0, 0x03, 0x00 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x0700);

        Assert.Null(result.BootHeader);
    }

    [Fact]
    public void Analyze_BootHeaderMarksBytesAsDataReferences()
    {
        var data = new byte[] { 0xD0, 0x03, 0x00, 0x07, 0x40, 0x15, 0x60 };
        var result = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x0700);

        // All 6 boot header bytes should be data references
        for (ushort addr = 0x0700; addr < 0x0706; addr++)
        {
            Assert.Contains(addr, result.AbsoluteDataReferences);
        }
    }
}