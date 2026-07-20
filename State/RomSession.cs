using AtariHackerMCP.Atari;

namespace AtariHackerMCP.State;

public sealed record BootHeader(
    byte Flag,
    byte SectorCount,
    ushort LoadAddress,
    ushort InitAddress
);

public sealed class RomSession
{
    public string? FilePath { get; set; }

    public byte[]? Data { get; set; }

    public IReadOnlyList<XexSegment>? Segments { get; set; }

    public ushort? RunAddress { get; set; }

    public ushort? InitAddress { get; set; }

    public ushort? BaseAddress { get; set; }

    /// <summary>
    /// When the loaded data was extracted from an ATR, the path to that ATR.
    /// </summary>
    public string? SourceAtrPath { get; set; }

    /// <summary>
    /// When loading boot sectors, the decoded boot header fields (if standard).
    /// </summary>
    public BootHeader? BootHeader { get; set; }

    public int Length => Data?.Length ?? 0;

    public bool IsLoaded => Data is not null;

    public void Load(string filePath, byte[] data)
    {
        FilePath = filePath;
        Data = data;
        Segments = null;
        RunAddress = null;
        InitAddress = null;
        BaseAddress = null;
        SourceAtrPath = null;
        BootHeader = null;
    }

    public void ClearMetadata()
    {
        Segments = null;
        RunAddress = null;
        InitAddress = null;
        BaseAddress = null;
        SourceAtrPath = null;
        BootHeader = null;
    }
}
