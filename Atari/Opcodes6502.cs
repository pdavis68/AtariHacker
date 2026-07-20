namespace AtariHackerMCP.Atari;

public enum AddressingMode
{
    Implied,
    Accumulator,
    Immediate,
    ZeroPage,
    ZeroPageX,
    ZeroPageY,
    Absolute,
    AbsoluteX,
    AbsoluteY,
    Indirect,
    IndirectX,
    IndirectY,
    Relative
}

public sealed record OpcodeEntry(byte OpCode, string Mnemonic, AddressingMode Mode, int Bytes, bool IsOfficial);

public static class Opcodes6502
{
    public static IReadOnlyDictionary<byte, OpcodeEntry> Table { get; } = BuildTable();

    private static Dictionary<byte, OpcodeEntry> BuildTable()
    {
        var table = new Dictionary<byte, OpcodeEntry>(256);

        void Add(byte opCode, string mnemonic, AddressingMode mode, int bytes, bool isOfficial = true)
            => table[opCode] = new OpcodeEntry(opCode, mnemonic, mode, bytes, isOfficial);

        Add(0x00, "BRK", AddressingMode.Implied, 1); Add(0x01, "ORA", AddressingMode.IndirectX, 2); Add(0x02, "KIL", AddressingMode.Implied, 1, false); Add(0x03, "SLO", AddressingMode.IndirectX, 2, false);
        Add(0x04, "NOP", AddressingMode.ZeroPage, 2, false); Add(0x05, "ORA", AddressingMode.ZeroPage, 2); Add(0x06, "ASL", AddressingMode.ZeroPage, 2); Add(0x07, "SLO", AddressingMode.ZeroPage, 2, false);
        Add(0x08, "PHP", AddressingMode.Implied, 1); Add(0x09, "ORA", AddressingMode.Immediate, 2); Add(0x0A, "ASL", AddressingMode.Accumulator, 1); Add(0x0B, "ANC", AddressingMode.Immediate, 2, false);
        Add(0x0C, "NOP", AddressingMode.Absolute, 3, false); Add(0x0D, "ORA", AddressingMode.Absolute, 3); Add(0x0E, "ASL", AddressingMode.Absolute, 3); Add(0x0F, "SLO", AddressingMode.Absolute, 3, false);

        Add(0x10, "BPL", AddressingMode.Relative, 2); Add(0x11, "ORA", AddressingMode.IndirectY, 2); Add(0x12, "KIL", AddressingMode.Implied, 1, false); Add(0x13, "SLO", AddressingMode.IndirectY, 2, false);
        Add(0x14, "NOP", AddressingMode.ZeroPageX, 2, false); Add(0x15, "ORA", AddressingMode.ZeroPageX, 2); Add(0x16, "ASL", AddressingMode.ZeroPageX, 2); Add(0x17, "SLO", AddressingMode.ZeroPageX, 2, false);
        Add(0x18, "CLC", AddressingMode.Implied, 1); Add(0x19, "ORA", AddressingMode.AbsoluteY, 3); Add(0x1A, "NOP", AddressingMode.Implied, 1, false); Add(0x1B, "SLO", AddressingMode.AbsoluteY, 3, false);
        Add(0x1C, "NOP", AddressingMode.AbsoluteX, 3, false); Add(0x1D, "ORA", AddressingMode.AbsoluteX, 3); Add(0x1E, "ASL", AddressingMode.AbsoluteX, 3); Add(0x1F, "SLO", AddressingMode.AbsoluteX, 3, false);

        Add(0x20, "JSR", AddressingMode.Absolute, 3); Add(0x21, "AND", AddressingMode.IndirectX, 2); Add(0x22, "KIL", AddressingMode.Implied, 1, false); Add(0x23, "RLA", AddressingMode.IndirectX, 2, false);
        Add(0x24, "BIT", AddressingMode.ZeroPage, 2); Add(0x25, "AND", AddressingMode.ZeroPage, 2); Add(0x26, "ROL", AddressingMode.ZeroPage, 2); Add(0x27, "RLA", AddressingMode.ZeroPage, 2, false);
        Add(0x28, "PLP", AddressingMode.Implied, 1); Add(0x29, "AND", AddressingMode.Immediate, 2); Add(0x2A, "ROL", AddressingMode.Accumulator, 1); Add(0x2B, "ANC", AddressingMode.Immediate, 2, false);
        Add(0x2C, "BIT", AddressingMode.Absolute, 3); Add(0x2D, "AND", AddressingMode.Absolute, 3); Add(0x2E, "ROL", AddressingMode.Absolute, 3); Add(0x2F, "RLA", AddressingMode.Absolute, 3, false);

        Add(0x30, "BMI", AddressingMode.Relative, 2); Add(0x31, "AND", AddressingMode.IndirectY, 2); Add(0x32, "KIL", AddressingMode.Implied, 1, false); Add(0x33, "RLA", AddressingMode.IndirectY, 2, false);
        Add(0x34, "NOP", AddressingMode.ZeroPageX, 2, false); Add(0x35, "AND", AddressingMode.ZeroPageX, 2); Add(0x36, "ROL", AddressingMode.ZeroPageX, 2); Add(0x37, "RLA", AddressingMode.ZeroPageX, 2, false);
        Add(0x38, "SEC", AddressingMode.Implied, 1); Add(0x39, "AND", AddressingMode.AbsoluteY, 3); Add(0x3A, "NOP", AddressingMode.Implied, 1, false); Add(0x3B, "RLA", AddressingMode.AbsoluteY, 3, false);
        Add(0x3C, "NOP", AddressingMode.AbsoluteX, 3, false); Add(0x3D, "AND", AddressingMode.AbsoluteX, 3); Add(0x3E, "ROL", AddressingMode.AbsoluteX, 3); Add(0x3F, "RLA", AddressingMode.AbsoluteX, 3, false);

        Add(0x40, "RTI", AddressingMode.Implied, 1); Add(0x41, "EOR", AddressingMode.IndirectX, 2); Add(0x42, "KIL", AddressingMode.Implied, 1, false); Add(0x43, "SRE", AddressingMode.IndirectX, 2, false);
        Add(0x44, "NOP", AddressingMode.ZeroPage, 2, false); Add(0x45, "EOR", AddressingMode.ZeroPage, 2); Add(0x46, "LSR", AddressingMode.ZeroPage, 2); Add(0x47, "SRE", AddressingMode.ZeroPage, 2, false);
        Add(0x48, "PHA", AddressingMode.Implied, 1); Add(0x49, "EOR", AddressingMode.Immediate, 2); Add(0x4A, "LSR", AddressingMode.Accumulator, 1); Add(0x4B, "ALR", AddressingMode.Immediate, 2, false);
        Add(0x4C, "JMP", AddressingMode.Absolute, 3); Add(0x4D, "EOR", AddressingMode.Absolute, 3); Add(0x4E, "LSR", AddressingMode.Absolute, 3); Add(0x4F, "SRE", AddressingMode.Absolute, 3, false);

        Add(0x50, "BVC", AddressingMode.Relative, 2); Add(0x51, "EOR", AddressingMode.IndirectY, 2); Add(0x52, "KIL", AddressingMode.Implied, 1, false); Add(0x53, "SRE", AddressingMode.IndirectY, 2, false);
        Add(0x54, "NOP", AddressingMode.ZeroPageX, 2, false); Add(0x55, "EOR", AddressingMode.ZeroPageX, 2); Add(0x56, "LSR", AddressingMode.ZeroPageX, 2); Add(0x57, "SRE", AddressingMode.ZeroPageX, 2, false);
        Add(0x58, "CLI", AddressingMode.Implied, 1); Add(0x59, "EOR", AddressingMode.AbsoluteY, 3); Add(0x5A, "NOP", AddressingMode.Implied, 1, false); Add(0x5B, "SRE", AddressingMode.AbsoluteY, 3, false);
        Add(0x5C, "NOP", AddressingMode.AbsoluteX, 3, false); Add(0x5D, "EOR", AddressingMode.AbsoluteX, 3); Add(0x5E, "LSR", AddressingMode.AbsoluteX, 3); Add(0x5F, "SRE", AddressingMode.AbsoluteX, 3, false);

        Add(0x60, "RTS", AddressingMode.Implied, 1); Add(0x61, "ADC", AddressingMode.IndirectX, 2); Add(0x62, "KIL", AddressingMode.Implied, 1, false); Add(0x63, "RRA", AddressingMode.IndirectX, 2, false);
        Add(0x64, "NOP", AddressingMode.ZeroPage, 2, false); Add(0x65, "ADC", AddressingMode.ZeroPage, 2); Add(0x66, "ROR", AddressingMode.ZeroPage, 2); Add(0x67, "RRA", AddressingMode.ZeroPage, 2, false);
        Add(0x68, "PLA", AddressingMode.Implied, 1); Add(0x69, "ADC", AddressingMode.Immediate, 2); Add(0x6A, "ROR", AddressingMode.Accumulator, 1); Add(0x6B, "ARR", AddressingMode.Immediate, 2, false);
        Add(0x6C, "JMP", AddressingMode.Indirect, 3); Add(0x6D, "ADC", AddressingMode.Absolute, 3); Add(0x6E, "ROR", AddressingMode.Absolute, 3); Add(0x6F, "RRA", AddressingMode.Absolute, 3, false);

        Add(0x70, "BVS", AddressingMode.Relative, 2); Add(0x71, "ADC", AddressingMode.IndirectY, 2); Add(0x72, "KIL", AddressingMode.Implied, 1, false); Add(0x73, "RRA", AddressingMode.IndirectY, 2, false);
        Add(0x74, "NOP", AddressingMode.ZeroPageX, 2, false); Add(0x75, "ADC", AddressingMode.ZeroPageX, 2); Add(0x76, "ROR", AddressingMode.ZeroPageX, 2); Add(0x77, "RRA", AddressingMode.ZeroPageX, 2, false);
        Add(0x78, "SEI", AddressingMode.Implied, 1); Add(0x79, "ADC", AddressingMode.AbsoluteY, 3); Add(0x7A, "NOP", AddressingMode.Implied, 1, false); Add(0x7B, "RRA", AddressingMode.AbsoluteY, 3, false);
        Add(0x7C, "NOP", AddressingMode.AbsoluteX, 3, false); Add(0x7D, "ADC", AddressingMode.AbsoluteX, 3); Add(0x7E, "ROR", AddressingMode.AbsoluteX, 3); Add(0x7F, "RRA", AddressingMode.AbsoluteX, 3, false);

        Add(0x80, "NOP", AddressingMode.Immediate, 2, false); Add(0x81, "STA", AddressingMode.IndirectX, 2); Add(0x82, "NOP", AddressingMode.Immediate, 2, false); Add(0x83, "SAX", AddressingMode.IndirectX, 2, false);
        Add(0x84, "STY", AddressingMode.ZeroPage, 2); Add(0x85, "STA", AddressingMode.ZeroPage, 2); Add(0x86, "STX", AddressingMode.ZeroPage, 2); Add(0x87, "SAX", AddressingMode.ZeroPage, 2, false);
        Add(0x88, "DEY", AddressingMode.Implied, 1); Add(0x89, "NOP", AddressingMode.Immediate, 2, false); Add(0x8A, "TXA", AddressingMode.Implied, 1); Add(0x8B, "XAA", AddressingMode.Immediate, 2, false);
        Add(0x8C, "STY", AddressingMode.Absolute, 3); Add(0x8D, "STA", AddressingMode.Absolute, 3); Add(0x8E, "STX", AddressingMode.Absolute, 3); Add(0x8F, "SAX", AddressingMode.Absolute, 3, false);

        Add(0x90, "BCC", AddressingMode.Relative, 2); Add(0x91, "STA", AddressingMode.IndirectY, 2); Add(0x92, "KIL", AddressingMode.Implied, 1, false); Add(0x93, "AHX", AddressingMode.IndirectY, 2, false);
        Add(0x94, "STY", AddressingMode.ZeroPageX, 2); Add(0x95, "STA", AddressingMode.ZeroPageX, 2); Add(0x96, "STX", AddressingMode.ZeroPageY, 2); Add(0x97, "SAX", AddressingMode.ZeroPageY, 2, false);
        Add(0x98, "TYA", AddressingMode.Implied, 1); Add(0x99, "STA", AddressingMode.AbsoluteY, 3); Add(0x9A, "TXS", AddressingMode.Implied, 1); Add(0x9B, "TAS", AddressingMode.AbsoluteY, 3, false);
        Add(0x9C, "SHY", AddressingMode.AbsoluteX, 3, false); Add(0x9D, "STA", AddressingMode.AbsoluteX, 3); Add(0x9E, "SHX", AddressingMode.AbsoluteY, 3, false); Add(0x9F, "AHX", AddressingMode.AbsoluteY, 3, false);

        Add(0xA0, "LDY", AddressingMode.Immediate, 2); Add(0xA1, "LDA", AddressingMode.IndirectX, 2); Add(0xA2, "LDX", AddressingMode.Immediate, 2); Add(0xA3, "LAX", AddressingMode.IndirectX, 2, false);
        Add(0xA4, "LDY", AddressingMode.ZeroPage, 2); Add(0xA5, "LDA", AddressingMode.ZeroPage, 2); Add(0xA6, "LDX", AddressingMode.ZeroPage, 2); Add(0xA7, "LAX", AddressingMode.ZeroPage, 2, false);
        Add(0xA8, "TAY", AddressingMode.Implied, 1); Add(0xA9, "LDA", AddressingMode.Immediate, 2); Add(0xAA, "TAX", AddressingMode.Implied, 1); Add(0xAB, "LAX", AddressingMode.Immediate, 2, false);
        Add(0xAC, "LDY", AddressingMode.Absolute, 3); Add(0xAD, "LDA", AddressingMode.Absolute, 3); Add(0xAE, "LDX", AddressingMode.Absolute, 3); Add(0xAF, "LAX", AddressingMode.Absolute, 3, false);

        Add(0xB0, "BCS", AddressingMode.Relative, 2); Add(0xB1, "LDA", AddressingMode.IndirectY, 2); Add(0xB2, "KIL", AddressingMode.Implied, 1, false); Add(0xB3, "LAX", AddressingMode.IndirectY, 2, false);
        Add(0xB4, "LDY", AddressingMode.ZeroPageX, 2); Add(0xB5, "LDA", AddressingMode.ZeroPageX, 2); Add(0xB6, "LDX", AddressingMode.ZeroPageY, 2); Add(0xB7, "LAX", AddressingMode.ZeroPageY, 2, false);
        Add(0xB8, "CLV", AddressingMode.Implied, 1); Add(0xB9, "LDA", AddressingMode.AbsoluteY, 3); Add(0xBA, "TSX", AddressingMode.Implied, 1); Add(0xBB, "LAS", AddressingMode.AbsoluteY, 3, false);
        Add(0xBC, "LDY", AddressingMode.AbsoluteX, 3); Add(0xBD, "LDA", AddressingMode.AbsoluteX, 3); Add(0xBE, "LDX", AddressingMode.AbsoluteY, 3); Add(0xBF, "LAX", AddressingMode.AbsoluteY, 3, false);

        Add(0xC0, "CPY", AddressingMode.Immediate, 2); Add(0xC1, "CMP", AddressingMode.IndirectX, 2); Add(0xC2, "NOP", AddressingMode.Immediate, 2, false); Add(0xC3, "DCP", AddressingMode.IndirectX, 2, false);
        Add(0xC4, "CPY", AddressingMode.ZeroPage, 2); Add(0xC5, "CMP", AddressingMode.ZeroPage, 2); Add(0xC6, "DEC", AddressingMode.ZeroPage, 2); Add(0xC7, "DCP", AddressingMode.ZeroPage, 2, false);
        Add(0xC8, "INY", AddressingMode.Implied, 1); Add(0xC9, "CMP", AddressingMode.Immediate, 2); Add(0xCA, "DEX", AddressingMode.Implied, 1); Add(0xCB, "AXS", AddressingMode.Immediate, 2, false);
        Add(0xCC, "CPY", AddressingMode.Absolute, 3); Add(0xCD, "CMP", AddressingMode.Absolute, 3); Add(0xCE, "DEC", AddressingMode.Absolute, 3); Add(0xCF, "DCP", AddressingMode.Absolute, 3, false);

        Add(0xD0, "BNE", AddressingMode.Relative, 2); Add(0xD1, "CMP", AddressingMode.IndirectY, 2); Add(0xD2, "KIL", AddressingMode.Implied, 1, false); Add(0xD3, "DCP", AddressingMode.IndirectY, 2, false);
        Add(0xD4, "NOP", AddressingMode.ZeroPageX, 2, false); Add(0xD5, "CMP", AddressingMode.ZeroPageX, 2); Add(0xD6, "DEC", AddressingMode.ZeroPageX, 2); Add(0xD7, "DCP", AddressingMode.ZeroPageX, 2, false);
        Add(0xD8, "CLD", AddressingMode.Implied, 1); Add(0xD9, "CMP", AddressingMode.AbsoluteY, 3); Add(0xDA, "NOP", AddressingMode.Implied, 1, false); Add(0xDB, "DCP", AddressingMode.AbsoluteY, 3, false);
        Add(0xDC, "NOP", AddressingMode.AbsoluteX, 3, false); Add(0xDD, "CMP", AddressingMode.AbsoluteX, 3); Add(0xDE, "DEC", AddressingMode.AbsoluteX, 3); Add(0xDF, "DCP", AddressingMode.AbsoluteX, 3, false);

        Add(0xE0, "CPX", AddressingMode.Immediate, 2); Add(0xE1, "SBC", AddressingMode.IndirectX, 2); Add(0xE2, "NOP", AddressingMode.Immediate, 2, false); Add(0xE3, "ISC", AddressingMode.IndirectX, 2, false);
        Add(0xE4, "CPX", AddressingMode.ZeroPage, 2); Add(0xE5, "SBC", AddressingMode.ZeroPage, 2); Add(0xE6, "INC", AddressingMode.ZeroPage, 2); Add(0xE7, "ISC", AddressingMode.ZeroPage, 2, false);
        Add(0xE8, "INX", AddressingMode.Implied, 1); Add(0xE9, "SBC", AddressingMode.Immediate, 2); Add(0xEA, "NOP", AddressingMode.Implied, 1); Add(0xEB, "SBC", AddressingMode.Immediate, 2, false);
        Add(0xEC, "CPX", AddressingMode.Absolute, 3); Add(0xED, "SBC", AddressingMode.Absolute, 3); Add(0xEE, "INC", AddressingMode.Absolute, 3); Add(0xEF, "ISC", AddressingMode.Absolute, 3, false);

        Add(0xF0, "BEQ", AddressingMode.Relative, 2); Add(0xF1, "SBC", AddressingMode.IndirectY, 2); Add(0xF2, "KIL", AddressingMode.Implied, 1, false); Add(0xF3, "ISC", AddressingMode.IndirectY, 2, false);
        Add(0xF4, "NOP", AddressingMode.ZeroPageX, 2, false); Add(0xF5, "SBC", AddressingMode.ZeroPageX, 2); Add(0xF6, "INC", AddressingMode.ZeroPageX, 2); Add(0xF7, "ISC", AddressingMode.ZeroPageX, 2, false);
        Add(0xF8, "SED", AddressingMode.Implied, 1); Add(0xF9, "SBC", AddressingMode.AbsoluteY, 3); Add(0xFA, "NOP", AddressingMode.Implied, 1, false); Add(0xFB, "ISC", AddressingMode.AbsoluteY, 3, false);
        Add(0xFC, "NOP", AddressingMode.AbsoluteX, 3, false); Add(0xFD, "SBC", AddressingMode.AbsoluteX, 3); Add(0xFE, "INC", AddressingMode.AbsoluteX, 3); Add(0xFF, "ISC", AddressingMode.AbsoluteX, 3, false);

        return table;
    }
}
