using AtariHacker.Helpers;

namespace AtariHacker.State;

public enum SegmentType
{
    Code,
    Data,
    Graphics,
    Text,
    ZeroPage
}

public sealed record SegmentDefinition(
    string Name,
    SegmentType Type,
    ushort Start,
    ushort End,
    string? Comment = null);

public sealed class SegmentManager
{
    private readonly List<SegmentDefinition> _segments = new();

    public IReadOnlyList<SegmentDefinition> Segments => _segments.AsReadOnly();

    /// <summary>
    /// Define a new segment. If a segment with the same name exists, it is replaced.
    /// </summary>
    public void Define(SegmentDefinition segment)
    {
        var existing = _segments.FindIndex(s => s.Name.Equals(segment.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            _segments[existing] = segment;
        }
        else
        {
            _segments.Add(segment);
        }
    }

    /// <summary>
    /// Remove a segment by name.
    /// </summary>
    public void Remove(string name)
    {
        _segments.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Remove all segments.
    /// </summary>
    public void Clear()
    {
        _segments.Clear();
    }

    /// <summary>
    /// Return the segment type for the given address, or null if the address is not in any segment.
    /// </summary>
    public SegmentType? Classify(ushort address)
    {
        foreach (var segment in _segments)
        {
            if (address >= segment.Start && address <= segment.End)
            {
                return segment.Type;
            }
        }
        return null;
    }

    /// <summary>
    /// Check if the given address falls within the named segment.
    /// </summary>
    public bool IsAddressInRange(ushort address, string segmentName)
    {
        foreach (var segment in _segments)
        {
            if (segment.Name.Equals(segmentName, StringComparison.OrdinalIgnoreCase)
                && address >= segment.Start && address <= segment.End)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Return the segment name for the given address, or null if not in any segment.
    /// </summary>
    public string? GetSegmentName(ushort address)
    {
        foreach (var segment in _segments)
        {
            if (address >= segment.Start && address <= segment.End)
            {
                return segment.Name;
            }
        }
        return null;
    }

    /// <summary>
    /// Check if any segments overlap (same address range covered by multiple segments).
    /// </summary>
    public bool HasOverlaps(out string? overlapDescription)
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            for (var j = i + 1; j < _segments.Count; j++)
            {
                var a = _segments[i];
                var b = _segments[j];
                var overlap = a.Start <= b.End && b.Start <= a.End;
                if (overlap)
                {
                    overlapDescription = $"Segments '{a.Name}' ({Formatting.HexWord(a.Start)}-{Formatting.HexWord(a.End)}) and '{b.Name}' ({Formatting.HexWord(b.Start)}-{Formatting.HexWord(b.End)}) overlap.";
                    return true;
                }
            }
        }
        overlapDescription = null;
        return false;
    }

    /// <summary>
    /// Find gaps between segments (address ranges not covered by any segment).
    /// </summary>
    public IReadOnlyList<(ushort Start, ushort End)> FindGaps()
    {
        if (_segments.Count == 0)
        {
            return Array.Empty<(ushort, ushort)>();
        }

        var sorted = _segments.OrderBy(s => s.Start).ToList();
        var gaps = new List<(ushort, ushort)>();
        ushort? previousEnd = null;

        foreach (var segment in sorted)
        {
            if (previousEnd is not null && previousEnd.Value + 1 < segment.Start)
            {
                gaps.Add(((ushort)(previousEnd.Value + 1), (ushort)(segment.Start - 1)));
            }
            if (previousEnd is null || segment.End > previousEnd)
            {
                previousEnd = segment.End;
            }
        }

        return gaps;
    }
}