using AtariHacker.Helpers;

namespace AtariHacker.Test;

public sealed class OutputFormatterTests
{
    [Fact]
    public void FormatCsv_ProducesCorrectCsvWithHeaderRow()
    {
        var headers = new[] { "name", "value" };
        var rows = new[] { new[] { "foo", "42" }, new[] { "bar", "99" } };

        var csv = OutputFormatter.FormatCsv(headers, rows);
        var lines = csv.Trim().Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("name,value", lines[0]);
        Assert.Equal("foo,42", lines[1]);
        Assert.Equal("bar,99", lines[2]);
    }

    [Fact]
    public void FormatCsv_EscapesCommasAndQuotes()
    {
        var headers = new[] { "name", "desc" };
        var rows = new[] { new[] { "test", "has, comma" }, new[] { "test2", "has \"quote\"" } };

        var csv = OutputFormatter.FormatCsv(headers, rows);
        Assert.Contains("\"has, comma\"", csv);
        Assert.Contains("\"has \"\"quote\"\"\"", csv);
    }

    [Fact]
    public void FormatTsv_ProducesCorrectTsvWithHeaderRow()
    {
        var headers = new[] { "name", "value" };
        var rows = new[] { new[] { "foo", "42" } };

        var tsv = OutputFormatter.FormatTsv(headers, rows);
        var lines = tsv.Trim().Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("name\tvalue", lines[0]);
        Assert.Equal("foo\t42", lines[1]);
    }

    [Fact]
    public void FormatTsv_ReplacesTabsAndNewlinesWithSpaces()
    {
        var headers = new[] { "name" };
        var rows = new[] { new[] { "has\ttab\nand\rnewline" } };

        var tsv = OutputFormatter.FormatTsv(headers, rows);
        Assert.Contains("has tab and newline", tsv);
        Assert.DoesNotContain("\t", tsv);
    }

    [Fact]
    public void FormatKv_ProducesCorrectKeyValuePairs()
    {
        var keys = new[] { "name", "value" };
        var rows = new[] { new[] { "foo", "42" } };

        var kv = OutputFormatter.FormatKv(keys, rows);
        Assert.Contains("name=foo", kv);
        Assert.Contains("value=42", kv);
    }

    [Fact]
    public void FormatKv_SeparatesRowsWithBlankLines()
    {
        var keys = new[] { "name" };
        var rows = new[] { new[] { "foo" }, new[] { "bar" } };

        var kv = OutputFormatter.FormatKv(keys, rows);
        Assert.Contains("name=foo\n\nname=bar", kv);
    }

    [Fact]
    public void AllFormatters_HandleEmptyRowsGracefully()
    {
        var headers = new[] { "name" };
        var rows = Array.Empty<string[]>();

        Assert.Equal("name\n", OutputFormatter.FormatCsv(headers, rows));
        Assert.Equal("name\n", OutputFormatter.FormatTsv(headers, rows));
        Assert.Equal("", OutputFormatter.FormatKv(headers, rows));
    }
}
