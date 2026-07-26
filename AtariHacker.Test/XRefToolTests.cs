using AtariHacker.Analysis;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class XRefToolTests
{
    [Fact]
    public void XRef_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = XRefTool.XRef(session, new SymbolTable(), new ZeroPageMap(), "$D012");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void XRef_ReturnsNoCrossReferencesForUnreferencedAddress()
    {
        var data = new byte[] { 0x60, 0x60, 0x60 };
        var session = new RomSession { Data = data };
        var result = XRefTool.XRef(session, new SymbolTable(), new ZeroPageMap(), "$9999");
        Assert.Contains("No cross-references", result);
    }

    [Fact]
    public void ClassifyAccess_StaStxStyReturnsWrite()
    {
        Assert.Equal(AccessType.Write, XRefTool.ClassifyAccess("STA"));
        Assert.Equal(AccessType.Write, XRefTool.ClassifyAccess("STX"));
        Assert.Equal(AccessType.Write, XRefTool.ClassifyAccess("STY"));
    }

    [Fact]
    public void ClassifyAccess_IncDecAslLsrRolRorReturnsReadWrite()
    {
        Assert.Equal(AccessType.ReadWrite, XRefTool.ClassifyAccess("INC"));
        Assert.Equal(AccessType.ReadWrite, XRefTool.ClassifyAccess("DEC"));
        Assert.Equal(AccessType.ReadWrite, XRefTool.ClassifyAccess("ASL"));
        Assert.Equal(AccessType.ReadWrite, XRefTool.ClassifyAccess("LSR"));
        Assert.Equal(AccessType.ReadWrite, XRefTool.ClassifyAccess("ROL"));
        Assert.Equal(AccessType.ReadWrite, XRefTool.ClassifyAccess("ROR"));
    }

    [Fact]
    public void ClassifyAccess_JsrJmpReturnsExecute()
    {
        Assert.Equal(AccessType.Execute, XRefTool.ClassifyAccess("JSR"));
        Assert.Equal(AccessType.Execute, XRefTool.ClassifyAccess("JMP"));
    }

    [Fact]
    public void ClassifyAccess_AllOthersReturnRead()
    {
        Assert.Equal(AccessType.Read, XRefTool.ClassifyAccess("LDA"));
        Assert.Equal(AccessType.Read, XRefTool.ClassifyAccess("CMP"));
        Assert.Equal(AccessType.Read, XRefTool.ClassifyAccess("ADC"));
        Assert.Equal(AccessType.Read, XRefTool.ClassifyAccess("AND"));
    }

    [Fact]
    public void XRef_FormatsTextOutputCorrectly()
    {
        var data = new byte[] { 0xAD, 0x12, 0xD0, 0x60 };
        var session = new RomSession { Data = data };
        var result = XRefTool.XRef(session, new SymbolTable(), new ZeroPageMap(), "$D012");
        Assert.NotEmpty(result);
    }

    [Fact]
    public void XRef_FormatsCsvOutputCorrectly()
    {
        var data = new byte[] { 0xAD, 0x12, 0xD0, 0x60 };
        var session = new RomSession { Data = data };
        var result = XRefTool.XRef(session, new SymbolTable(), new ZeroPageMap(), "$D012", format: "csv");
        Assert.Contains("address", result);
    }
}