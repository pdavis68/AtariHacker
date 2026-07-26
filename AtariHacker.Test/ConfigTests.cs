using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class ConfigTests
{
    [Fact]
    public void Load_ReturnsNullWhenNoConfigFileExists()
    {
        var config = CliConfig.Load("/nonexistent/path/.atari-hacker.config");
        Assert.Null(config);
    }

    [Fact]
    public void ResolveTarget_CliTargetTakesPriorityOverConfig()
    {
        var config = new CliConfig { Target = "/path/to/config.rom" };
        var result = CliConfig.ResolveTarget("/path/to/cli.rom", config);
        Assert.Equal("/path/to/cli.rom", result);
    }

    [Fact]
    public void ResolveTarget_ReturnsConfigTargetWhenNoCliTarget()
    {
        var config = new CliConfig { Target = "/path/to/config.rom" };
        var result = CliConfig.ResolveTarget(null, config);
        Assert.Equal("/path/to/config.rom", result);
    }

    [Fact]
    public void ResolveTarget_ReturnsNullWhenNeitherIsSet()
    {
        var result = CliConfig.ResolveTarget(null, null);
        Assert.Null(result);
    }

    [Fact]
    public void Save_WritesConfigToSpecifiedPath()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var config = new CliConfig { Target = "/test.rom" };
            config.Save(tempPath);

            Assert.True(File.Exists(tempPath));
            var content = File.ReadAllText(tempPath);
            Assert.Contains("/test.rom", content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}