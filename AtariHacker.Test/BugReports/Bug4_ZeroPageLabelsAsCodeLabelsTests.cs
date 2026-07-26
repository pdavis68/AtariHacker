using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 4: Zero-page OS variable labels applied to code addresses
///
/// OS zero-page variable labels (like RUNAD, RUNADH, MEMTOP, CBAUD, etc.)
/// are applied as code labels in the disassembly output. Since the code is
/// running at addresses like $0700+, and the zero-page is at $0000-$00FF,
/// these labels should not appear as code labels. They should only appear
/// as operand comments when the code references those addresses.
/// </summary>
public sealed class Bug4_ZeroPageLabelsAsCodeLabelsTests
{
    /// <summary>
    /// When disassembling code at $0700+, zero-page OS variable labels (from
    /// the ZeroPageMap) should NOT appear as code labels. They should only
    /// appear as operand comments.
    /// </summary>
    [Fact]
    public void DisassembleCa65_ZeroPageLabelsDoNotAppearAsCodeLabels()
    {
        // LDA $42 ($A5 $42) - loads from address $0042 (RUNAD in zero-page map)
        // STA $43 ($85 $43) - stores to address $0043 (RUNADH in zero-page map)
        // RTS ($60)
        var data = new byte[]
        {
            0xA5, 0x42,       // LDA $42
            0x85, 0x43,       // STA $43
            0x60              // RTS
        };

        var session = new RomSession { Data = data };
        var symbols = new SymbolTable();
        var zeroPageMap = new ZeroPageMap();

        // Populate with zero-page OS variables (as done in CreateCliSession)
        AtariHardwareMap.PopulateZeroPage(zeroPageMap);

        // Disassemble at address $0700 with ca65 format (no analyze)
        // The data has 5 bytes, so use numBytes=5
        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 5, startAddress: "$0700", format: "ca65");

        // Zero-page labels should NOT appear as code labels (left column)
        // RUNAD is at $0042, which is NOT in our code range ($0700-$0704)
        Assert.DoesNotContain("RUNAD:", result);
        Assert.DoesNotContain("RUNADH:", result);
        Assert.DoesNotContain("INITAD:", result);

        // The instructions should still be disassembled correctly
        Assert.Contains("LDA", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STA", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RTS", result, StringComparison.OrdinalIgnoreCase);

        // The .org directive should be $0700
        Assert.Contains("$0700", result);
    }

    /// <summary>
    /// When disassembling at the zero-page range (file offsets), the auto-generated
    /// labels should be used instead of zero-page OS variable names.
    /// </summary>
    [Fact]
    public void DisassembleAtZeroPageRange_UsesAutoLabelsNotOsVariableNames()
    {
        // Code at file offset 0x42 (which is address 0x0042 when no address override)
        var data = new byte[0x50];
        // LDA #$40 at offset 0x42
        data[0x42] = 0xA9; // LDA #imm
        data[0x43] = 0x40; // #$40
        data[0x44] = 0x60; // RTS

        var session = new RomSession { Data = data };
        var symbols = new SymbolTable();
        var zeroPageMap = new ZeroPageMap();

        AtariHardwareMap.PopulateZeroPage(zeroPageMap);

        // Disassemble at file offset 0x42 (no address override, so address = file offset)
        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0x42", 3, startAddress: null, format: "ca65");

        // The label at address $0042 should NOT be "RUNAD:" (the zero-page OS variable name)
        // It should be an auto-generated label like "L0042:" or similar
        Assert.DoesNotContain("RUNAD:", result);

        // The instruction should still be disassembled correctly
        Assert.Contains("LDA", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RTS", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zero-page labels should appear as operand comments, not as code labels.
    /// When an instruction references a zero-page address, the label should appear
    /// in the comment, not as a code label.
    /// </summary>
    [Fact]
    public void ZeroPageLabelsAppearAsOperandCommentsNotCodeLabels()
    {
        // LDA $42 - loads from address $0042 (RUNAD in zero-page map)
        var data = new byte[]
        {
            0xA5, 0x42,       // LDA $42
            0x60              // RTS
        };

        var session = new RomSession { Data = data };
        var symbols = new SymbolTable();
        var zeroPageMap = new ZeroPageMap();

        AtariHardwareMap.PopulateZeroPage(zeroPageMap);

        // Disassemble with listing format so we can see comments
        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 2, startAddress: "$0700", format: "listing");

        // The zero-page label RUNAD should appear as a comment (operand reference),
        // not as a code label (RUNAD:)
        // The comment text shows "RUNAD" (the label at $0042)
        Assert.Contains("RUNAD", result);
        // But RUNAD should NOT appear as a code label (followed by colon)
        Assert.DoesNotContain("RUNAD:", result);
    }
}