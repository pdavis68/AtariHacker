using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class RomSessionTests
{
    [Fact]
    public void IsLoaded_ReturnsFalseWhenDataIsNull()
    {
        var session = new RomSession();
        Assert.False(session.IsLoaded);
    }

    [Fact]
    public void IsLoaded_ReturnsTrueWhenDataIsNotNull()
    {
        var session = new RomSession { Data = new byte[100] };
        Assert.True(session.IsLoaded);
    }

    [Fact]
    public void Length_ReturnsDataLength()
    {
        var session = new RomSession { Data = new byte[100] };
        Assert.Equal(100, session.Length);
    }

    [Fact]
    public void Length_ReturnsZeroWhenDataIsNull()
    {
        var session = new RomSession();
        Assert.Equal(0, session.Length);
    }

    [Fact]
    public void Load_SetsFilePathAndDataAndClearsMetadata()
    {
        var session = new RomSession
        {
            Segments = new List<Atari.XexSegment>(),
            RunAddress = 0x8000,
            InitAddress = 0x8100,
            BaseAddress = 0x8000,
            SourceAtrPath = "/path/to/file.atr",
            BootHeader = new BootHeader(0, 1, 0x8000, 0x8100)
        };

        var data = new byte[] { 0x00, 0x01, 0x02 };
        session.Load("/path/to/file.rom", data);

        Assert.Equal("/path/to/file.rom", session.FilePath);
        Assert.Same(data, session.Data);
        Assert.Null(session.Segments);
        Assert.Null(session.RunAddress);
        Assert.Null(session.InitAddress);
        Assert.Null(session.BaseAddress);
        Assert.Null(session.SourceAtrPath);
        Assert.Null(session.BootHeader);
    }

    [Fact]
    public void ClearMetadata_ResetsAllOptionalFieldsToNull()
    {
        var session = new RomSession
        {
            Data = new byte[10],
            Segments = new List<Atari.XexSegment>(),
            RunAddress = 0x8000,
            InitAddress = 0x8100,
            BaseAddress = 0x8000,
            SourceAtrPath = "/path/to/file.atr",
            BootHeader = new BootHeader(0, 1, 0x8000, 0x8100)
        };

        session.ClearMetadata();

        Assert.NotNull(session.Data); // Data should not be cleared
        Assert.Null(session.Segments);
        Assert.Null(session.RunAddress);
        Assert.Null(session.InitAddress);
        Assert.Null(session.BaseAddress);
        Assert.Null(session.SourceAtrPath);
        Assert.Null(session.BootHeader);
    }
}