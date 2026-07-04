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

        var oldInstructions = oldBody.Instructions;
        var newInstructions = newBody.Instructions;
        var lcs = LongestCommonSubsequence(oldInstructions, newInstructions);
        var differences = ImmutableArray.CreateBuilder<IlInstructionDiff>();
        int oldIndex = 0;
        int newIndex = 0;
        int diffIndex = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddUnmatched(oldInstructions, oldIndex, nextOld, newInstructions, newIndex, nextNew, differences, ref diffIndex);
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddUnmatched(oldInstructions, oldIndex, oldInstructions.Length, newInstructions, newIndex, newInstructions.Length, differences, ref diffIndex);

        var rows = differences.ToImmutable();
        return new IlBodyDiffResult(rows.Length == 0, Failure: null, rows);
    }

    static void AddUnmatched(
        ImmutableArray<DecodedInstruction> oldInstructions,
        int oldStart,
        int oldEnd,
        ImmutableArray<DecodedInstruction> newInstructions,
        int newStart,
        int newEnd,
        ImmutableArray<IlInstructionDiff>.Builder differences,
        ref int diffIndex)
    {
        int oldIndex = oldStart;
        int newIndex = newStart;
        while (oldIndex < oldEnd && newIndex < newEnd)
        {
            differences.Add(new IlInstructionDiff(
                IlBodyDiffChangeKind.Changed,
                diffIndex++,
                oldInstructions[oldIndex].Offset,
                oldInstructions[oldIndex].OpCode,
                newInstructions[newIndex].Offset,
                newInstructions[newIndex].OpCode));
            oldIndex++;
            newIndex++;
        }

        while (oldIndex < oldEnd)
        {
            var oldInstruction = oldInstructions[oldIndex++];
            differences.Add(new IlInstructionDiff(
                IlBodyDiffChangeKind.Removed,
                diffIndex++,
                oldInstruction.Offset,
                oldInstruction.OpCode,
                NewOffset: null,
                NewOpCode: null));
        }

        while (newIndex < newEnd)
        {
            var newInstruction = newInstructions[newIndex++];
            differences.Add(new IlInstructionDiff(
                IlBodyDiffChangeKind.Added,
                diffIndex++,
                OldOffset: null,
                OldOpCode: null,
                newInstruction.Offset,
                newInstruction.OpCode));
        }
    }

    static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(
        ImmutableArray<DecodedInstruction> oldInstructions,
        ImmutableArray<DecodedInstruction> newInstructions)
    {
        var lengths = new int[oldInstructions.Length + 1, newInstructions.Length + 1];
        for (int oldIndex = oldInstructions.Length - 1; oldIndex >= 0; oldIndex--)
        {
            for (int newIndex = newInstructions.Length - 1; newIndex >= 0; newIndex--)
            {
                lengths[oldIndex, newIndex] = CanonicalEquals(oldInstructions[oldIndex], newInstructions[newIndex])
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        var pairs = new List<(int OldIndex, int NewIndex)>();
        int i = 0;
        int j = 0;
        while (i < oldInstructions.Length && j < newInstructions.Length)
        {
            if (CanonicalEquals(oldInstructions[i], newInstructions[j]))
            {
                pairs.Add((i, j));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    static bool CanonicalEquals(DecodedInstruction oldInstruction, DecodedInstruction newInstruction)
    {
        if (oldInstruction.OpCode != newInstruction.OpCode || oldInstruction.Operand != newInstruction.Operand)
            return false;
        if (oldInstruction.Operand is OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget)
            return true;
        if (oldInstruction.Operand == OperandKind.InlineSwitch)
            return oldInstruction.BranchTargets.Length == newInstruction.BranchTargets.Length;
        return oldInstruction.OperandValue == newInstruction.OperandValue;
    }
}
