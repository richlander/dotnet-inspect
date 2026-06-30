using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.OffsetReport;

/// <summary>What an instruction's operand refers to, once resolved against metadata.</summary>
public enum OffsetOperandKind
{
    None,
    Method,             // call/callvirt/newobj/ldftn/ldvirtftn/jmp callee
    IndirectSignature,  // calli standalone signature
    Field,
    Type,
    String,
    Token,              // ldtoken
    BranchTargets,      // br*/leave*/switch — one or more IL offsets
    Argument,           // ldarg/starg/ldarga slot
    Local,              // ldloc/stloc/ldloca slot
    Constant,           // ldc.*
}

/// <summary>An instruction operand resolved to a human/agent-readable form.</summary>
public sealed record OffsetOperand(OffsetOperandKind Kind, string Display);

/// <summary>The exception-handling region (if any) the offset sits inside, innermost wins.</summary>
public sealed record OffsetExceptionContext(
    HandlerKind Kind,
    string Position,    // "try" | "catch" | "filter" | "finally" | "fault"
    int RegionStart,
    int RegionEnd,
    string? CatchType);

/// <summary>The call instruction this offset is the return address of (offset == call.NextOffset).</summary>
public sealed record ReturnAddressSite(ILOpCode OpCode, int CallOffset, string Callee);

/// <summary>
/// The literal structural facts at one IL offset — the "spine" of an offset report. PDB-free; no
/// analysis index and no decompiler IR (substrate + metadata names only). Resolution returns null,
/// not an empty report, when the offset does not name a real instruction (bad token / no body /
/// not an instruction boundary). Dense/total — these are facts the IL <i>contains</i>, distinct from
/// the sparse, positive-only annotation overlay that joins onto this spine by offset.
/// </summary>
public sealed record OffsetReport(
    string Method,
    int MethodToken,
    int Offset,
    ILOpCode Instruction,
    string InstructionName,
    OffsetOperand? Operand,
    OffsetExceptionContext? ExceptionRegion,
    ReturnAddressSite? ReturnAddressOf,
    bool IsBranchTarget);
