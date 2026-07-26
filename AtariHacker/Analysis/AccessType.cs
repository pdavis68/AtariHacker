namespace AtariHacker.Analysis;

/// <summary>
/// Classifies how an instruction accesses a memory address.
/// </summary>
public enum AccessType
{
    /// <summary>Instruction reads from the address (LDA, LDX, CMP, etc.)</summary>
    Read,
    /// <summary>Instruction writes to the address (STA, STX, etc.)</summary>
    Write,
    /// <summary>Instruction both reads and modifies the address (INC, DEC, ROL, etc.)</summary>
    ReadWrite,
    /// <summary>Instruction transfers control to the address (JSR, JMP)</summary>
    Execute
}
