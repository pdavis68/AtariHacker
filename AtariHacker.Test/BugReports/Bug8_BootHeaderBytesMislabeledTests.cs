using AtariHacker.Analysis;
using AtariHacker.Atari;
using AtariHacker.Helpers;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 4: Boot header bytes mislabeled in disassembly
///
/// The boot header (6 bytes at $0700-$0705: $D0 $03 $00 $07 $40 $15) is being
/// disassembled with incorrect labels and comments:
///
/// 1. Labels like `data_0702` skip address $0701 (the sector count byte).
/// 2. Comments like `; CBAUD` on $4C are wrong — $4C is the JMP opcode, not a
///    hardware register reference. The comment should reference the boot header
///    field, not a hardware register name.
/// 3. The boot header should be treated as a single structural unit, not individual
///    bytes with auto-generated labels.
/// </summary>
public sealed class Bug8_BootHeaderBytesMislabeledTests
{
    /// <summary>
    /// Create a boot header + code:
    /// Header: $D0 $03 $00 $07 $40 $15 (standard 6-byte boot header)
    /// Code: LDA #$00 ($A9 $00), STA $D201 ($8D $01 $D2), RTS ($60)
    /// </summary>
    private static (RomSession Session, SymbolTable Symbols, ZeroPageMap ZeroPage) CreateBootHeaderRom()
    {
        var data = new byte[]
        {
            0xD0, 0x03,       // Boot flag, sector count
            0x00, 0x07,       // Load address $0700 (little-endian)
            0x40, 0x15,       // Init address $1540 (little-endian)
            0xA9, 0x00,       // LDA #$00
            0x8D, 0x01, 0xD2, // STA $D201 (AUDC1)
            0x60              // RTS
        };

        var session = new RomSession { Data = data, BaseAddress = 0x0700 };
        var symbols = new SymbolTable();
        AtariHardwareMap.Populate(symbols);
        var zeroPageMap = new ZeroPageMap();
        AtariHardwareMap.PopulateZeroPage(zeroPageMap);

        return (session, symbols, zeroPageMap);
    }

    [Fact]
    public void DisassembleWithAnalyze_NoCbaudCommentOnBootHeader()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        // Disassemble with --analyze in ca65 format
        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 11, startAddress: "$0700", format: "ca65", analyze: true);

        // Bug: The comment "; CBAUD" should NOT appear on boot header bytes
        // $4C is the opcode for JMP, not a hardware register reference
        Assert.DoesNotContain("CBAUD", result);
    }

    [Fact]
    public void DisassembleWithAnalyze_NoHardwareRegisterLabelsOnBootHeader()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 12, startAddress: "$0700", format: "ca65", analyze: true);

        // The boot header bytes should be emitted as .byte directives, not as
        // instructions with hardware register comments. AUDC1 is a valid hardware
        // register label that appears as an operand comment on STA $D201, which
        // is correct behavior. The test verifies the boot header bytes themselves
        // don't have misleading labels.
        Assert.Contains(".byte", result);
        // The boot header bytes should NOT contain BNE (which would be the
        // disassembly of $D0 $03 as code)
        Assert.DoesNotContain("BNE", result);
        // Code after the header should be properly disassembled
        Assert.Contains("LDA", result);
        Assert.Contains("STA", result);
        Assert.Contains("RTS", result);
    }

    [Fact]
    public void DisassembleWithAnalyze_BootHeaderAddressesAreDataReferences()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        // Run the analyzer
        var references = DisassemblyAnalyzer.Analyze(
            session.Data!, session.Segments, session.BaseAddress);

        // All 6 boot header bytes should be in absolute data references
        for (ushort addr = 0x0700; addr < 0x0706; addr++)
        {
            Assert.Contains(addr, references.AbsoluteDataReferences);
        }
    }

    [Fact]
    public void DisassembleWithAnalyze_BootHeaderBytesAreDataRegions()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        var references = DisassemblyAnalyzer.Analyze(
            session.Data!, session.Segments, session.BaseAddress);
        var (codeRegions, dataRegions) = DisassemblyAnalyzer.TraceCodeRegions(
            session.Data!, references, session.Segments, session.BaseAddress);

        // All 6 boot header bytes should be in data regions
        for (ushort addr = 0x0700; addr < 0x0706; addr++)
        {
            Assert.Contains(addr, dataRegions);
        }

        // Code after the header should be in code regions
        // LDA #$00 at $0706 (2 bytes), STA $D201 at $0708 (3 bytes), RTS at $070B (1 byte)
        Assert.Contains((ushort)0x0706, codeRegions);
        Assert.Contains((ushort)0x0707, codeRegions);
        Assert.Contains((ushort)0x0708, codeRegions);
        Assert.Contains((ushort)0x0709, codeRegions);
        Assert.Contains((ushort)0x070A, codeRegions);
        Assert.Contains((ushort)0x070B, codeRegions);
    }

    [Fact]
    public void DisassembleWithAnalyze_BootHeaderEmitsAsByteDirectives()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 12, startAddress: "$0700", format: "ca65", analyze: true);

        // The boot header should be emitted as .byte directives
        Assert.Contains(".byte", result);
        // Should NOT contain BNE (which would be the disassembly of $D0 $03)
        Assert.DoesNotContain("BNE", result);
        // Should contain the code after the header
        Assert.Contains("LDA", result);
        Assert.Contains("STA", result);
        Assert.Contains("RTS", result);
    }

    [Fact]
    public void DisassembleWithAnalyze_StructuredBootHeaderOutput()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 12, startAddress: "$0700", format: "ca65", analyze: true);

        // Should contain "Boot header" comment
        Assert.Contains("Boot header", result);
        // Should contain boot flag with description
        Assert.Contains("Boot flag:", result);
        // Should contain sector count
        Assert.Contains("Sectors to load:", result);
        // Should contain load address
        Assert.Contains("Load address:", result);
        // Should contain init address
        Assert.Contains("Init address:", result);
    }

    [Fact]
    public void DisassembleWithAnalyze_NoDataLabelsOnStructuredBootHeader()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 12, startAddress: "$0700", format: "ca65", analyze: true);

        // Should NOT contain auto-generated data labels for boot header addresses
        Assert.DoesNotContain("data_0700", result);
        Assert.DoesNotContain("data_0701", result);
        Assert.DoesNotContain("data_0702", result);
        Assert.DoesNotContain("data_0703", result);
        Assert.DoesNotContain("data_0704", result);
        Assert.DoesNotContain("data_0705", result);
    }

    [Fact]
    public void DisassembleWithAnalyze_BootHeaderFormatMatchesExpectedOutput()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();

        var result = DisassemblerTool.Disassemble(
            session, symbols, zeroPageMap,
            "0", 12, startAddress: "$0700", format: "ca65", analyze: true);

        // The boot header should be emitted as structured .byte/.word directives
        // with inline comments describing each field
        Assert.Contains(".byte\t$D0", result);
        Assert.Contains(".byte\t$03", result);
        Assert.Contains(".word\t$0700", result);
        Assert.Contains(".word\t$1540", result);
    }

    [Fact]
    public void AnalyzeAndDisassemble_BootHeaderDataLabelsExist()
    {
        var (session, symbols, zeroPageMap) = CreateBootHeaderRom();
        var segmentManager = new SegmentManager();

        // Use analyze-disassemble path
        var result = AnalysisTools.AnalyzeAndDisassemble(
            session, symbols, zeroPageMap, segmentManager,
            "0", 12, "ca65", null);

        // The boot header bytes should be data, not instructions
        Assert.DoesNotContain("BNE", result);
        Assert.DoesNotContain("BRK", result);
        // Code after header should be disassembled
        Assert.Contains("LDA", result);
        Assert.Contains("STA", result);
        Assert.Contains("RTS", result);
    }
}