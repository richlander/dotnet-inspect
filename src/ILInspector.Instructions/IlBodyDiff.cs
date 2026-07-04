using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

public enum IlDiffKind
{
    Context,
    Remove,
    Add,
}

public enum IlOperandIdentityKind
{
    Immediate,
    Token,
    Slot,
    BranchTarget,
    SwitchTargets,
}

public sealed record IlOperandIdentity(IlOperandIdentityKind Kind, string Value);

public sealed record CanonicalIlOperation(
    int Offset,
    string OpcodeFamily,
    IlOperandIdentity? Operand)
{
    public string Display => Operand is null ? OpcodeFamily : $"{OpcodeFamily} {Operand.Value}";
}

public sealed record IlDiffRow(
    int HunkId,
    IlDiffKind Kind,
    CanonicalIlOperation Operation);

public sealed record IlBodyDiffResult(
    bool IsExact,
    string? Failure,
    ImmutableArray<IlDiffRow> Rows);

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
        var oldToNew = lcs.ToDictionary(pair => pair.OldIndex, pair => pair.NewIndex);
        var rows = ImmutableArray.CreateBuilder<IlDiffRow>();
        int oldIndex = 0;
        int newIndex = 0;
        int hunkId = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddUnmatched(oldInstructions, oldIndex, nextOld, newInstructions, newIndex, nextNew, rows, ref hunkId);
            if (!BranchTargetsMatch(oldInstructions, nextOld, newInstructions, nextNew, oldToNew))
            {
                int hunk = hunkId++;
                rows.Add(new IlDiffRow(hunk, IlDiffKind.Remove, ToOperation(oldInstructions[nextOld])));
                rows.Add(new IlDiffRow(hunk, IlDiffKind.Add, ToOperation(newInstructions[nextNew])));
            }
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddUnmatched(oldInstructions, oldIndex, oldInstructions.Length, newInstructions, newIndex, newInstructions.Length, rows, ref hunkId);

        var diffRows = rows.ToImmutable();
        return new IlBodyDiffResult(diffRows.Length == 0, Failure: null, diffRows);
    }

    static void AddUnmatched(
        ImmutableArray<DecodedInstruction> oldInstructions,
        int oldStart,
        int oldEnd,
        ImmutableArray<DecodedInstruction> newInstructions,
        int newStart,
        int newEnd,
        ImmutableArray<IlDiffRow>.Builder rows,
        ref int hunkId)
    {
        if (oldStart == oldEnd && newStart == newEnd)
            return;

        int hunk = hunkId++;
        int oldIndex = oldStart;
        int newIndex = newStart;
        while (oldIndex < oldEnd)
            rows.Add(new IlDiffRow(hunk, IlDiffKind.Remove, ToOperation(oldInstructions[oldIndex++])));
        while (newIndex < newEnd)
            rows.Add(new IlDiffRow(hunk, IlDiffKind.Add, ToOperation(newInstructions[newIndex++])));
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
        var oldOperation = ToOperation(oldInstruction);
        var newOperation = ToOperation(newInstruction);
        if (oldOperation.OpcodeFamily != newOperation.OpcodeFamily)
            return false;
        if (oldInstruction.Operand is OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget)
            return true;
        if (oldInstruction.Operand == OperandKind.InlineSwitch)
            return oldInstruction.BranchTargets.Length == newInstruction.BranchTargets.Length;
        return oldOperation.Operand == newOperation.Operand;
    }

    static CanonicalIlOperation ToOperation(DecodedInstruction instruction)
        => new(
            instruction.Offset,
            OpcodeFamily(instruction),
            OperandIdentity(instruction));

    static string OpcodeFamily(DecodedInstruction instruction)
    {
        var opcode = instruction.OpCode;
        if (opcode is ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1
            or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4
            or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7
            or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4)
        {
            return "ldc.i4";
        }

        return opcode.IsShortBranch()
            ? opcode.ToString()[..^"_s".Length].Replace('_', '.').ToLowerInvariant()
            : opcode.ToString().Replace('_', '.').ToLowerInvariant();
    }

    static IlOperandIdentity? OperandIdentity(DecodedInstruction instruction)
        => instruction.OpCode switch
        {
            ILOpCode.Ldc_i4_m1 => new(IlOperandIdentityKind.Immediate, "-1"),
            ILOpCode.Ldc_i4_0 => new(IlOperandIdentityKind.Immediate, "0"),
            ILOpCode.Ldc_i4_1 => new(IlOperandIdentityKind.Immediate, "1"),
            ILOpCode.Ldc_i4_2 => new(IlOperandIdentityKind.Immediate, "2"),
            ILOpCode.Ldc_i4_3 => new(IlOperandIdentityKind.Immediate, "3"),
            ILOpCode.Ldc_i4_4 => new(IlOperandIdentityKind.Immediate, "4"),
            ILOpCode.Ldc_i4_5 => new(IlOperandIdentityKind.Immediate, "5"),
            ILOpCode.Ldc_i4_6 => new(IlOperandIdentityKind.Immediate, "6"),
            ILOpCode.Ldc_i4_7 => new(IlOperandIdentityKind.Immediate, "7"),
            ILOpCode.Ldc_i4_8 => new(IlOperandIdentityKind.Immediate, "8"),
            _ => OperandIdentityByKind(instruction),
        };

    static IlOperandIdentity? OperandIdentityByKind(DecodedInstruction instruction)
        => instruction.Operand switch
        {
            OperandKind.ShortInlineI or OperandKind.InlineI or OperandKind.InlineI8
                or OperandKind.ShortInlineR or OperandKind.InlineR
                => new(IlOperandIdentityKind.Immediate, instruction.OperandValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            OperandKind.ShortInlineVar or OperandKind.InlineVar
                => new(IlOperandIdentityKind.Slot, instruction.OperandValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget
                => new(IlOperandIdentityKind.BranchTarget, $"IL_{instruction.BranchTargets[0]:X4}"),
            OperandKind.InlineSwitch
                => new(IlOperandIdentityKind.SwitchTargets, string.Join(", ", instruction.BranchTargets.Select(target => $"IL_{target:X4}"))),
            OperandKind.InlineString or OperandKind.InlineMethod or OperandKind.InlineField
                or OperandKind.InlineType or OperandKind.InlineSig or OperandKind.InlineTok
                => new(IlOperandIdentityKind.Token, $"0x{instruction.OperandValue:X8}"),
            _ => null,
        };

    static bool BranchTargetsMatch(
        ImmutableArray<DecodedInstruction> oldInstructions,
        int oldIndex,
        ImmutableArray<DecodedInstruction> newInstructions,
        int newIndex,
        IReadOnlyDictionary<int, int> oldToNew)
    {
        var oldInstruction = oldInstructions[oldIndex];
        var newInstruction = newInstructions[newIndex];
        if (oldInstruction.Operand is not (OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget or OperandKind.InlineSwitch))
            return true;
        if (oldInstruction.BranchTargets.Length != newInstruction.BranchTargets.Length)
            return false;

        for (int i = 0; i < oldInstruction.BranchTargets.Length; i++)
        {
            int oldTargetIndex = InstructionIndexAt(oldInstructions, oldInstruction.BranchTargets[i]);
            int newTargetIndex = InstructionIndexAt(newInstructions, newInstruction.BranchTargets[i]);
            if (oldTargetIndex < 0 || newTargetIndex < 0)
                return oldInstruction.BranchTargets[i] == newInstruction.BranchTargets[i];
            if (!oldToNew.TryGetValue(oldTargetIndex, out int mappedNewTarget))
                return false;
            if (mappedNewTarget != newTargetIndex)
                return false;
        }

        return true;
    }

    static int InstructionIndexAt(ImmutableArray<DecodedInstruction> instructions, int offset)
    {
        for (int i = 0; i < instructions.Length; i++)
            if (instructions[i].Offset == offset)
                return i;
        return -1;
    }
}
