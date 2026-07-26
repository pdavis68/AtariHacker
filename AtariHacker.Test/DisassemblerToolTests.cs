using AtariHacker.Atari;
using AtariHacker.State;
using AtariHacker.Tools;

namespace AtariHacker.Test;

public sealed class DisassemblerToolTests
{
    [Fact]
    public void Disassemble_ReturnsErrorWhenNoRomIsLoaded()
    {
        var session = new RomSession();
        var result = DisassemblerTool.Disassemble(session, new SymbolTable(), new ZeroPageMap(), "0", 10);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void Disassemble_ReturnsErrorForOffsetBeyondRomSize()
    {
        var session = new RomSession { Data = new byte[10] };
        var result = DisassemblerTool.Disassemble(session, new SymbolTable(), new ZeroPageMap(), "20", 10);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void TryGetOfficialEntry_ReturnsTrueForOfficialOpcodes()
    {
        var found = DisassemblerTool.TryGetOfficialEntry(0xA9, out var entry);
        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("LDA", entry.Mnemonic);
    }

    [Fact]
    public void TryGetOfficialEntry_ReturnsFalseForIllegalOpcodes()
    {
        var found = DisassemblerTool.TryGetOfficialEntry(0x02, out var entry);
        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void Disassemble_DisassemblesKnownOpcodesCorrectly()
    {
        var data = new byte[] { 0xA9, 0x40, 0x60 };
        var session = new RomSession { Data = data };
        var result = DisassemblerTool.Disassemble(session, new SymbolTable(), new ZeroPageMap(), "0", 3);

        Assert.Contains("LDA", result);
        Assert.Contains("RTS", result);
    }

    [Fact]
    public void Disassemble_UsesAddressOverrideWhenProvided()
    {
        var data = new byte[] { 0xA9, 0x40, 0x60 };
        var session = new RomSession { Data = data };
        var result = DisassemblerTool.Disassemble(session, new SymbolTable(), new ZeroPageMap(), "0", 3, startAddress: "$8000");

        Assert.Contains("$8000", result);
    }

    [Fact]
    public void ResolveOperandAddress_ComputesCorrectTargetAddress()
    {
        var data = new byte[] { 0x20, 0x00, 0x81 };
        var entry = Opcodes6502.Table[0x20];
        var addr = DisassemblerTool.ResolveOperandAddress(entry, data, 0, (ushort)0x8000);
        Assert.Equal((ushort)0x8100, addr);
    }

    [Fact]
    public void FormatOperand_FormatsImmediateWithHash()
    {
        var data = new byte[] { 0xA9, 0x40 };
        var entry = Opcodes6502.Table[0xA9];
        var operand = DisassemblerTool.FormatOperand(entry, data, 0, (ushort)0x8000, new SymbolTable(), new ZeroPageMap());
        Assert.Equal("#$40", operand);
    }

    [Fact]
    public void FormatOperand_FormatsZeroPageCorrectly()
    {
        var data = new byte[] { 0xA5, 0x80 };
        var entry = Opcodes6502.Table[0xA5];
        var operand = DisassemblerTool.FormatOperand(entry, data, 0, (ushort)0x8000, new SymbolTable(), new ZeroPageMap());
        Assert.Equal("$80", operand);
    }

    [Fact]
    public void FormatOperand_FormatsAbsoluteOperandsCorrectly()
    {
        var data = new byte[] { 0xAD, 0x12, 0xD0 };
        var entry = Opcodes6502.Table[0xAD];
        var operand = DisassemblerTool.FormatOperand(entry, data, 0, (ushort)0x8000, new SymbolTable(), new ZeroPageMap());
        Assert.Equal("$D012", operand);
    }

    [Fact]
    public void FormatOperand_FormatsIndexedOperandsWithCommaXY()
    {
        var data = new byte[] { 0xBD, 0x00, 0x80 };
        var entry = Opcodes6502.Table[0xBD];
        var operand = DisassemblerTool.FormatOperand(entry, data, 0, (ushort)0x8000, new SymbolTable(), new ZeroPageMap());
        Assert.Equal("$8000,X", operand);
    }

    [Fact]
    public void FormatOperand_FormatsIndirectOperandsWithParens()
    {
        var data = new byte[] { 0x6C, 0x00, 0x80 };
        var entry = Opcodes6502.Table[0x6C];
        var operand = DisassemblerTool.FormatOperand(entry, data, 0, (ushort)0x8000, new SymbolTable(), new ZeroPageMap());
        Assert.Equal("($8000)", operand);
    }
}