using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class StructureTemplateTests
{
    [Fact]
    public void StructureLibrary_Add_AddsNewTemplate()
    {
        var lib = new StructureLibrary();
        lib.Add(new StructureTemplate { Name = "test", Description = "A test template" });
        Assert.Single(lib.Templates);
        Assert.Equal("test", lib.Templates[0].Name);
    }

    [Fact]
    public void StructureLibrary_Add_ThrowsInvalidOperationExceptionForDuplicateName()
    {
        var lib = new StructureLibrary();
        lib.Add(new StructureTemplate { Name = "test" });
        Assert.Throws<InvalidOperationException>(() =>
            lib.Add(new StructureTemplate { Name = "test" }));
    }

    [Fact]
    public void StructureLibrary_Remove_RemovesTemplateByName()
    {
        var lib = new StructureLibrary();
        lib.Add(new StructureTemplate { Name = "test" });
        lib.Remove("test");
        Assert.Empty(lib.Templates);
    }

    [Fact]
    public void StructureLibrary_Find_FindsTemplateByNameCaseInsensitive()
    {
        var lib = new StructureLibrary();
        lib.Add(new StructureTemplate { Name = "MyTemplate" });

        var found = lib.Find("mytemplate");
        Assert.NotNull(found);
        Assert.Equal("MyTemplate", found.Name);
    }

    [Fact]
    public void StructureLibrary_Query_FiltersByTag()
    {
        var lib = new StructureLibrary();
        lib.Add(new StructureTemplate { Name = "a", Tags = new List<string> { "game" } });
        lib.Add(new StructureTemplate { Name = "b", Tags = new List<string> { "disk" } });

        var results = lib.Query(tag: "game");
        Assert.Single(results);
        Assert.Equal("a", results[0].Name);
    }

    [Fact]
    public void StructureLibrary_Query_FiltersByCategory()
    {
        var lib = new StructureLibrary();
        lib.Add(new StructureTemplate { Name = "a", Category = "game-templates" });
        lib.Add(new StructureTemplate { Name = "b", Category = "disk-structures" });

        var results = lib.Query(category: "game-templates");
        Assert.Single(results);
    }

    [Fact]
    public void StructureLibrary_Query_FiltersByText()
    {
        var lib = new StructureLibrary();
        lib.Add(new StructureTemplate { Name = "player-struct", Description = "Player data" });
        lib.Add(new StructureTemplate { Name = "enemy-struct", Description = "Enemy data" });

        var results = lib.Query(query: "player");
        Assert.Single(results);
    }

    [Fact]
    public void StructureMatch_StoresAllPropertiesCorrectly()
    {
        var match = new StructureMatch
        {
            TemplateName = "Test",
            Address = 0x8000,
            Confidence = 0.95,
            FieldValues = new Dictionary<string, object> { { "x", 42 } }
        };

        Assert.Equal("Test", match.TemplateName);
        Assert.Equal(0x8000, match.Address);
        Assert.Equal(0.95, match.Confidence);
        Assert.Single(match.FieldValues);
    }
}