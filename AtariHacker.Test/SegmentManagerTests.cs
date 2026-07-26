using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class SegmentManagerTests
{
    [Fact]
    public void Define_AddsNewSegment()
    {
        var mgr = new SegmentManager();
        var seg = new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF);
        mgr.Define(seg);

        Assert.Single(mgr.Segments);
        Assert.Equal("Code", mgr.Segments[0].Name);
    }

    [Fact]
    public void Define_ReplacesExistingSegmentWithSameName()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x9000, (ushort)0x90FF));

        Assert.Single(mgr.Segments);
        Assert.Equal((ushort)0x9000, mgr.Segments[0].Start);
    }

    [Fact]
    public void Remove_RemovesSegmentByName()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Remove("Code");

        Assert.Empty(mgr.Segments);
    }

    [Fact]
    public void Remove_DoesNothingForNonExistentName()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Remove("NonExistent");

        Assert.Single(mgr.Segments);
    }

    [Fact]
    public void Clear_RemovesAllSegments()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Define(new SegmentDefinition("Data", SegmentType.Data, (ushort)0x8100, (ushort)0x81FF));
        mgr.Clear();

        Assert.Empty(mgr.Segments);
    }

    [Fact]
    public void Classify_ReturnsCorrectSegmentTypeForAddress()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Define(new SegmentDefinition("Data", SegmentType.Data, (ushort)0x8100, (ushort)0x81FF));

        Assert.Equal(SegmentType.Code, mgr.Classify((ushort)0x8040));
        Assert.Equal(SegmentType.Data, mgr.Classify((ushort)0x8150));
    }

    [Fact]
    public void Classify_ReturnsNullForAddressNotInAnySegment()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));

        Assert.Null(mgr.Classify((ushort)0x9000));
    }

    [Fact]
    public void IsAddressInRange_ReturnsTrueForAddressInNamedSegment()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));

        Assert.True(mgr.IsAddressInRange((ushort)0x8040, "Code"));
    }

    [Fact]
    public void IsAddressInRange_ReturnsFalseForAddressNotInNamedSegment()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));

        Assert.False(mgr.IsAddressInRange((ushort)0x9000, "Code"));
    }

    [Fact]
    public void GetSegmentName_ReturnsCorrectSegmentName()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));

        Assert.Equal("Code", mgr.GetSegmentName((ushort)0x8040));
    }

    [Fact]
    public void GetSegmentName_ReturnsNullForAddressNotInAnySegment()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Code", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));

        Assert.Null(mgr.GetSegmentName((ushort)0x9000));
    }

    [Fact]
    public void HasOverlaps_DetectsOverlappingSegments()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Seg1", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Define(new SegmentDefinition("Seg2", SegmentType.Data, (ushort)0x80A0, (ushort)0x81FF));

        Assert.True(mgr.HasOverlaps(out _));
    }

    [Fact]
    public void HasOverlaps_ReturnsFalseForNonOverlappingSegments()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Seg1", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Define(new SegmentDefinition("Seg2", SegmentType.Data, (ushort)0x8100, (ushort)0x81FF));

        Assert.False(mgr.HasOverlaps(out _));
    }

    [Fact]
    public void FindGaps_FindsGapsBetweenSegments()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("Seg1", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Define(new SegmentDefinition("Seg2", SegmentType.Code, (ushort)0x8200, (ushort)0x82FF));

        var gaps = mgr.FindGaps();
        Assert.NotEmpty(gaps);
        Assert.Equal((ushort)0x8100, gaps[0].Start);
        Assert.Equal((ushort)0x81FF, gaps[0].End);
    }

    [Fact]
    public void FindGaps_ReturnsEmptyListForNoSegments()
    {
        var mgr = new SegmentManager();
        var gaps = mgr.FindGaps();
        Assert.Empty(gaps);
    }

    [Fact]
    public void GetOrderedSegments_ReturnsSortedByStartThenName()
    {
        var mgr = new SegmentManager();
        mgr.Define(new SegmentDefinition("A", SegmentType.Code, (ushort)0x8000, (ushort)0x80FF));
        mgr.Define(new SegmentDefinition("B", SegmentType.Code, (ushort)0x8100, (ushort)0x81FF));

        var ordered = mgr.GetOrderedSegments().ToList();
        Assert.Equal("A", ordered[0].Name);
    }
}