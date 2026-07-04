using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

public enum IlBodyDiffChangeKind
{
    Changed,
    Added,
    Removed,
}

public sealed record IlInstructionDiff(
    IlBodyDiffChangeKind Kind,
    int InstructionIndex,
    int? OldOffset,
    ILOpCode? OldOpCode,
    int? NewOffset,
    ILOpCode? NewOpCode);

public sealed record IlBodyDiffResult(
    bool IsExact,
    string? Failure,
    ImmutableArray<IlInstructionDiff> Differences);

/// <summary>
/// Low-level IL body diff substrate over decoded instruction streams.
/// </summary>
public static class IlBodyDiff
{
    public static IlBodyDiffResult Compare(MethodInstructions oldBody, MethodInstructions newBody)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);

        if (!oldBody.IsComplete)
            return new IlBodyDiffResult(false, oldBody.Blocks.IncompleteReason ?? "old body decode failed", []);
        if (!newBody.IsComplete)
            return new IlBodyDiffResult(false, newBody.Blocks.IncompleteReason ?? "new body decode failed", []);

        var differences = ImmutableArray.CreateBuilder<IlInstructionDiff>();
        int shared = Math.Min(oldBody.Instructions.Length, newBody.Instructions.Length);
        for (int i = 0; i < shared; i++)
        {
            var oldInstruction = oldBody.Instructions[i];
            var newInstruction = newBody.Instructions[i];
            if (CanonicalEquals(oldInstruction, newInstruction))
                continue;

            differences.Add(new IlInstructionDiff(
                IlBodyDiffChangeKind.Changed,
                i,
                oldInstruction.Offset,
                oldInstruction.OpCode,
                newInstruction.Offset,
                newInstruction.OpCode));
        }

        for (int i = shared; i < oldBody.Instructions.Length; i++)
        {
            var oldInstruction = oldBody.Instructions[i];
            differences.Add(new IlInstructionDiff(
                IlBodyDiffChangeKind.Removed,
                i,
                oldInstruction.Offset,
                oldInstruction.OpCode,
                NewOffset: null,
                NewOpCode: null));
        }

        for (int i = shared; i < newBody.Instructions.Length; i++)
        {
            var newInstruction = newBody.Instructions[i];
            differences.Add(new IlInstructionDiff(
                IlBodyDiffChangeKind.Added,
                i,
                OldOffset: null,
                OldOpCode: null,
                newInstruction.Offset,
                newInstruction.OpCode));
        }

        var rows = differences.ToImmutable();
        return new IlBodyDiffResult(rows.Length == 0, Failure: null, rows);
    }

    static bool CanonicalEquals(DecodedInstruction oldInstruction, DecodedInstruction newInstruction)
        => oldInstruction.OpCode == newInstruction.OpCode
            && oldInstruction.Operand == newInstruction.Operand
            && oldInstruction.OperandValue == newInstruction.OperandValue
            && oldInstruction.BranchTargets.SequenceEqual(newInstruction.BranchTargets);
}
