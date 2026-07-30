using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

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

    /// <summary>
    /// Replace the containing-method ordinal inside Roslyn-synthesized closure
    /// member names with <c>#</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Roslyn names a lambda's cache field <c>&lt;&gt;9__N_M</c>, its method
    /// <c>&lt;Name&gt;b__N_M</c>, and a local function
    /// <c>&lt;Name&gt;g__Local|N_M</c>, where <c>N</c> is the containing
    /// method's declaration ordinal in the <em>compilation unit</em> and
    /// <c>M</c> indexes the lambda within that method. <c>N</c> therefore
    /// describes the unit, not the member: recompiling the same source in a
    /// unit whose member ordering or member count differs renumbers it while
    /// the emitted IL stays behaviorally identical.
    /// </para>
    /// <para>
    /// Only <c>N</c> is replaced. The containing-method name, the local
    /// function name, and the per-method index <c>M</c> all stay significant,
    /// so a body that binds to the wrong lambda, or to a lambda of a
    /// differently named method, still diffs.
    /// </para>
    /// <para>
    /// Applies to a member's simple name only, never to the rest of a
    /// formatted operand. Declaring types, return types, parameter types,
    /// generic arguments, standalone signatures, and string literals are left
    /// alone, so the rewrite cannot reach text that is not a member name.
    /// Within that name the match is anchored at both ends: the whole name
    /// must be one of the three forms. A synthesized-looking <em>substring</em>
    /// is never rewritten, so names that arrive from a producer other than C#
    /// (<c>x!&lt;Run&gt;b__1_0</c>, <c>&lt;Run&gt;b__1_0!suffix</c>) keep
    /// comparing literally. The form must also match the member's metadata
    /// table — the cache form is accepted only on a field and the lambda and
    /// local-function forms only on a method — and both indices must be
    /// spelled the way Roslyn spells them, as canonical non-negative
    /// <see cref="int"/> values, so <c>&lt;Run&gt;b__0103_0</c> and a
    /// 30-digit ordinal are not recognized either.
    /// </para>
    /// <para>
    /// Known limitation, deliberately not addressed: overloads share a
    /// containing-method name and are told apart only by <c>N</c>, so this
    /// option conflates the closures of two overloads with the same lambda
    /// index. State-machine names (<c>&lt;Name&gt;d__N</c>) are left alone for
    /// the same reason — <c>N</c> is their only distinguishing component.
    /// Synthesized <em>types</em> that do carry the ordinal — display classes
    /// (<c>&lt;&gt;c__DisplayClassN_M</c>) and the state machine of an async
    /// lambda (<c>&lt;&lt;Run&gt;b__N_M&gt;d__1</c>) — keep it, because they
    /// are types rather than members. Each of these costs a false positive,
    /// never a masked difference.
    /// </para>
    /// <para>
    /// Every rule that can change what this option relates is enforced by a
    /// gate in <c>IlBodyDiffNormalizationTests</c>, and each was confirmed
    /// load-bearing by neutering it individually and observing that gate — and
    /// only that gate — fail. <c>ToleratesContainingMethodRenumbering</c> and
    /// <c>ToleratesRenumberingOfALambdaCacheField</c> cover what is related;
    /// each asserts the two names differ <em>without</em> the option as well
    /// as agreeing with it, so neither can pass vacuously in either
    /// direction. The remaining gates assert only that the names still differ
    /// <em>with</em> the option, which is the whole claim for a name this
    /// option must not relate: <c>PreservesEveryOtherNameComponent</c> for the
    /// components that stay significant, the anchoring, every component of the
    /// grammar, and both canonical-ordinal rules;
    /// <c>RejectsAFieldFormOnAMethod</c> and
    /// <c>RejectsAMethodFormOnAField</c> for the kind correspondence;
    /// <c>RejectsAMethodFormBehindAMethodSpecificationOnAField</c> for the
    /// generic-instantiation path;
    /// <c>RejectsANonCanonicalCacheFieldOrdinal</c> for the cache field's
    /// ordinal spelling; <c>LeavesTypeOperandsAlone</c> and
    /// <c>PreservesSynthesizedLikeStringLiterals</c> for the scope; and
    /// <c>StopsNormalizingPastTheNestingCap</c> for the depth bound.
    /// </para>
    /// <para>
    /// Four checks are deliberately <em>not</em> gated, because they are
    /// redundant fast paths rather than rules: the length floor, the leading
    /// <c>&lt;</c> pre-check, and the <c>__</c> pre-check in
    /// <c>Normalize</c>, and the empty-span check in
    /// <c>IsCanonicalOrdinal</c>. Each is subsumed by a check that follows it,
    /// so neutering one changes no observable behavior and no test can
    /// distinguish it. They are listed here so that a future reader does not
    /// mistake their absence from the gate list for an ungated property.
    /// </para>
    /// </remarks>
    NormalizeSynthesizedMemberOrdinals = 1 << 3,
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
public static class IlBodyDiff
{
    const IlBodyDiffNormalization SupportedNormalizations =
        IlBodyDiffNormalization.NormalizeVariableLayout
        | IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
        | IlBodyDiffNormalization.NormalizePlatformAssemblyScope
        | IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals;

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

        return Compare(
            MethodInstructions.Decode(oldBody),
            MethodInstructions.Decode(newBody),
            new MetadataOperandResolver(oldReader, normalization),
            new MetadataOperandResolver(newReader, normalization),
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

        var resolver = reader is null ? null : new MetadataOperandResolver(reader, IlBodyDiffNormalization.None);
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

    sealed class MetadataOperandResolver(
        MetadataReader reader,
        IlBodyDiffNormalization normalization)
    {
        // Malformed metadata can make a declaring type or resolution-scope chain cyclic,
        // so the type-name climbs below would recurse until an uncatchable
        // StackOverflowException (which TryResolve's catch cannot intercept). Cap the climb
        // ([ThreadStatic] for thread safety) and degrade to the leaf name past the cap.
        [ThreadStatic]
        static int s_climbDepth;
        const int MaxClimbDepth = 256;

        public bool TryResolve(DecodedInstruction instruction, out IlOperandIdentity? operand, out string? failure)
        {
            try
            {
                string value = instruction.Operand switch
                {
                    OperandKind.InlineString => ResolveString((int)instruction.OperandValue),
                    OperandKind.InlineMethod => ResolveMethod((int)instruction.OperandValue),
                    OperandKind.InlineField => ResolveField((int)instruction.OperandValue),
                    OperandKind.InlineType => ResolveType((int)instruction.OperandValue),
                    OperandKind.InlineTok => ResolveToken((int)instruction.OperandValue),
                    OperandKind.InlineSig => ResolveSignature((int)instruction.OperandValue),
                    _ => throw new InvalidOperationException($"Operand kind {instruction.Operand} is not a metadata token."),
                };
                if (reader.IsAssembly && instruction.Operand != OperandKind.InlineString)
                {
                    string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);
                    value = NormalizeAssemblyScopes(value, assembly);
                }
                operand = new IlOperandIdentity(IlOperandIdentityKind.Token, value);
                failure = null;
                return true;
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                operand = null;
                failure = $"metadata token operand at IL_{instruction.Offset:X4} could not be resolved: {ex.Message}";
                return false;
            }
        }

        string ResolveString(int token)
        {
            var handle = MetadataTokens.UserStringHandle(token);
            return $"string \"{Escape(reader.GetUserString(handle))}\"";
        }

        string ResolveMethod(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodDefinition((MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberReference((MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => FormatMethodSpecification((MethodSpecificationHandle)handle),
                _ => throw new BadImageFormatException($"Expected method token, got {handle.Kind}."),
            };
        }

        string ResolveField(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.FieldDefinition => FormatFieldDefinition((FieldDefinitionHandle)handle),
                HandleKind.MemberReference => FormatFieldMemberReference((MemberReferenceHandle)handle),
                _ => throw new BadImageFormatException($"Expected field token, got {handle.Kind}."),
            };
        }

        string ResolveType(int token)
            => FormatType(MetadataTokens.EntityHandle(token));

        string ResolveToken(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => $"type {FormatType(handle)}",
                HandleKind.MethodDefinition => $"method {FormatMethodDefinition((MethodDefinitionHandle)handle)}",
                HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)handle).GetKind() == MemberReferenceKind.Method
                    => $"method {FormatMemberReference((MemberReferenceHandle)handle)}",
                HandleKind.MemberReference => $"field {FormatFieldMemberReference((MemberReferenceHandle)handle)}",
                HandleKind.MethodSpecification => $"method {FormatMethodSpecification((MethodSpecificationHandle)handle)}",
                HandleKind.FieldDefinition => $"field {FormatFieldDefinition((FieldDefinitionHandle)handle)}",
                _ => throw new BadImageFormatException($"Unsupported ldtoken handle kind {handle.Kind}."),
            };
        }

        string ResolveSignature(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                throw new BadImageFormatException($"Expected standalone signature token, got {handle.Kind}.");

            var signature = reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            return $"signature {Convert.ToHexString(reader.GetBlobBytes(signature.Signature))}";
        }

        string FormatMethodDefinition(MethodDefinitionHandle handle)
        {
            var method = reader.GetMethodDefinition(handle);
            var signature = GuardedProviderDecode.TryMethod(
                reader,
                method,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, method.Signature));
            return FormatCall(signature, FormatType(method.GetDeclaringType()), NormalizeMemberName(reader.GetString(method.Name), SynthesizedMemberKind.Method), genericArgs: null);
        }

        string FormatMemberReference(MemberReferenceHandle handle)
        {
            var member = reader.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Method)
                throw new BadImageFormatException("Expected method member reference.");

            var signature = GuardedProviderDecode.TryMemberRefMethod(
                reader,
                member,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, member.Signature));
            return FormatCall(signature, FormatMemberParent(member.Parent), NormalizeMemberName(reader.GetString(member.Name), SynthesizedMemberKind.Method), genericArgs: null);
        }

        string FormatMethodSpecification(MethodSpecificationHandle handle)
        {
            var spec = reader.GetMethodSpecification(handle);
            var typeArguments = GuardedProviderDecode.TryMethodSpec(
                reader,
                spec,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : [GuardedProviderDecode.RejectedIdentity(reader, spec.Signature)];
            string genericArgs = $"<{string.Join(", ", typeArguments)}>";

            return spec.Method.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodSpecificationDefinition((MethodDefinitionHandle)spec.Method, genericArgs),
                HandleKind.MemberReference => FormatMethodSpecificationReference((MemberReferenceHandle)spec.Method, genericArgs),
                _ => throw new BadImageFormatException($"Unsupported method specification target {spec.Method.Kind}."),
            };
        }

        string FormatMethodSpecificationDefinition(MethodDefinitionHandle handle, string genericArgs)
        {
            var method = reader.GetMethodDefinition(handle);
            var signature = GuardedProviderDecode.TryMethod(
                reader,
                method,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, method.Signature));
            return FormatCall(signature, FormatType(method.GetDeclaringType()), NormalizeMemberName(reader.GetString(method.Name), SynthesizedMemberKind.Method), genericArgs);
        }

        string FormatMethodSpecificationReference(MemberReferenceHandle handle, string genericArgs)
        {
            var member = reader.GetMemberReference(handle);

            // Same check as the direct member-reference path. A method
            // specification can name a member reference that is actually a
            // field, and without this the generic-instantiation path would
            // normalize a method-form name on a field. Gated by
            // IlBodyDiffNormalizationTests.NormalizeSynthesizedMemberOrdinals_RejectsAMethodFormBehindAMethodSpecificationOnAField.
            if (member.GetKind() != MemberReferenceKind.Method)
                throw new BadImageFormatException("Expected method member reference.");

            var signature = GuardedProviderDecode.TryMemberRefMethod(
                reader,
                member,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.FallbackSignature(
                    GuardedProviderDecode.RejectedIdentity(reader, member.Signature));
            return FormatCall(signature, FormatMemberParent(member.Parent), NormalizeMemberName(reader.GetString(member.Name), SynthesizedMemberKind.Method), genericArgs);
        }

        string FormatFieldDefinition(FieldDefinitionHandle handle)
        {
            var field = reader.GetFieldDefinition(handle);
            string fieldType = GuardedProviderDecode.TryField(
                reader,
                field,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, field.Signature);
            return $"{fieldType} {FormatType(field.GetDeclaringType())}::{NormalizeMemberName(reader.GetString(field.Name), SynthesizedMemberKind.Field)}";
        }

        string FormatFieldMemberReference(MemberReferenceHandle handle)
        {
            var member = reader.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Field)
                throw new BadImageFormatException("Expected field member reference.");

            string fieldType = GuardedProviderDecode.TryMemberRefField(
                reader,
                member,
                SignatureIdentityProvider.Instance,
                context: null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, member.Signature);
            return $"{fieldType} {FormatMemberParent(member.Parent)}::{NormalizeMemberName(reader.GetString(member.Name), SynthesizedMemberKind.Field)}";
        }

        string FormatCall(MethodSignature<string> signature, string parent, string name, string? genericArgs)
        {
            string instance = signature.Header.IsInstance ? "instance " : "";
            string convention = CallingConventionPrefix(signature.Header.CallingConvention);
            string arity = signature.GenericParameterCount > 0
                ? $"`{signature.GenericParameterCount}"
                : "";
            return $"{instance}{convention}{signature.ReturnType} {parent}::{name}{arity}{genericArgs}({FormatParameterList(signature)})";
        }

        string FormatMemberParent(EntityHandle parent)
            => parent.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => FormatType(parent),
                _ => throw new BadImageFormatException($"Unsupported member parent {parent.Kind}."),
            };

        string FormatType(EntityHandle handle)
            => handle.Kind switch
            {
                HandleKind.TypeDefinition => FormatTypeDefinition((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => FormatTypeReference((TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => FormatTypeSpecification((TypeSpecificationHandle)handle),
                _ => throw new BadImageFormatException($"Unsupported type handle {handle.Kind}."),
            };

        string FormatTypeSpecification(TypeSpecificationHandle handle)
        {
            var specification = reader.GetTypeSpecification(handle);
            return GuardedProviderDecode.TryTypeSpec(
                reader,
                handle,
                SignatureIdentityProvider.Instance,
                null,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, specification.Signature);
        }

        string FormatTypeDefinition(TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            string fullName;
            if (!declaring.IsNil && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { fullName = $"{FormatTypeDefinition(declaring)}+{name}"; }
                finally { s_climbDepth--; }
            }
            else
            {
                fullName = Dotted(reader.GetString(type.Namespace), name);
            }
            return $"[{CurrentAssemblyName()}]{fullName}";
        }

        string FormatTypeReference(TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            string fullName = Dotted(reader.GetString(type.Namespace), name);
            if (type.ResolutionScope.Kind == HandleKind.AssemblyReference)
                return $"[{AssemblyReferenceIdentity(reader, (AssemblyReferenceHandle)type.ResolutionScope)}]{fullName}";
            if (type.ResolutionScope.Kind == HandleKind.TypeReference && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { return $"{FormatTypeReference((TypeReferenceHandle)type.ResolutionScope)}+{fullName}"; }
                finally { s_climbDepth--; }
            }
            return $"[{CurrentAssemblyName()}]{fullName}";
        }

        string CurrentAssemblyName()
            => (normalization & IlBodyDiffNormalization.NormalizeCurrentAssemblyScope) != 0
                ? "<current>"
                : reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";

        static string Dotted(string ns, string name)
            => ns.Length == 0 ? name : $"{ns}.{name}";

        static string Escape(string value)
            => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        string NormalizeAssemblyScopes(string value, string currentAssembly)
        {
            bool normalizeCurrent =
                (normalization & IlBodyDiffNormalization.NormalizeCurrentAssemblyScope) != 0;
            bool normalizePlatform =
                (normalization & IlBodyDiffNormalization.NormalizePlatformAssemblyScope) != 0;
            if (!normalizeCurrent && !normalizePlatform)
                return value;

            StringBuilder? normalized = null;
            int copied = 0;
            int open = value.IndexOf('[', StringComparison.Ordinal);
            while (open >= 0)
            {
                int close = value.IndexOf(']', open + 1);
                if (close < 0)
                    break;

                ReadOnlySpan<char> identity = value.AsSpan(open + 1, close - open - 1);
                int comma = identity.IndexOf(',');
                ReadOnlySpan<char> name = comma >= 0 ? identity[..comma] : identity;
                string? normalizedScope = normalizeCurrent && name.Equals(currentAssembly, StringComparison.Ordinal)
                    ? "<current>"
                    : normalizePlatform && !name.Equals(currentAssembly, StringComparison.Ordinal)
                        && IsPlatformAssembly(name)
                            ? "<platform>"
                            : null;
                if (normalizedScope is not null)
                {
                    normalized ??= new StringBuilder(value.Length);
                    normalized.Append(value, copied, open - copied);
                    normalized.Append('[').Append(normalizedScope).Append(']');
                    copied = close + 1;
                }

                open = value.IndexOf('[', close + 1);
            }

            if (normalized is null)
                return value;

            normalized.Append(value, copied, value.Length - copied);
            return normalized.ToString();
        }

        static bool IsPlatformAssembly(ReadOnlySpan<char> name)
            => name.Equals("mscorlib", StringComparison.Ordinal)
                || name.Equals("netstandard", StringComparison.Ordinal)
                || name.Equals("System", StringComparison.Ordinal)
                || name.StartsWith("System.", StringComparison.Ordinal)
                || name.Equals("Microsoft.CSharp", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.VisualBasic", StringComparison.Ordinal);

        /// <summary>
        /// Applies <see cref="IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals"/>
        /// to a member's simple name, taken straight from the metadata string
        /// heap.
        /// </summary>
        /// <remarks>
        /// The normalization is applied here, to the typed identity, rather
        /// than to the formatted operand string. A formatted operand also
        /// carries declaring types, return types, parameter types, and generic
        /// arguments, and scanning that flattened text would let the rewrite
        /// reach names that are not members at all. Scoping it to the member
        /// name is what makes the flag mean what it says.
        /// <para>
        /// The corollary is that synthesized <em>type</em> names keep their
        /// ordinals: display classes (<c>&lt;&gt;c__DisplayClassN_M</c>) and
        /// the state machine of an async lambda
        /// (<c>&lt;&lt;Run&gt;b__N_M&gt;d__1</c>) are types, so a body that
        /// references one can still diff on a renumbered ordinal. That costs a
        /// false positive, never a masked difference, and no corpus row needs
        /// it today. Covering it means normalizing type leaf names too, which
        /// requires threading this option through
        /// <see cref="SignatureIdentityProvider"/>.
        /// </para>
        /// </remarks>
        string NormalizeMemberName(string name, SynthesizedMemberKind kind)
            => (normalization & IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals) != 0
                ? SynthesizedOrdinals.Normalize(name, kind)
                : name;
    }

    /// <summary>
    /// Which metadata table a member name came from. Roslyn emits the lambda
    /// cache form <c>&lt;&gt;9__N_M</c> only as a field and the lambda and
    /// local-function forms only as methods, so the normalizer needs the kind
    /// to hold names to that correspondence.
    /// </summary>
    internal enum SynthesizedMemberKind
    {
        Field,
        Method,
    }

    /// <summary>
    /// Rewrites the containing-method ordinal out of Roslyn closure member
    /// names. See
    /// <see cref="IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals"/>
    /// for what the ordinal means and why it is not evidence.
    /// </summary>
    /// <remarks>
    /// The grammar is matched from the start of the identifier rather than by
    /// scanning for <c>__</c>. Anchoring matters: a scan reaches separators
    /// inside the enclosing method's own name, so an authored method named
    /// <c>b__1_0</c> would have its digits rewritten and would then compare
    /// equal to an authored <c>b__2_0</c>. Anchoring also removes the need to
    /// guess where an identifier begins, which is what previously left
    /// <c>&lt;.ctor&gt;b__1_0</c> unnormalized.
    /// </remarks>
    internal static class SynthesizedOrdinals
    {
        internal const char Placeholder = '#';

        /// <summary>
        /// Caps how deep the enclosing-name recursion goes. Roslyn nests
        /// closure names only as deep as the source nests lambdas, so a
        /// handful of levels covers real input. The cap exists because member
        /// names come from untrusted metadata, where an adversarially nested
        /// name would otherwise recurse once per level and overflow the stack
        /// (see docs/design/untrusted-data-threat-model.md, which requires
        /// recursion over hostile input to be bounded). Past the cap the
        /// enclosing name is left literal, which can only cost a false
        /// positive, never a masked difference.
        /// </summary>
        const int MaxNestingDepth = 16;

        /// <summary>
        /// Shortest recognized form, <c>&lt;&gt;9__0_0</c>.
        /// </summary>
        const int MinNameLength = 8;

        /// <summary>
        /// Normalizes a member's simple name when the <em>whole</em> name is
        /// one of the recognized synthesized forms (for example
        /// <c>&lt;Run&gt;b__103_0</c>), rewriting only the containing-method
        /// ordinal. Any other name is returned unchanged.
        /// </summary>
        public static string Normalize(string value, SynthesizedMemberKind kind)
            => Normalize(value, kind, depth: 0);

        static string Normalize(string value, SynthesizedMemberKind kind, int depth)
        {
            // Cheap rejects, subsumed by the grammar below rather than adding
            // to it. The shortest recognized form is `<>9__0_0`, every form
            // opens with '<', which C# cannot spell, and every form carries
            // '__'. `TryNormalizeName` declines each of these on its own, so
            // no test distinguishes their presence — they exist to skip the
            // scan on the overwhelming majority of names, not to reject
            // anything the grammar would otherwise accept. Widening one is
            // therefore safe; narrowing one is not, since a form the grammar
            // accepts must be able to reach it.
            if (value.Length < MinNameLength
                || value[0] != '<'
                || !value.Contains("__", StringComparison.Ordinal))
            {
                return value;
            }

            return TryNormalizeName(value, kind, depth, out string replacement) ? replacement : value;
        }

        /// <summary>
        /// Matches <paramref name="value"/> in its entirety against
        /// <c>&lt;&gt;9__N_M</c>, <c>&lt;Name&gt;b__N_M</c>, or
        /// <c>&lt;Name&gt;g__Local|N_M</c> and rewrites only <c>N</c>. Any
        /// other name, including the state-machine form
        /// <c>&lt;Name&gt;d__N</c>, is declined.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The match is anchored at both ends on purpose. These names arrive
        /// from arbitrary metadata, not only from Roslyn, so recognizing a
        /// synthesized-looking <em>substring</em> would let unrelated names
        /// collapse: <c>x!&lt;Run&gt;b__103_0</c> and
        /// <c>&lt;Run&gt;b__103_0!suffix</c> are legal metadata names that no
        /// C# compiler emits, and normalizing an ordinal buried inside them
        /// would equate members that genuinely differ.
        /// </para>
        /// <para>
        /// Anchoring also bounds the work. There is exactly one candidate
        /// start, so each nesting level performs one angle scan plus at most
        /// one separator scan over a disjoint part of the same string, and the
        /// depth cap bounds the levels — linear overall, with no per-candidate
        /// rescan for a hostile name to amplify
        /// (see docs/design/untrusted-data-threat-model.md).
        /// </para>
        /// </remarks>
        static bool TryNormalizeName(string value, SynthesizedMemberKind kind, int depth, out string replacement)
        {
            replacement = "";

            // The enclosing name may itself be synthesized (a lambda inside a
            // lambda, or one inside top-level statements' `<Main>$`), so match
            // the '>' that closes this name rather than the first one.
            int close = FindClosingAngle(value);
            if (close < 0)
                return false;

            int marker = close + 1;
            if (marker + 2 >= value.Length
                || value[marker] is not ('9' or 'b' or 'g')
                || value[marker + 1] != '_'
                || value[marker + 2] != '_')
            {
                return false;
            }

            // Roslyn emits the cache form `<>9__N_M` only as a field and the
            // lambda and local-function forms only as methods. Holding names to
            // that correspondence keeps a *method* named `<>9__1_0` — which no
            // C# compiler emits — comparing literally. Gated by
            // IlBodyDiffNormalizationTests.NormalizeSynthesizedMemberOrdinals_RejectsAFieldFormOnAMethod
            // and ..._RejectsAMethodFormOnAField.
            var markerKind = value[marker] == '9' ? SynthesizedMemberKind.Field : SynthesizedMemberKind.Method;
            if (kind != markerKind)
                return false;

            // Roslyn omits the containing-method name only for the lambda
            // cache field `<>9__N_M`; the lambda and local-function forms
            // always carry one. Pinning that correspondence keeps names no C#
            // compiler emits, such as `<>b__1_0`, comparing literally.
            bool namesContainingMethod = close > 1;
            if (namesContainingMethod != (value[marker] is 'b' or 'g'))
                return false;

            int digitsStart = marker + 3;
            if (value[marker] == 'g')
            {
                // Local function: the ordinal follows the '|' that terminates
                // the local's own name, which cannot contain '|'. The name
                // must be non-empty, so the separator cannot sit immediately
                // after `g__`.
                int bar = value.IndexOf('|', digitsStart);
                if (bar <= digitsStart)
                    return false;
                digitsStart = bar + 1;
            }

            int digitsEnd = digitsStart;
            while (digitsEnd < value.Length && char.IsAsciiDigit(value[digitsEnd]))
                digitsEnd++;

            // Require at least one digit followed by `_M`. That trailing index
            // is what separates a closure name from an identifier that merely
            // ends in digits, and it stays significant so a lambda bound to the
            // wrong slot still differs.
            if (!IsCanonicalOrdinal(value, digitsStart, digitsEnd)
                || digitsEnd >= value.Length
                || value[digitsEnd] != '_')
            {
                return false;
            }

            // `M` must be digits that run to the end of the name. Roslyn emits
            // nothing after the per-method index, so a name that continues is
            // not one of these forms and must keep comparing literally.
            int lambdaStart = digitsEnd + 1;
            int lambdaEnd = lambdaStart;
            while (lambdaEnd < value.Length && char.IsAsciiDigit(value[lambdaEnd]))
                lambdaEnd++;

            if (lambdaEnd != value.Length || !IsCanonicalOrdinal(value, lambdaStart, lambdaEnd))
                return false;

            // The enclosing name carries its own ordinal when it is itself a
            // closure, so normalize it under the same grammar. Recursing rather
            // than rescanning is what keeps an authored enclosing method named
            // `b__1_0` distinct from one named `b__2_0`. A containing name is
            // always a method, whatever kind the outer member is.
            string inner = value[1..close];
            string normalizedInner = depth < MaxNestingDepth
                ? Normalize(inner, SynthesizedMemberKind.Method, depth + 1)
                : inner;

            replacement = $"<{normalizedInner}{value[close..digitsStart]}{Placeholder}{value[digitsEnd..]}";
            return true;
        }

        /// <summary>
        /// Reports whether <c>value[start..end]</c> is an ordinal exactly as
        /// Roslyn spells one.
        /// </summary>
        /// <remarks>
        /// Roslyn formats these indices with an invariant <see cref="int"/>
        /// conversion, so <c>0103</c> and a value past <see cref="int.MaxValue"/>
        /// are not forms it can emit. Accepting them would let
        /// <c>&lt;Run&gt;b__0103_0</c> and <c>&lt;Run&gt;b__0128_0</c>
        /// collapse — a masked difference between two names that no compiler
        /// produced and that nothing else relates. Requiring the canonical
        /// encoding costs at most a false positive on such a name, which is
        /// the safe direction.
        /// <para>
        /// The two rules are gated separately, because a padded ordinal is
        /// rejected by the leading-zero rule before the parse is reached and
        /// would leave the range rule untested.
        /// <c>IlBodyDiffNormalizationTests.NormalizeSynthesizedMemberOrdinals_PreservesEveryOtherNameComponent</c>
        /// covers both rules against both indices — a leading zero and a
        /// value past <see cref="int.MaxValue"/> with no leading zero — and
        /// <c>..._RejectsANonCanonicalCacheFieldOrdinal</c> covers both
        /// against the field form.
        /// </para>
        /// </remarks>
        static bool IsCanonicalOrdinal(string value, int start, int end)
        {
            int length = end - start;

            // Redundant with the parse below, which also rejects an empty
            // span; kept because it states the intent at the top.
            if (length == 0)
                return false;

            // "0" is canonical; "0" followed by anything is not.
            if (length > 1 && value[start] == '0')
                return false;

            return int.TryParse(
                value.AsSpan(start, length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _);
        }

        /// <summary>
        /// Returns the index of the <c>&gt;</c> closing the <c>&lt;</c> at
        /// index 0, honoring nesting, or -1 when unbalanced.
        /// </summary>
        static int FindClosingAngle(string value)
        {
            int depth = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '<')
                {
                    depth++;
                }
                else if (value[i] == '>' && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    sealed class SignatureIdentityProvider : ISignatureTypeProvider<string, object?>
    {
        public static SignatureIdentityProvider Instance { get; } = new();

        // Malformed metadata can make a declaring type or resolution-scope chain cyclic, so
        // the TypeName climbs below would recurse until an uncatchable StackOverflowException.
        // Cap the climb ([ThreadStatic] so the shared Instance stays thread-safe) and degrade
        // to the leaf name past the cap.
        [ThreadStatic]
        static int s_climbDepth;
        const int MaxClimbDepth = 256;

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode switch
            {
                PrimitiveTypeCode.Void => "void",
                PrimitiveTypeCode.Boolean => "bool",
                PrimitiveTypeCode.Char => "char",
                PrimitiveTypeCode.SByte => "int8",
                PrimitiveTypeCode.Byte => "uint8",
                PrimitiveTypeCode.Int16 => "int16",
                PrimitiveTypeCode.UInt16 => "uint16",
                PrimitiveTypeCode.Int32 => "int32",
                PrimitiveTypeCode.UInt32 => "uint32",
                PrimitiveTypeCode.Int64 => "int64",
                PrimitiveTypeCode.UInt64 => "uint64",
                PrimitiveTypeCode.Single => "float32",
                PrimitiveTypeCode.Double => "float64",
                PrimitiveTypeCode.String => "string",
                PrimitiveTypeCode.Object => "object",
                PrimitiveTypeCode.IntPtr => "native int",
                PrimitiveTypeCode.UIntPtr => "native uint",
                PrimitiveTypeCode.TypedReference => "typedref",
                _ => typeCode.ToString(),
            };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeName(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeName(reader, handle);

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var specification = reader.GetTypeSpecification(handle);
            return GuardedProviderDecode.TryTypeSpec(
                reader,
                handle,
                this,
                genericContext,
                out var decoded)
                ? decoded
                : GuardedProviderDecode.RejectedIdentity(reader, specification.Signature);
        }

        public string GetSZArrayType(string elementType) => $"{elementType}[]";
        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
        public string GetByReferenceType(string elementType) => $"{elementType}&";
        public string GetPointerType(string elementType) => $"{elementType}*";
        public string GetPinnedType(string elementType) => $"{elementType} pinned";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => $"{genericType}<{string.Join(", ", typeArguments)}>";
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
            => $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            string convention = CallingConventionPrefix(signature.Header.CallingConvention);
            return $"method {convention}{signature.ReturnType} *({FormatParameterList(signature)})";
        }

        static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { return $"{TypeName(reader, declaring)}+{name}"; }
                finally { s_climbDepth--; }
            }
            string ns = reader.GetString(type.Namespace);
            string assembly = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";
            return $"[{assembly}]{(ns.Length == 0 ? name : $"{ns}.{name}")}";
        }

        static string TypeName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            string ns = reader.GetString(type.Namespace);
            string fullName = ns.Length == 0 ? name : $"{ns}.{name}";
            if (type.ResolutionScope.Kind == HandleKind.AssemblyReference)
                return $"[{AssemblyReferenceIdentity(reader, (AssemblyReferenceHandle)type.ResolutionScope)}]{fullName}";
            if (type.ResolutionScope.Kind == HandleKind.TypeReference && s_climbDepth < MaxClimbDepth)
            {
                s_climbDepth++;
                try { return $"{TypeName(reader, (TypeReferenceHandle)type.ResolutionScope)}+{fullName}"; }
                finally { s_climbDepth--; }
            }
            return fullName;
        }
    }

    static string FormatParameterList(MethodSignature<string> signature)
    {
        if (signature.Header.CallingConvention != SignatureCallingConvention.VarArgs)
            return string.Join(", ", signature.ParameterTypes);

        var builder = ImmutableArray.CreateBuilder<string>(signature.ParameterTypes.Length + 1);
        int requiredCount = Math.Clamp(signature.RequiredParameterCount, 0, signature.ParameterTypes.Length);
        for (int i = 0; i < requiredCount; i++)
            builder.Add(signature.ParameterTypes[i]);
        builder.Add("...");
        for (int i = requiredCount; i < signature.ParameterTypes.Length; i++)
            builder.Add(signature.ParameterTypes[i]);
        return string.Join(", ", builder);
    }

    static string CallingConventionPrefix(SignatureCallingConvention convention)
    {
        string text = convention switch
        {
            SignatureCallingConvention.Default => "",
            SignatureCallingConvention.VarArgs => "vararg",
            SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
            SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
            SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
            SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
            SignatureCallingConvention.Unmanaged => "unmanaged",
            _ => convention.ToString(),
        };
        return text.Length == 0 ? "" : $"{text} ";
    }

    static string AssemblyReferenceIdentity(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        var reference = reader.GetAssemblyReference(handle);
        string name = reader.GetString(reference.Name);
        string culture = reference.Culture.IsNil ? "neutral" : reader.GetString(reference.Culture);
        string keyOrToken = reference.PublicKeyOrToken.IsNil
            ? "null"
            : Convert.ToHexString(reader.GetBlobBytes(reference.PublicKeyOrToken)).ToLowerInvariant();
        string keyLabel = (reference.Flags & AssemblyFlags.PublicKey) != 0 ? "PublicKey" : "PublicKeyToken";
        string flags = reference.Flags == default ? "" : $", Flags={reference.Flags}";
        return $"{name}, Version={reference.Version}, Culture={culture}, {keyLabel}={keyOrToken}{flags}";
    }
}
