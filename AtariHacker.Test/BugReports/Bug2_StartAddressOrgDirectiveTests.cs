using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 2: `--start-address` value is not applied to `.org` directive in ca65 output
///
/// The `--start-address` parameter is documented as "Override the memory start address"
/// but is not applied to the `.org` directive in the ca65 output format. The `.org`
/// always shows `$0000` regardless of the `--start-address` value.
/// </summary>
public sealed class Bug2_StartAddressOrgDirectiveTests
{
    [Fact]
    public void DisassembleCa65_WithStartAddress_EmitsCorrectOrgDirective()
    {
        // Simple binary: LDA #$40, RTS
        var data = new byte[] { 0xA9, 0x40, 0x60 };
        var session = new RomSession { Data = data };

        var result = DisassemblerTool.Disassemble(
            session, new SymbolTable(), new ZeroPageMap(),
            "0", 3, startAddress: "$0700", format: "ca65");

        // The .org directive should show $0700, not $0000
        Assert.Contains(".org", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$0700", result);
        // Should NOT contain $0000 as the .org value
        // (The address field in the listing might show $0000 for other reasons,
        // but the .org directive should be $0700)
        Assert.DoesNotContain("\t.org\t$0000", result);
    }

    [Fact]
    public void DisassembleCa65_WithoutStartAddress_ShowsFileOffset()
    {
        // Without startAddress, the .org should show the file offset (0 = $0000)
        var data = new byte[] { 0xA9, 0x40, 0x60 };
        var session = new RomSession { Data = data };

        var result = DisassemblerTool.Disassemble(
            session, new SymbolTable(), new ZeroPageMap(),
            "0", 3, startAddress: null, format: "ca65");

        // The .org directive should show $0000 (file offset 0)
        Assert.Contains(".org", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$0000", result);
    }

    [Fact]
    public void DisassembleCa65_WithStartAddressAndAnalyze_EmitsCorrectOrgDirective()
    {
        // With --analyze, the .org directive should still show the correct value
        var data = new byte[] { 0xA9, 0x40, 0x60 };
        var session = new RomSession { Data = data };

        var result = DisassemblerTool.Disassemble(
            session, new SymbolTable(), new ZeroPageMap(),
            "0", 3, startAddress: "$0700", format: "ca65", analyze: true);

        // The .org directive should show $0700, not $0000
        Assert.Contains(".org", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$0700", result);
        Assert.DoesNotContain("\t.org\t$0000", result);
    }
}