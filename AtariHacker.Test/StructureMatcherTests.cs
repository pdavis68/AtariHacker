using AtariHacker.Analysis;
using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class StructureMatcherTests
{
    [Fact]
    public void MatchAll_ReturnsEmptyListForEmptyTemplates()
    {
        var data = new byte[100];
        var results = StructureMatcher.MatchAll(data, 0, 0, 50, new List<StructureTemplate>());
        Assert.Empty(results);
    }

    [Fact]
    public void MatchAll_MatchesSimpleByteTemplate()
    {
        var data = new byte[100];
        var template = new StructureTemplate
        {
            Name = "test",
            Fields = new List<StructureField>
            {
                new() { Name = "field1", Offset = 0, Type = "byte" },
                new() { Name = "field2", Offset = 1, Type = "byte" }
            }
        };

        var results = StructureMatcher.MatchAll(data, 0, 0, 50, new List<StructureTemplate> { template });
        Assert.NotEmpty(results);
    }

    [Fact]
    public void MatchAll_SortsResultsByConfidenceDescending()
    {
        var data = new byte[100];
        var template = new StructureTemplate
        {
            Name = "test",
            Fields = new List<StructureField>
            {
                new() { Name = "val", Offset = 0, Type = "byte" }
            }
        };

        var results = StructureMatcher.MatchAll(data, 0, 0, 50, new List<StructureTemplate> { template });
        Assert.NotEmpty(results);
        for (var i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Confidence >= results[i].Confidence);
    }

    [Fact]
    public void MatchTemplate_ScansRangeAndFindsAllMatches()
    {
        var data = new byte[100];
        var template = new StructureTemplate
        {
            Name = "test",
            Fields = new List<StructureField>
            {
                new() { Name = "val", Offset = 0, Type = "byte" }
            }
        };

        var results = StructureMatcher.MatchTemplate(data, 0, 0, 50, template);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void MatchTemplate_RespectsStepParameter()
    {
        var data = new byte[100];
        var template = new StructureTemplate
        {
            Name = "test",
            Fields = new List<StructureField>
            {
                new() { Name = "val", Offset = 0, Type = "byte" }
            }
        };

        var results = StructureMatcher.MatchTemplate(data, 0, 0, 50, template, step: 10);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void ComputeTemplateSize_ComputesCorrectTotalSize()
    {
        var template = new StructureTemplate
        {
            Name = "test",
            Fields = new List<StructureField>
            {
                new() { Name = "a", Offset = 0, Type = "byte" },
                new() { Name = "b", Offset = 1, Type = "word_le" },
                new() { Name = "c", Offset = 3, Type = "byte" }
            }
        };

        var size = StructureMatcher.ComputeTemplateSize(template);
        Assert.Equal(4, size);
    }
}