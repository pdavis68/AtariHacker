using AtariHacker.Analysis;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class DataFlowResultTests
{
    [Fact]
    public void DataFlowLink_RecordStoresAllProperties()
    {
        var link = new DataFlowLink(
            0x8000,
            AccessType.Write,
            0x8010,
            AccessType.Read,
            "forward path",
            5);

        Assert.Equal(0x8000, link.FromAddress);
        Assert.Equal(AccessType.Write, link.FromAccess);
        Assert.Equal(0x8010, link.ToAddress);
        Assert.Equal(AccessType.Read, link.ToAccess);
        Assert.Equal("forward path", link.Path);
        Assert.Equal(5, link.InstructionCount);
    }

    [Fact]
    public void DataFlowResult_RecordStoresAllProperties()
    {
        var writes = new List<XRefEntry>();
        var reads = new List<XRefEntry>();
        var chain = new List<DataFlowLink>();

        var result = new DataFlowResult(0xD012, writes, reads, chain);

        Assert.Equal(0xD012, result.TargetAddress);
        Assert.Same(writes, result.Writes);
        Assert.Same(reads, result.Reads);
        Assert.Same(chain, result.Chain);
    }
}