using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class PatternLibraryTests
{
    [Fact]
    public void Add_AddsNewPatternEntry()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "test", Hex = "20 ?? ??", Description = "A test pattern" });
        Assert.Single(lib.Patterns);
        Assert.Equal("test", lib.Patterns[0].Name);
    }

    [Fact]
    public void Add_ThrowsInvalidOperationExceptionForDuplicateName()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "test", Hex = "20 ?? ??" });
        Assert.Throws<InvalidOperationException>(() =>
            lib.Add(new PatternEntry { Name = "test", Hex = "60" }));
    }

    [Fact]
    public void Remove_RemovesPatternByNameAndReturnsTrue()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "test", Hex = "20 ?? ??" });
        var removed = lib.Remove("test");

        Assert.True(removed);
        Assert.Empty(lib.Patterns);
    }

    [Fact]
    public void Remove_ReturnsFalseForNonExistentName()
    {
        var lib = new PatternLibrary();
        var removed = lib.Remove("nonexistent");
        Assert.False(removed);
    }

    [Fact]
    public void Find_FindsPatternByNameCaseInsensitive()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "TestPattern", Hex = "20 ?? ??" });

        var found = lib.Find("testpattern");
        Assert.NotNull(found);
        Assert.Equal("TestPattern", found.Name);

        found = lib.Find("TESTPATTERN");
        Assert.NotNull(found);
    }

    [Fact]
    public void Find_ReturnsNullForNonExistentName()
    {
        var lib = new PatternLibrary();
        var found = lib.Find("nonexistent");
        Assert.Null(found);
    }

    [Fact]
    public void Query_FiltersByTag()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "a", Hex = "20", Tags = new List<string> { "jsr" } });
        lib.Add(new PatternEntry { Name = "b", Hex = "60", Tags = new List<string> { "rts" } });

        var results = lib.Query(tag: "jsr");
        Assert.Single(results);
        Assert.Equal("a", results[0].Name);
    }

    [Fact]
    public void Query_FiltersByCategory()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "a", Hex = "20", Category = "code-patterns" });
        lib.Add(new PatternEntry { Name = "b", Hex = "60", Category = "uncategorized" });

        var results = lib.Query(category: "code-patterns");
        Assert.Single(results);
        Assert.Equal("a", results[0].Name);
    }

    [Fact]
    public void Query_FiltersByTextQueryNameAndDescription()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "jsr-pattern", Hex = "20 ?? ??", Description = "JSR to subroutine" });
        lib.Add(new PatternEntry { Name = "rts", Hex = "60", Description = "Return from subroutine" });

        var results = lib.Query(query: "subroutine");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Query_CombinesMultipleFilters()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "a", Hex = "20", Category = "code", Tags = new List<string> { "jsr" } });
        lib.Add(new PatternEntry { Name = "b", Hex = "60", Category = "code", Tags = new List<string> { "rts" } });
        lib.Add(new PatternEntry { Name = "c", Hex = "00", Category = "data", Tags = new List<string> { "brk" } });

        var results = lib.Query(tag: "jsr", category: "code");
        Assert.Single(results);
        Assert.Equal("a", results[0].Name);
    }

    [Fact]
    public void Query_ReturnsAllPatternsWhenNoFiltersSpecified()
    {
        var lib = new PatternLibrary();
        lib.Add(new PatternEntry { Name = "a", Hex = "20" });
        lib.Add(new PatternEntry { Name = "b", Hex = "60" });

        var results = lib.Query();
        Assert.Equal(2, results.Count);
    }
}