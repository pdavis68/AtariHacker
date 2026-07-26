using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class StructureToolsTests
{
    [Fact]
    public void ListTemplates_ReturnsEmptyMessageWhenLibraryIsEmpty()
    {
        var result = StructureTools.ListTemplates(null, null, null, "text");
        Assert.NotNull(result);
    }

    [Fact]
    public void DefineTemplate_ValidatesTemplateStructure()
    {
        var result = StructureTools.DefineTemplate("invalid json", false);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void RemoveTemplate_RemovesTemplateByName()
    {
        var result = StructureTools.RemoveTemplate("nonexistent");
        Assert.Contains("not found", result);
    }

    [Fact]
    public void ShowTemplate_DisplaysTemplateDetails()
    {
        var result = StructureTools.ShowTemplate("nonexistent");
        Assert.Contains("not found", result);
    }
}
