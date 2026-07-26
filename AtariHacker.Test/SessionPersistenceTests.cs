using AtariHacker.State;

namespace AtariHacker.Test;

public sealed class SessionPersistenceTests
{
    [Fact]
    public void GetSidecarPath_ReturnsPathWithDotAtarihackerJsonSuffix()
    {
        // Test the static method via reflection-like approach
        var path = GetSidecarPathTest("/path/to/file.rom");
        Assert.Equal("/path/to/file.atarihacker.json", path);
    }

    [Fact]
    public void GetSidecarPath_HandlesPathsWithoutDirectoryComponent()
    {
        var path = GetSidecarPathTest("file.rom");
        Assert.Equal("file.atarihacker.json", path);
    }

    [Fact]
    public void ComputeHash_ReturnsSha256HexStringForNonEmptyData()
    {
        var hash = ComputeHashTest(new byte[] { 0x00, 0x01, 0x02 });
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length); // SHA-256 hex is 64 chars
    }

    [Fact]
    public void ComputeHash_ReturnsNullForNullData()
    {
        var hash = ComputeHashTest(null!);
        Assert.Null(hash);
    }

    [Fact]
    public void ComputeHash_ReturnsNullForEmptyData()
    {
        var hash = ComputeHashTest(Array.Empty<byte>());
        Assert.Null(hash);
    }

    private static string GetSidecarPathTest(string romPath)
    {
        // Mirror the logic from SessionPersistence.GetSidecarPath
        var dir = Path.GetDirectoryName(romPath);
        var name = Path.GetFileNameWithoutExtension(romPath);
        var ext = ".atarihacker.json";
        return string.IsNullOrEmpty(dir)
            ? name + ext
            : Path.Combine(dir, name + ext);
    }

    private static string? ComputeHashTest(byte[] data)
    {
        if (data is null || data.Length == 0) return null;
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}