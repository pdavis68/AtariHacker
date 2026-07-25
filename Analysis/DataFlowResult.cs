namespace AtariHacker.Analysis;

/// <summary>
/// Represents a link in a data flow chain between a write and a read instruction.
/// </summary>
public sealed record DataFlowLink(
    ushort FromAddress,
    AccessType FromAccess,
    ushort ToAddress,
    AccessType ToAccess,
    string Path,
    int InstructionCount);

/// <summary>
/// The complete result of a data flow trace for a target address.
/// </summary>
public sealed record DataFlowResult(
    ushort TargetAddress,
    List<XRefEntry> Writes,
    List<XRefEntry> Reads,
    List<DataFlowLink> Chain);
