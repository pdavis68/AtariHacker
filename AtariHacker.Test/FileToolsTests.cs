using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class FileToolsTests
{
    [Fact]
    public void BuildRomInfo_IncludesFilePathAndSize()
    {
        var session = new RomSession
        {
            FilePath = "/path/to/test.rom",
            Data = new byte[8192]
        };

        var info = FileTools.BuildRomInfo(session, new SymbolTable(), new ZeroPageMap(), false);
        Assert.Contains("test.rom", info);
        Assert.Contains("8192", info);
    }

    [Fact]
    public void BuildRomInfo_ShowsBaseAddressForRawBinaries()
    {
        var session = new RomSession
        {
            FilePath = "/path/to/test.bin",
            Data = new byte[100],
            BaseAddress = (ushort)0x8000
        };

        var info = FileTools.BuildRomInfo(session, new SymbolTable(), new ZeroPageMap(), false);
        Assert.Contains("$8000", info);
    }

    [Fact]
    public void PopulateMetadata_ClearsExistingMetadata()
    {
        var session = new RomSession
        {
            FilePath = "/path/to/test.rom",
            Data = new byte[100],
            RunAddress = (ushort)0x8000,
            InitAddress = (ushort)0x8100
        };

        FileTools.PopulateMetadata(session, session.Data);
        Assert.Null(session.RunAddress);
        Assert.Null(session.InitAddress);
    }

    [Fact]
    public void PopulateMetadata_ParsesXexSegmentsForXexFiles()
    {
        var data = new byte[2 + 4 + 256];
        data[0] = 0xFF; data[1] = 0xFF;
        data[2] = 0x00; data[3] = 0x80;
        data[4] = 0xFF; data[5] = 0x80;

        var session = new RomSession
        {
            FilePath = "/path/to/test.xex",
            Data = data
        };

        FileTools.PopulateMetadata(session, data);
        Assert.NotNull(session.Segments);
        Assert.NotEmpty(session.Segments);
    }

    [Fact]
    public void PopulateMetadata_DoesNothingForNonXexFiles()
    {
        var session = new RomSession
        {
            FilePath = "/path/to/test.rom",
            Data = new byte[100]
        };

        FileTools.PopulateMetadata(session, session.Data);
        Assert.Null(session.Segments);
    }
}