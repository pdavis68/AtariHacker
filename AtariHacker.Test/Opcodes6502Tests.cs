using AtariHacker.Atari;

namespace AtariHacker.Test;

public sealed class Opcodes6502Tests
{
    [Fact]
    public void Table_ContainsExactly256Entries()
    {
        Assert.Equal(256, Opcodes6502.Table.Count);
    }

    [Fact]
    public void Table_CorrectlyMapsKnownOpcodes()
    {
        // 0xA9 = LDA immediate
        var entry = Opcodes6502.Table[0xA9];
        Assert.Equal("LDA", entry.Mnemonic);
        Assert.Equal(AddressingMode.Immediate, entry.Mode);
        Assert.Equal(2, entry.Bytes);
        Assert.True(entry.IsOfficial);
    }

    [Fact]
    public void Table_MarksIllegalOpcodesCorrectly()
    {
        // 0x02 = KIL (illegal)
        var entry = Opcodes6502.Table[0x02];
        Assert.False(entry.IsOfficial);
        Assert.Equal("KIL", entry.Mnemonic);

        // 0x80 = NOP (illegal)
        entry = Opcodes6502.Table[0x80];
        Assert.False(entry.IsOfficial);
    }

    [Fact]
    public void Table_EntriesHaveCorrectAddressingModesAndByteCounts()
    {
        // JSR absolute = 3 bytes
        var jsr = Opcodes6502.Table[0x20];
        Assert.Equal("JSR", jsr.Mnemonic);
        Assert.Equal(AddressingMode.Absolute, jsr.Mode);
        Assert.Equal(3, jsr.Bytes);

        // LDA immediate = 2 bytes
        var lda = Opcodes6502.Table[0xA9];
        Assert.Equal(AddressingMode.Immediate, lda.Mode);
        Assert.Equal(2, lda.Bytes);

        // BRK implied = 1 byte
        var brk = Opcodes6502.Table[0x00];
        Assert.Equal(AddressingMode.Implied, brk.Mode);
        Assert.Equal(1, brk.Bytes);

        // BNE relative = 2 bytes
        var bne = Opcodes6502.Table[0xD0];
        Assert.Equal(AddressingMode.Relative, bne.Mode);
        Assert.Equal(2, bne.Bytes);
    }

    [Fact]
    public void AllOfficialOpcodes_HaveCorrectMnemonicStrings()
    {
        // Spot-check a range of official opcodes
        Assert.Equal("BRK", Opcodes6502.Table[0x00].Mnemonic);
        Assert.Equal("JSR", Opcodes6502.Table[0x20].Mnemonic);
        Assert.Equal("RTI", Opcodes6502.Table[0x40].Mnemonic);
        Assert.Equal("RTS", Opcodes6502.Table[0x60].Mnemonic);
        Assert.Equal("BCC", Opcodes6502.Table[0x90].Mnemonic);
        Assert.Equal("LDY", Opcodes6502.Table[0xA0].Mnemonic);
        Assert.Equal("CPY", Opcodes6502.Table[0xC0].Mnemonic);
        Assert.Equal("BNE", Opcodes6502.Table[0xD0].Mnemonic);
        Assert.Equal("CPX", Opcodes6502.Table[0xE0].Mnemonic);
        Assert.Equal("BEQ", Opcodes6502.Table[0xF0].Mnemonic);
    }
}
