using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Classifies an opcode's inline operand. Determines operand size during decode and
/// lets consumers read tokens/indices/constants without re-deriving opcode shape.
/// </summary>
public enum OperandKind
{
    None,
    ShortInlineVar,   // 1-byte local/argument index
    InlineVar,        // 2-byte local/argument index
    ShortInlineI,     // int8
    InlineI,          // int32
    InlineI8,         // int64
    ShortInlineR,     // float32
    InlineR,          // float64
    InlineString,     // string token
    InlineMethod,     // method token
    InlineField,      // field token
    InlineType,       // type token
    InlineSig,        // standalone signature token (calli)
    InlineTok,        // ldtoken: field/method/type token
    ShortInlineBrTarget,
    InlineBrTarget,
    InlineSwitch,
}

/// <summary>
/// One decoded IL instruction: its position, opcode, decoded operand classification,
/// resolved branch targets, and the structural control-flow facts the block builder needs.
/// The single decode shared by analysis and (eventually) the decompiler importer.
/// </summary>
public sealed record DecodedInstruction(
    int Offset,
    ILOpCode OpCode,
    int OperandOffset,
    int NextOffset,
    OperandKind Operand,
    long OperandValue,
    ImmutableArray<int> BranchTargets,
    bool Branches,
    bool IsUnconditionalBranch,
    bool Exits,
    bool FallsThrough,
    bool LeavesRegion)
{
    /// <summary>Total instruction length in bytes, including operand.</summary>
    public int Length => NextOffset - Offset;

    /// <summary>True when this instruction ends its basic block (branch, return, throw, or EH exit).</summary>
    public bool TerminatesBlock => Branches || Exits;
}
