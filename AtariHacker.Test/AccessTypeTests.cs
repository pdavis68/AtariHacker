using AtariHacker.Analysis;

namespace AtariHacker.Test;

public sealed class AccessTypeTests
{
    [Fact]
    public void EnumValuesAreCorrectlyDefined()
    {
        Assert.Equal(0, (int)AccessType.Read);
        Assert.Equal(1, (int)AccessType.Write);
        Assert.Equal(2, (int)AccessType.ReadWrite);
        Assert.Equal(3, (int)AccessType.Execute);
    }

    [Fact]
    public void AllValuesAreDistinct()
    {
        var values = Enum.GetValues<AccessType>();
        Assert.Equal(4, values.Length);
        Assert.Equal(4, values.Distinct().Count());
    }
}