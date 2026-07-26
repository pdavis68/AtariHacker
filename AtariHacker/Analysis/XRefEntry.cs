namespace AtariHacker.Analysis;

/// <summary>
/// Represents a single cross-reference hit with access type and context metadata.
/// </summary>
public sealed record XRefEntry(
    ushort Address,
    string Mnemonic,
    string Operand,
    AccessType Access,
    string? Procedure,
    string? Segment);
