using AtariHacker.Helpers;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 1: `hex-dump` crashes with `--start-address $0000`
///
/// The `$0000` address is being interpreted by the shell as a variable expansion
/// when not properly quoted, but even when the AddressParser receives `$0000`,
/// the zero-address case should be handled correctly. The parser should parse
/// `$0000` as address 0 (0x0000).
/// </summary>
public sealed class Bug5_HexDumpStartAddressZeroTests
{
    [Fact]
    public void AddressParser_ParseAddressDollarZero_ReturnsZero()
    {
        // $0000 should parse as address 0
        var result = AddressParser.ParseAddress("$0000");
        Assert.Equal(0, result);
    }

    [Fact]
    public void AddressParser_ParseAddressDollarZeroZero_ReturnsZero()
    {
        // $0 should also parse as address 0
        var result = AddressParser.ParseAddress("$0");
        Assert.Equal(0, result);
    }

    [Fact]
    public void AddressParser_ParseAddressZeroXZero_ReturnsZero()
    {
        // 0x0000 should also parse as address 0
        var result = AddressParser.ParseAddress("0x0000");
        Assert.Equal(0, result);
    }

    [Fact]
    public void HexDump_WithStartAddressZero_DoesNotCrash()
    {
        // Create a session with some data
        var data = new byte[] { 0xA9, 0x00, 0x60, 0xA9, 0x01, 0x60 };
        var session = new RomSession { Data = data };

        // Hex dump with startAddress=$0000 should work without crashing
        var result = HexDumpTool.HexDump(session, "0", 6, "$0000");

        // Should not contain an error message
        Assert.DoesNotContain("ERROR", result, StringComparison.OrdinalIgnoreCase);
        // Should contain the hex dump header
        Assert.Contains("Offset", result);
        // The address column should show $0000 as the first address
        Assert.Contains("$0000", result);
    }

    [Fact]
    public void HexDump_WithStartAddressZeroAndNonZeroOffset_WorksCorrectly()
    {
        // Create data with known pattern
        var data = new byte[] { 0xEA, 0xEA, 0xA9, 0x42, 0x60 };
        var session = new RomSession { Data = data };

        // Hex dump at offset 2 with startAddress=$0000
        // offset 2 in file = address $0002 in memory
        var result = HexDumpTool.HexDump(session, "2", 3, "$0000");

        // Should not error
        Assert.DoesNotContain("ERROR", result, StringComparison.OrdinalIgnoreCase);
        // Should contain the expected bytes
        Assert.Contains("A9", result);
        Assert.Contains("42", result);
    }

    [Fact]
    public void HexDump_WithStartAddressZeroAndOffsetZero_ShowsAddressZero()
    {
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var session = new RomSession { Data = data };

        var result = HexDumpTool.HexDump(session, "0", 4, "$0000");

        // The first row should show address $0000
        Assert.Contains("$0000", result);
        // Should show the bytes
        Assert.Contains("00", result);
        Assert.Contains("01", result);
    }
}