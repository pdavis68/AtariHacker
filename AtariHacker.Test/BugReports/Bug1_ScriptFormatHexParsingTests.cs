using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test.BugReports;

/// <summary>
/// Bug 1: Script format does not parse `$` prefix as hex for `offset`
///
/// When using the batch script format, the `offset` parameter does not recognize
/// the `$` prefix as hexadecimal. The value `$0700` is parsed as decimal 700 instead
/// of hex 0x0700 (1792 decimal).
/// </summary>
public sealed class Bug1_ScriptFormatHexParsingTests
{
    [Fact]
    public void ScriptDisassemble_WithHexOffset_ParsesCorrectly()
    {
        // Create a 2000-byte ROM with known data at offset 0x0700 (1792 decimal)
        var data = new byte[2000];
        // Put a distinctive pattern at offset 0x0700 (1792 decimal) that we can verify
        // LDA #$40 ($A9 $40) followed by RTS ($60)
        data[1792] = 0xA9; // LDA #imm
        data[1793] = 0x40; // #$40
        data[1794] = 0x60; // RTS
        // Put different data at offset 700 (decimal) to distinguish
        data[700] = 0xEA; // NOP
        data[701] = 0xEA; // NOP
        data[702] = 0xEA; // NOP

        var session = new RomSession { Data = data };
        var tempFile = Path.GetTempFileName();
        try
        {
            // Script uses $ prefix for hex offset
            File.WriteAllText(tempFile, "disassemble offset=$0700 numBytes=3 format=ca65");

            var result = ScriptRunner.RunScript(session, new SymbolTable(), new ZeroPageMap(), null!, new SegmentManager(), tempFile);

            // The result should contain LDA #$40, not NOPs
            Assert.Contains("LDA", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("#$40", result);
            // Should NOT contain the NOPs at offset 700 decimal
            // NOP at offset 700 decimal would show as $0700 memory address but
            // the disassembly should be at offset 1792 = $0700 hex
            Assert.DoesNotContain(string.Join(' ', [0xEA.ToString("X2"), 0xEA.ToString("X2"), 0xEA.ToString("X2")]), result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ScriptDisassemble_WithHexOffsetDecimalOffset_ProducesDifferentResult()
    {
        // Verify that $0700 (hex) and 700 (decimal) produce different results
        var data = new byte[2000];
        data[700] = 0xA9; // LDA #imm
        data[701] = 0x01; // #$01
        data[702] = 0x60; // RTS
        data[1792] = 0xA9; // LDA #imm
        data[1793] = 0x02; // #$02
        data[1794] = 0x60; // RTS

        var session = new RomSession { Data = data };
        var hexScript = Path.GetTempFileName();
        var decScript = Path.GetTempFileName();
        try
        {
            // Script with $0700 hex offset
            File.WriteAllText(hexScript, "disassemble offset=$0700 numBytes=3 format=ca65");
            var hexResult = ScriptRunner.RunScript(session, new SymbolTable(), new ZeroPageMap(), null!, new SegmentManager(), hexScript);

            // Script with 700 decimal offset
            File.WriteAllText(decScript, "disassemble offset=700 numBytes=3 format=ca65");
            var decResult = ScriptRunner.RunScript(session, new SymbolTable(), new ZeroPageMap(), null!, new SegmentManager(), decScript);

            // The hex result should contain #$02 (from offset 1792)
            Assert.Contains("#$02", hexResult);
            // The decimal result should contain #$01 (from offset 700)
            Assert.Contains("#$01", decResult);
        }
        finally
        {
            File.Delete(hexScript);
            File.Delete(decScript);
        }
    }
}