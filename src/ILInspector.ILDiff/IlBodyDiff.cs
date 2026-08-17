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

[Flags]
public enum IlBodyDiffNormalization
{
    None = 0,

    /// <summary>
    /// Fold local and argument opcode encodings into their operation families
    /// and omit raw local and argument slot numbers.
    /// </summary>
    NormalizeVariableLayout = 1 << 0,

    /// <summary>
    /// Replace references to types and members defined by each compared
    /// assembly with a shared current-assembly scope.
    /// </summary>
    NormalizeCurrentAssemblyScope = 1 << 1,

    /// <summary>
    /// Replace known platform assembly reference scopes with a shared scope.
    /// </summary>
    NormalizePlatformAssemblyScope = 1 << 2,

    // Bit 3 held the retired unsound per-side ordinal rewrite. It is deliberately not
    // reused: stale numeric callers must be rejected rather than silently selecting a
    // different normalization. Gated by
    // Compare_RejectsRetiredSynthesizedMemberOrdinalOption.

    /// <summary>
    /// Compare Roslyn compiler-generated lambdas, local functions and state machines
    /// under an ordinal-free name when the two sides correspond one-to-one.
    /// </summary>
    /// <remarks>
    /// The ordinal Roslyn embeds in these names indexes the containing type's members,
    /// so it shifts whenever that type's member population differs — which it always
    /// does when one side is a reconstructed skeleton. Requires both readers; see
    /// <see cref="CompilerGeneratedOrdinalCorrespondence"/> for why the decision is
    /// two-sided.
    /// </remarks>
    NormalizeCompilerGeneratedOrdinals = 1 << 4,
}

public enum IlBodyDiffOutcome
{
    Unavailable = 0,
    Exact,
    OperandDiff,
    OpcodeDiff,
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
    CanonicalIlOperation Operation,
    string Message);

public enum IlDiffFailureKind
{
    OldBodyMissing,
    NewBodyMissing,
    DecodeFailure,
    IdentityResolutionFailure,
    TokenResolutionFailure,
    UnsupportedBoundary,
}

public sealed record IlDiffFailureRow(
    IlDiffFailureKind Kind,
    string Message,
    string? Side = null,
    string? Detail = null);

public sealed record IlBodyDiffResult(
    IlBodyDiffOutcome Outcome,
    string? Failure,
    ImmutableArray<IlDiffRow> Rows,
    ImmutableArray<IlDiffFailureRow> FailureRows = default)
{
    public bool IsAvailable => Outcome != IlBodyDiffOutcome.Unavailable;

    public bool IsExact => Outcome == IlBodyDiffOutcome.Exact;

    public static IlBodyDiffResult OldBodyMissing(string? detail = null)
        => Failed(IlDiffFailureKind.OldBodyMissing, "old body missing", side: "old", detail);

    public static IlBodyDiffResult NewBodyMissing(string? detail = null)
        => Failed(IlDiffFailureKind.NewBodyMissing, "new body missing", side: "new", detail);

    public static IlBodyDiffResult UnsupportedBoundary(string message, string? detail = null)
        => Failed(IlDiffFailureKind.UnsupportedBoundary, message, side: null, detail);

    public static IlBodyDiffResult UnsupportedBoundary(string message, ImmutableArray<IlDiffRow> rows, string? detail = null)
        => new(
            IlBodyDiffOutcome.Unavailable,
            message,
            rows,
            [new IlDiffFailureRow(IlDiffFailureKind.UnsupportedBoundary, message, null, detail)]);

    public static IlBodyDiffResult Failed(IlDiffFailureKind kind, string message, string? side = null, string? detail = null)
        => new(
            IlBodyDiffOutcome.Unavailable,
            message,
            [],
            [new IlDiffFailureRow(kind, message, side, detail)]);
}

/// <summary>
/// Low-level IL body diff substrate over decoded instruction streams.
/// </summary>
public static partial class IlBodyDiff
{
    const IlBodyDiffNormalization SupportedNormalizations =
        IlBodyDiffNormalization.NormalizeVariableLayout
        | IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
        | IlBodyDiffNormalization.NormalizePlatformAssemblyScope
        | IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals;

    public static IlBodyDiffResult Compare(MethodInstructions oldBody, MethodInstructions newBody)
        => Compare(oldBody, newBody, oldResolver: null, newResolver: null, IlBodyDiffNormalization.None);

    public static IlBodyDiffResult Compare(
        MethodInstructions oldBody,
        MethodInstructions newBody,
        IlBodyDiffNormalization normalization)
        => Compare(oldBody, newBody, oldResolver: null, newResolver: null, normalization);

    public static IlBodyDiffResult Compare(
        MetadataReader oldReader,
        MethodBodyBlock oldBody,
        MetadataReader newReader,
        MethodBodyBlock newBody)
        => Compare(oldReader, oldBody, newReader, newBody, IlBodyDiffNormalization.None);

    public static IlBodyDiffResult Compare(
        MetadataReader oldReader,
        MethodBodyBlock oldBody,
        MetadataReader newReader,
        MethodBodyBlock newBody,
        IlBodyDiffNormalization normalization)
    {
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newReader);
        ArgumentNullException.ThrowIfNull(newBody);

        var (oldCorrespondence, newCorrespondence) =
            (normalization & IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals) != 0
                ? CompilerGeneratedOrdinalCorrespondence.Build(oldReader, newReader)
                : (CompilerGeneratedOrdinalCorrespondence.Empty, CompilerGeneratedOrdinalCorrespondence.Empty);

        return Compare(
            MethodInstructions.Decode(oldBody),
            MethodInstructions.Decode(newBody),
            new MetadataOperandResolver(oldReader, normalization, oldCorrespondence),
            new MetadataOperandResolver(newReader, normalization, newCorrespondence),
            normalization);
    }

    /// <summary>
    /// Canonicalizes a decoded body into the offset-free operation stream the diff aligns over.
    /// Exposed so the evidence adapter can reuse the exact canonicalization (opcode-family and
    /// operand-identity normalization) instead of reimplementing it.
    /// </summary>
    public static bool TryCanonicalize(
        MethodInstructions body,
        MetadataReader? reader,
        out ImmutableArray<CanonicalIlOperation> operations,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.IsComplete)
        {
            operations = [];
            failure = body.Blocks.IncompleteReason ?? "body decode failed";
            return false;
        }

        var resolver = reader is null ? null : new MetadataOperandResolver(reader, IlBodyDiffNormalization.None, CompilerGeneratedOrdinalCorrespondence.Empty);
        return TryBuildOperations(
            body.Instructions,
            resolver,
            "body",
            IlBodyDiffNormalization.None,
            out operations,
            out failure);
    }

    static IlBodyDiffResult Compare(
        MethodInstructions oldBody,
        MethodInstructions newBody,
        MetadataOperandResolver? oldResolver,
        MetadataOperandResolver? newResolver,
        IlBodyDiffNormalization normalization)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);
        if ((normalization & ~SupportedNormalizations) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalization),
                normalization,
                "Unsupported IL body diff normalization.");
        }

        if (!oldBody.IsComplete)
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.DecodeFailure,
                oldBody.Blocks.IncompleteReason ?? "old body decode failed",
                side: "old");
        if (!newBody.IsComplete)
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.DecodeFailure,
                newBody.Blocks.IncompleteReason ?? "new body decode failed",
                side: "new");

        var oldInstructions = oldBody.Instructions;
        var newInstructions = newBody.Instructions;
        if (!TryBuildOperations(oldInstructions, oldResolver, "old", normalization, out var oldOperations, out var oldFailure))
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.TokenResolutionFailure,
                oldFailure ?? "old body token resolution failed",
                side: "old");
        if (!TryBuildOperations(newInstructions, newResolver, "new", normalization, out var newOperations, out var newFailure))
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.TokenResolutionFailure,
                newFailure ?? "new body token resolution failed",
                side: "new");
        bool opcodeSequenceExact = oldOperations
            .Select(static operation => operation.OpcodeFamily)
            .SequenceEqual(newOperations.Select(static operation => operation.OpcodeFamily));
        var lcs = LongestCommonSubsequence(oldOperations, newOperations);
        var oldToNew = BuildAlignmentMap(lcs, oldOperations.Length, newOperations.Length);
        var rows = ImmutableArray.CreateBuilder<IlDiffRow>();
        int oldIndex = 0;
        int newIndex = 0;
        int hunkId = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddUnmatched(oldOperations, oldIndex, nextOld, newOperations, newIndex, nextNew, rows, ref hunkId);
            if (!BranchTargetsMatch(oldInstructions, nextOld, newInstructions, nextNew, oldToNew))
            {
                int hunk = hunkId++;
                rows.Add(Row(hunk, IlDiffKind.Remove, oldOperations[nextOld]));
                rows.Add(Row(hunk, IlDiffKind.Add, newOperations[nextNew]));
            }
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddUnmatched(oldOperations, oldIndex, oldOperations.Length, newOperations, newIndex, newOperations.Length, rows, ref hunkId);

        var diffRows = rows.ToImmutable();
        var outcome = diffRows.Length == 0
            ? IlBodyDiffOutcome.Exact
            : opcodeSequenceExact
                ? IlBodyDiffOutcome.OperandDiff
                : IlBodyDiffOutcome.OpcodeDiff;
        return new IlBodyDiffResult(outcome, Failure: null, diffRows);
    }

    static bool TryBuildOperations(
        ImmutableArray<DecodedInstruction> instructions,
        MetadataOperandResolver? resolver,
        string side,
        IlBodyDiffNormalization normalization,
        out ImmutableArray<CanonicalIlOperation> operations,
        out string? failure)
    {
        var builder = ImmutableArray.CreateBuilder<CanonicalIlOperation>(instructions.Length);
        foreach (var instruction in instructions)
        {
            if (!TryToOperation(instruction, resolver, normalization, out var operation, out var operationFailure))
            {
                operations = [];
                failure = $"{side} body {operationFailure}";
                return false;
            }

            builder.Add(operation);
        }

        operations = builder.MoveToImmutable();
        failure = null;
        return true;
    }

    static void AddUnmatched(
        ImmutableArray<CanonicalIlOperation> oldOperations,
        int oldStart,
        int oldEnd,
        ImmutableArray<CanonicalIlOperation> newOperations,
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
            rows.Add(Row(hunk, IlDiffKind.Remove, oldOperations[oldIndex++]));
        while (newIndex < newEnd)
            rows.Add(Row(hunk, IlDiffKind.Add, newOperations[newIndex++]));
    }

    static IlDiffRow Row(int hunkId, IlDiffKind kind, CanonicalIlOperation operation)
        => new(hunkId, kind, operation, Message(kind, operation));

    static string Message(IlDiffKind kind, CanonicalIlOperation operation)
    {
        string action = kind switch
        {
            IlDiffKind.Add => "Added",
            IlDiffKind.Remove => "Removed",
            IlDiffKind.Context => "Unchanged",
            _ => kind.ToString(),
        };
        string subject = operation.Operand?.Kind is IlOperandIdentityKind.BranchTarget or IlOperandIdentityKind.SwitchTargets
            ? "IL branch"
            : "IL operation";
        return $"{action} {subject} '{operation.Display}'";
    }

    static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(
        ImmutableArray<CanonicalIlOperation> oldOperations,
        ImmutableArray<CanonicalIlOperation> newOperations)
    {
        var lengths = new int[oldOperations.Length + 1, newOperations.Length + 1];
        for (int oldIndex = oldOperations.Length - 1; oldIndex >= 0; oldIndex--)
        {
            for (int newIndex = newOperations.Length - 1; newIndex >= 0; newIndex--)
            {
                lengths[oldIndex, newIndex] = CanonicalEquals(oldOperations[oldIndex], newOperations[newIndex])
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        var pairs = new List<(int OldIndex, int NewIndex)>();
        int i = 0;
        int j = 0;
        while (i < oldOperations.Length && j < newOperations.Length)
        {
            if (CanonicalEquals(oldOperations[i], newOperations[j]))
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

    /// <summary>
    /// The old→new instruction-index alignment map (LCS anchors plus equal-sized gap pairs) that
    /// branch-target validation uses. Exposed so the evidence adapter reuses the exact alignment
    /// instead of reconstructing it.
    /// </summary>
    public static IReadOnlyDictionary<int, int> BuildAlignmentMap(
        ImmutableArray<CanonicalIlOperation> oldOperations,
        ImmutableArray<CanonicalIlOperation> newOperations)
    {
        var lcs = LongestCommonSubsequence(oldOperations, newOperations);
        return BuildAlignmentMap(lcs, oldOperations.Length, newOperations.Length);
    }

    static IReadOnlyDictionary<int, int> BuildAlignmentMap(
        IReadOnlyList<(int OldIndex, int NewIndex)> lcs,
        int oldLength,
        int newLength)
    {
        var map = new Dictionary<int, int>();
        int oldIndex = 0;
        int newIndex = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddGapPairs(map, oldIndex, nextOld, newIndex, nextNew);
            map[nextOld] = nextNew;
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddGapPairs(map, oldIndex, oldLength, newIndex, newLength);
        return map;
    }

    static void AddGapPairs(
        Dictionary<int, int> map,
        int oldStart,
        int oldEnd,
        int newStart,
        int newEnd)
    {
        int oldCount = oldEnd - oldStart;
        int newCount = newEnd - newStart;
        if (oldCount != newCount)
            return;

        for (int i = 0; i < oldCount; i++)
            map[oldStart + i] = newStart + i;
    }

    static bool CanonicalEquals(CanonicalIlOperation oldOperation, CanonicalIlOperation newOperation)
    {
        if (oldOperation.OpcodeFamily != newOperation.OpcodeFamily)
            return false;
        if (oldOperation.Operand?.Kind is IlOperandIdentityKind.BranchTarget)
            return true;
        if (oldOperation.Operand?.Kind is IlOperandIdentityKind.SwitchTargets)
            return oldOperation.Operand.Value.Split(',').Length == newOperation.Operand?.Value.Split(',').Length;
        return oldOperation.Operand == newOperation.Operand;
    }

    static bool TryToOperation(
        DecodedInstruction instruction,
        MetadataOperandResolver? resolver,
        IlBodyDiffNormalization normalization,
        out CanonicalIlOperation operation,
        out string? failure)
    {
        if (!TryOperandIdentity(instruction, resolver, normalization, out var operand, out failure))
        {
            operation = new CanonicalIlOperation(
                instruction.Offset,
                OpcodeFamily(instruction, normalization),
                Operand: null);
            return false;
        }

        operation = new CanonicalIlOperation(
            instruction.Offset,
            OpcodeFamily(instruction, normalization),
            operand);
        return true;
    }

    static string OpcodeFamily(
        DecodedInstruction instruction,
        IlBodyDiffNormalization normalization)
    {
        var opcode = instruction.OpCode;
        if ((normalization & IlBodyDiffNormalization.NormalizeVariableLayout) != 0)
        {
            if (opcode is ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2
                or ILOpCode.Ldarg_3 or ILOpCode.Ldarg_s or ILOpCode.Ldarg)
            {
                return "ldarg";
            }
            if (opcode is ILOpCode.Ldarga_s or ILOpCode.Ldarga)
                return "ldarga";
            if (opcode is ILOpCode.Starg_s or ILOpCode.Starg)
                return "starg";
            if (opcode is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2
                or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc)
            {
                return "ldloc";
            }
            if (opcode is ILOpCode.Ldloca_s or ILOpCode.Ldloca)
                return "ldloca";
            if (opcode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc)
            {
                return "stloc";
            }
        }

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

    static bool TryOperandIdentity(
        DecodedInstruction instruction,
        MetadataOperandResolver? resolver,
        IlBodyDiffNormalization normalization,
        out IlOperandIdentity? operand,
        out string? failure)
    {
        if (IsMetadataTokenOperand(instruction.Operand))
        {
            if (resolver is null)
            {
                operand = null;
                failure = $"metadata token operand at IL_{instruction.Offset:X4} requires a MetadataReader-backed comparison";
                return false;
            }

            return resolver.TryResolve(instruction, out operand, out failure);
        }

        operand = ImmediateOperandIdentity(instruction)
            ?? OperandIdentityByKind(instruction, normalization);
        failure = null;
        return true;
    }

    static IlOperandIdentity? ImmediateOperandIdentity(DecodedInstruction instruction)
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
            _ => null,
        };

    static IlOperandIdentity? OperandIdentityByKind(
        DecodedInstruction instruction,
        IlBodyDiffNormalization normalization)
        => instruction.Operand switch
        {
            OperandKind.ShortInlineI or OperandKind.InlineI or OperandKind.InlineI8
                or OperandKind.ShortInlineR or OperandKind.InlineR
                => new(IlOperandIdentityKind.Immediate, instruction.OperandValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            OperandKind.ShortInlineVar or OperandKind.InlineVar
                when (normalization & IlBodyDiffNormalization.NormalizeVariableLayout) == 0
                => new(IlOperandIdentityKind.Slot, instruction.OperandValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget
                => new(IlOperandIdentityKind.BranchTarget, $"IL_{instruction.BranchTargets[0]:X4}"),
            OperandKind.InlineSwitch
                => new(IlOperandIdentityKind.SwitchTargets, string.Join(", ", instruction.BranchTargets.Select(target => $"IL_{target:X4}"))),
            _ => null,
        };

    static bool IsMetadataTokenOperand(OperandKind kind)
        => kind is OperandKind.InlineString or OperandKind.InlineMethod or OperandKind.InlineField
            or OperandKind.InlineType or OperandKind.InlineSig or OperandKind.InlineTok;

    /// <summary>
    /// True when a matched branch/switch operation's targets still correspond under the given
    /// alignment map (i.e. the branch was not retargeted). Exposed so the evidence adapter reuses
    /// this decision verbatim rather than reimplementing it.
    /// </summary>
    public static bool BranchTargetsMatch(
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
