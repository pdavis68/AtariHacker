using AtariHacker.Analysis;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class XRefEntryTests
{
    [Fact]
    public void XRefEntry_RecordStoresAllProperties()
    {
        var entry = new XRefEntry(
            0x8000,
            "LDA",
            "$D012",
            AccessType.Read,
            "main",
            "Code");

        Assert.Equal(0x8000, entry.Address);
        Assert.Equal("LDA", entry.Mnemonic);
        Assert.Equal("$D012", entry.Operand);
        Assert.Equal(AccessType.Read, entry.Access);
        Assert.Equal("main", entry.Procedure);
        Assert.Equal("Code", entry.Segment);
    }
}