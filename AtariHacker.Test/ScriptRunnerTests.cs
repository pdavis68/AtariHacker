using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class ScriptRunnerTests
{
    [Fact]
    public void RunScript_ReturnsErrorForNonExistentScriptFile()
    {
        var session = new RomSession();
        var result = ScriptRunner.RunScript(session, new SymbolTable(), new ZeroPageMap(), null!, new SegmentManager(), "/nonexistent/script.txt");
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void RunScript_ExecutesCommandsFromScriptFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "# comment\nhelp\n");
            var session = new RomSession();
            var result = ScriptRunner.RunScript(session, new SymbolTable(), new ZeroPageMap(), null!, new SegmentManager(), tempFile);
            Assert.Contains("Script executed", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void RunScript_SkipsCommentsAndBlankLines()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "# this is a comment\n\n  \nhelp\n");
            var session = new RomSession();
            var result = ScriptRunner.RunScript(session, new SymbolTable(), new ZeroPageMap(), null!, new SegmentManager(), tempFile);
            Assert.Contains("Script executed", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void RunScript_StopsOnFirstError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "invalid_command_xyz\nhelp\n");
            var session = new RomSession();
            var result = ScriptRunner.RunScript(session, new SymbolTable(), new ZeroPageMap(), null!, new SegmentManager(), tempFile);
            Assert.Contains("ERROR", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
