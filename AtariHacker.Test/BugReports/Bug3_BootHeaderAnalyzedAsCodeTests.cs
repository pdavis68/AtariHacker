using AtariHacker.Analysis;
using AtariHacker.Helpers;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 3: Boot sector header (6 bytes) disassembled as code despite `--analyze`
///
/// The 6-byte boot sector header ($D0 $03 $00 $07 $40 $15) is disassembled as
/// 6502 code instead of being emitted as .byte data directives, even with the
/// --analyze flag. The header bytes happen to decode as valid 6502 opcodes.
/// NOTE: The last header byte ($15 = ORA zp,X) is a 2-byte instruction that
/// would consume the first code byte. We use $02 (illegal) instead to ensure
/// the code after the header is properly aligned for testing.
/// </summary>
public sealed class Bug3_BootHeaderAnalyzedAsCodeTests
{
    /// <summary>
    /// Boot sector header bytes: $D0 $03 $00 $07 $40 $02 (using $02 for last byte
    /// since $15 = ORA zp,X consumes 2 bytes and would eat into the code).
    /// These decode as:
    ///   $D0 = BNE (relative branch)
    ///   $03 = BRK
    ///   $00 = BRK
    ///   $07 = .byte (data)
    ///   $40 = RTI
    ///   $02 = illegal opcode (data)
    ///
    /// But they should be emitted as .byte data when --analyze is used.
    /// </summary>
    [Fact]
    public void DisassembleWithAnalyze_EmitsBootHeaderAsData()
    {
        // Boot sector header bytes followed by valid 6502 code
        // Header: $D0 $03 $00 $07 $40 $02 (illegal opcode for last byte)
        // Code: LDA #$00 ($A9 $00), STA $D201 ($8D $01 $D2), RTS ($60)
        var data = new byte[]
        {
            0xD0, 0x03,       // BNE +3 (boot flag, sector count)
            0x00, 0x07,       // load address $0700
            0x40, 0x02,       // init address $0240 (0x02 is illegal opcode, 1 byte)
            0xA9, 0x00,       // LDA #$00
            0x8D, 0x01, 0xD2, // STA $D201 (AUDC1)
            0x60              // RTS
        };

        var session = new RomSession { Data = data, BaseAddress = 0x0700 };

        // With --analyze, the boot sector header should be emitted as .byte data
        var result = DisassemblerTool.Disassemble(
            session, new SymbolTable(), new ZeroPageMap(),
            "0", data.Length, startAddress: "$0700", format: "ca65", analyze: true);

        // The first 6 bytes should be emitted as .byte directives
        // The header bytes are $D0, $03, $00, $07, $40, $02
        Assert.Contains(".byte", result);
        // BNE should NOT appear as a mnemonic for the header bytes
        // (the header is data, not code)
        Assert.DoesNotContain("BNE", result);
        // The code after the header should still be disassembled
        Assert.Contains("LDA", result);
        Assert.Contains("STA", result);
        Assert.Contains("RTS", result);
    }

    [Fact]
    public void Analyze_DetectsBootHeaderAsDataReferences()
    {
        // Verify that the analyzer detects the boot header bytes as data references
        var data = new byte[]
        {
            0xD0, 0x03,
            0x00, 0x07,
            0x40, 0x02,       // 0x02 is illegal (1 byte, doesn't consume next byte)
            0xA9, 0x00,
            0x60
        };

        var references = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x0700);

        // The boot header bytes (addresses $0700-$0705) should be in absolute data references
        for (ushort addr = 0x0700; addr < 0x0706; addr++)
        {
            Assert.Contains(addr, references.AbsoluteDataReferences);
        }
    }

    [Fact]
    public void AnalyzeAndTrace_BootHeaderBytesShouldRemainDataAfterTracing()
    {
        // After tracing code regions, the boot header bytes should still be marked as data
        var data = new byte[]
        {
            0xD0, 0x03,       // Boot header bytes
            0x00, 0x07,
            0x40, 0x02,       // 0x02 is illegal (1 byte, doesn't consume next byte)
            0xA9, 0x00,       // LDA #$00 (code after header)
            0x60              // RTS
        };

        var references = DisassemblyAnalyzer.Analyze(data, null, (ushort)0x0700);
        var (codeRegions, dataRegions) = DisassemblyAnalyzer.TraceCodeRegions(data, references, null, (ushort)0x0700);

        // The boot header bytes should be in data regions, not code regions
        for (ushort addr = 0x0700; addr < 0x0706; addr++)
        {
            Assert.Contains(addr, dataRegions);
            Assert.DoesNotContain(addr, codeRegions);
        }

        // The code after the header should be in code regions
        // LDA #$00 at address 0x0706 (2 bytes: 0x0706, 0x0707)
        // RTS at address 0x0708 (1 byte)
        Assert.Contains((ushort)0x0706, codeRegions);
        Assert.Contains((ushort)0x0707, codeRegions);
        Assert.Contains((ushort)0x0708, codeRegions);
    }

    [Fact]
    public void DisassembleWithoutAnalyze_MayDisassembleBootHeaderAsCode()
    {
        // Without --analyze, the boot header may be disassembled as code
        // (this is expected behavior without analysis)
        var data = new byte[]
        {
            0xD0, 0x03,
            0x00, 0x07,
            0x40, 0x02,       // 0x02 is illegal (1 byte)
            0xA9, 0x00,
            0x60
        };

        var session = new RomSession { Data = data, BaseAddress = 0x0700 };

        var result = DisassemblerTool.Disassemble(
            session, new SymbolTable(), new ZeroPageMap(),
            "0", data.Length, startAddress: "$0700", format: "ca65", analyze: false);

        // Without analyze, the header bytes may be disassembled as code
        // (this test just verifies the behavior difference)
        Assert.Contains("BNE", result);
        Assert.Contains("BRK", result);
    }
}