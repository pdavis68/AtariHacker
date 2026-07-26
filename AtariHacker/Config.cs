using System.Text.Json;

namespace AtariHacker;

/// <summary>
/// Configuration loaded from .atari-hacker.config in the current directory.
/// </summary>
public sealed class CliConfig
{
    private const string ConfigFileName = ".atari-hacker.config";

    /// <summary>
    /// Path to the target file (ATR, ROM, XEX, etc.).
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Load configuration from the specified path, or search up the directory tree.
    /// </summary>
    public static CliConfig? Load(string? configPath = null)
    {
        var path = configPath;
        if (path is null)
        {
            // Search upward from current directory
            var dir = Directory.GetCurrentDirectory();
            while (dir is not null)
            {
                var candidate = Path.Combine(dir, ConfigFileName);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        if (path is null || !File.Exists(path))
            return null;

        try
        {
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CliConfig>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            Console.Error.WriteLine($"Warning: Failed to parse config file: {path}");
            return null;
        }
    }

    /// <summary>
    /// Save configuration to the specified path (default: .atari-hacker.config in current directory).
    /// </summary>
    public void Save(string? configPath = null)
    {
        var path = configPath ?? Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Resolve the target file path: CLI override takes priority, then config file.
    /// </summary>
    public static string? ResolveTarget(string? cliTarget, CliConfig? config)
    {
        return cliTarget ?? config?.Target;
    }
}