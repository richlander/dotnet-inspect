using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;

using ILInspector.Instructions;
using ILInspector.Metadata;

using Inspector.Findings;

namespace ILInspector.ILDiff;


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

    /// <summary>
    /// Give every type and member reference, including those defined by either
    /// compared assembly, one shared <c>&lt;assembly&gt;</c> scope, so operands
    /// compare by namespace, name, and signature alone, without their defining or
    /// referenced assembly or its version. Subsumes
    /// <see cref="NormalizeCurrentAssemblyScope"/> and
    /// <see cref="NormalizePlatformAssemblyScope"/>. String literals are not
    /// rewritten. The mechanic Finding Diff Any's Body policy composes (#10205).
    /// </summary>
    NormalizeAssemblyScope = 1 << 5,
}

public enum IlBodyDiffOutcome
{
    Unavailable = 0,
    Exact,
    OperandDiff,
    OpcodeDiff,

    /// <summary>
    /// Every operation matches and only exception clauses differ: a clause's
    /// kind, its catch Type, or an extent that no longer aligns.
    /// </summary>
    ExceptionRegionDiff,
}

/// <summary>
/// An operation's operand identity under its policy
/// (<c>docs/design/finding-census.md#the-cursor</c>). A metadata token
/// operand is its row's Symbol Key, spelled by the operand's
/// <see cref="IlCensusSide"/> and compared as bytes; an immediate, a slot, a
/// branch or switch target, and a user string are values, compared as their
/// text. <see cref="Value"/> is the text a view shows: a value's text, or a
/// token's formatted spelling, projected from the side when first read and
/// never compared.
/// </summary>
public sealed class IlOperandIdentity : IEquatable<IlOperandIdentity>
{
    readonly StagedSymbolKey<IlOperandPart> _key;
    readonly IlCensusSide? _side;
    readonly OperandKind _operandKind;
    readonly int _token;
    string? _value;

    /// <summary>A value identity, or a token identity given as its text, which compares as text.</summary>
    public IlOperandIdentity(IlOperandIdentityKind kind, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Kind = kind;
        _value = value;
    }

    internal IlOperandIdentity(StagedSymbolKey<IlOperandPart> key, IlCensusSide side, OperandKind operandKind, int token)
    {
        Kind = IlOperandIdentityKind.Token;
        _key = key;
        _side = side;
        _operandKind = operandKind;
        _token = token;
    }

    public IlOperandIdentityKind Kind { get; }

    /// <summary>
    /// The text a view shows; a token's is formatted from its side when first
    /// read, or when its census is sealed (<see cref="IlCensusCursor.Drain"/>),
    /// since a sealed census may outlive its side's reader. Before sealing,
    /// the text is available while the reader lives.
    /// </summary>
    public string Value => _value ??= _side!.Format(_operandKind, _token);

    /// <summary>Formats the text now, so the identity no longer needs its side's reader.</summary>
    internal void Seal() => _ = Value;

    /// <summary>
    /// The row's key in its side's population, when this identity is a row's
    /// and compared as bytes; false for a value identity. A method, so the
    /// identity's serialized shape stays its kind and text.
    /// </summary>
    public bool TryGetKey(out StagedSymbolKey<IlOperandPart> key)
    {
        key = _key;
        return _side is not null;
    }

    public bool Equals(IlOperandIdentity? other)
    {
        if (other is null || Kind != other.Kind)
            return false;
        if (_side is null)
            return other._side is null && string.Equals(_value, other._value, StringComparison.Ordinal);
        return other._side is not null && _key.Equals(other._key);
    }

    public override bool Equals(object? obj) => obj is IlOperandIdentity other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, _side is null ? _value!.GetHashCode() : _key.GetHashCode());

    public override string ToString() => Value;
}

/// <param name="Targets">
/// The IL offsets an operation points at: a branch's target, a switch's arms,
/// or an exception clause's extent boundaries (try start and end, handler
/// start and end, and for a filter its filter start and end). Offsets are
/// evidence and display context, not identity.
/// </param>
/// <param name="TargetIndexes">
/// The same targets as instruction indexes within the operation's own body,
/// one per entry of <paramref name="Targets"/>: the index of the instruction
/// the target starts, <see cref="BodyEndTarget"/> for a clause boundary at the
/// end of the body, or <see cref="UndecidedTarget"/> for a target the cursor
/// could not place, which only a failed stream carries. Target indexes are not
/// part of an operation's matcher identity; the diff validates them over its
/// alignment.
/// </param>
public sealed record CanonicalIlOperation(
    int Offset,
    string OpcodeFamily,
    IlOperandIdentity? Operand,
    ImmutableArray<int> Targets = default,
    ImmutableArray<int> TargetIndexes = default)
{
    /// <summary>A target that starts no decidable instruction of its body.</summary>
    public const int UndecidedTarget = -1;

    /// <summary>A clause boundary at the end of the body, which aligns only with the end of the body.</summary>
    public const int BodyEndTarget = -2;

    /// <summary>
    /// Whether this is an exception clause atom, which canonicalization appends
    /// after the body's instructions in declaration order.
    /// </summary>
    public bool IsExceptionClause => OpcodeFamily is "catch" or "filter" or "finally" or "fault";

    /// <summary>Two operations are equal when every part is, targets and target indexes by value.</summary>
    public bool Equals(CanonicalIlOperation? other)
        => other is not null
            && Offset == other.Offset
            && OpcodeFamily == other.OpcodeFamily
            && Equals(Operand, other.Operand)
            && TargetsOf(this).SequenceEqual(TargetsOf(other))
            && IndexesOf(this).SequenceEqual(IndexesOf(other));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Offset);
        hash.Add(OpcodeFamily);
        hash.Add(Operand);
        foreach (int target in TargetsOf(this))
            hash.Add(target);
        foreach (int index in IndexesOf(this))
            hash.Add(index);
        return hash.ToHashCode();
    }

    static ImmutableArray<int> TargetsOf(CanonicalIlOperation operation)
        => operation.Targets.IsDefault ? [] : operation.Targets;

    internal static ImmutableArray<int> IndexesOf(CanonicalIlOperation operation)
        => operation.TargetIndexes.IsDefault ? [] : operation.TargetIndexes;

    public string Display
    {
        get
        {
            string head = Operand is null ? OpcodeFamily : $"{OpcodeFamily} {Operand.Value}";
            if (!IsExceptionClause || Targets.IsDefaultOrEmpty)
                return head;
            string extents = $"try [IL_{Targets[0]:X4}, IL_{Targets[1]:X4}) handler [IL_{Targets[2]:X4}, IL_{Targets[3]:X4})";
            if (Targets.Length >= 6)
                extents += $" filter [IL_{Targets[4]:X4}, IL_{Targets[5]:X4})";
            return $"{head} {extents}";
        }
    }
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
    internal const IlBodyDiffNormalization SupportedNormalizations =
        IlBodyDiffNormalization.NormalizeVariableLayout
        | IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
        | IlBodyDiffNormalization.NormalizePlatformAssemblyScope
        | IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals
        | IlBodyDiffNormalization.NormalizeAssemblyScope;

    /// <summary>
    /// Rejects a policy one side cannot apply alone:
    /// <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>
    /// needs both sides' readers, so a consumer that composes it compares
    /// through <see cref="Compare(MetadataReader, MethodBodyBlock, MetadataReader, MethodBodyBlock, IlBodyDiffNormalization)"/>.
    /// </summary>
    internal static void RequireSingleSidePolicy(IlBodyDiffNormalization normalization)
    {
        if ((normalization & ~SupportedNormalizations) != 0
            || (normalization & IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalization),
                normalization,
                "Unsupported single-side IL canonicalization normalization.");
        }
    }

    public static IlBodyDiffResult Compare(MethodInstructions oldBody, MethodInstructions newBody)
        => Compare(oldBody, newBody, oldSide: null, newSide: null, IlBodyDiffNormalization.None);

    public static IlBodyDiffResult Compare(
        MethodInstructions oldBody,
        MethodInstructions newBody,
        IlBodyDiffNormalization normalization)
        => Compare(oldBody, newBody, oldSide: null, newSide: null, normalization);

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

        var (oldSide, newSide) = IlCensusSide.CreatePair(oldReader, newReader, normalization);
        return Compare(oldSide, oldBody, newSide, newBody);
    }

    /// <summary>
    /// Compares two bodies through their sides, under the sides' one policy,
    /// so the rows every body of one image names are spelled once per side
    /// rather than once per comparison.
    /// </summary>
    public static IlBodyDiffResult Compare(
        IlCensusSide oldSide,
        MethodBodyBlock oldBody,
        IlCensusSide newSide,
        MethodBodyBlock newBody)
    {
        ArgumentNullException.ThrowIfNull(oldSide);
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newSide);
        ArgumentNullException.ThrowIfNull(newBody);
        RequireOnePolicy(oldSide, newSide);
        return Compare(
            MethodInstructions.Decode(oldBody),
            MethodInstructions.Decode(newBody),
            oldSide,
            newSide,
            oldSide.Policy);
    }

    internal static void RequireOnePolicy(IlCensusSide oldSide, IlCensusSide newSide)
    {
        if (oldSide.Policy != newSide.Policy)
            throw new ArgumentException("Both sides of one comparison are under one policy.", nameof(newSide));
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
        => TryCanonicalize(body, reader, IlBodyDiffNormalization.None, out operations, out failure);

    /// <summary>
    /// Canonicalizes one body under a normalization a single side can apply, by
    /// draining the body's <see cref="IlCensusCursor"/>: the operations are the
    /// cursor's atoms in order, and a failure is its terminal's reason.
    /// <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>
    /// needs both sides' readers, so it is rejected here; a consumer that
    /// composes it compares through <see cref="Compare(MetadataReader, MethodBodyBlock, MetadataReader, MethodBodyBlock, IlBodyDiffNormalization)"/>.
    /// </summary>
    public static bool TryCanonicalize(
        MethodInstructions body,
        MetadataReader? reader,
        IlBodyDiffNormalization normalization,
        out ImmutableArray<CanonicalIlOperation> operations,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(body);
        RequireSingleSidePolicy(normalization);
        IlCensusSide? side = reader is null ? null : IlCensusSide.Create(reader, normalization);
        return TryBuildOperations(body, side, label: "", normalization, out operations, out failure) && Seal(operations);
    }

    /// <summary>Canonicalizes one body through its side, under the side's policy.</summary>
    public static bool TryCanonicalize(
        IlCensusSide side,
        MethodInstructions body,
        out ImmutableArray<CanonicalIlOperation> operations,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(body);
        return TryBuildOperations(body, side, label: "", side.Policy, out operations, out failure) && Seal(operations);
    }

    // Operations handed out of the comparison may outlive their side's reader,
    // so their text is projected before they leave.
    static bool Seal(ImmutableArray<CanonicalIlOperation> operations)
    {
        foreach (CanonicalIlOperation operation in operations)
            operation.Operand?.Seal();
        return true;
    }

    static IlBodyDiffResult Compare(
        MethodInstructions oldBody,
        MethodInstructions newBody,
        IlCensusSide? oldSide,
        IlCensusSide? newSide,
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

        if (!TryBuildOperations(oldBody, oldSide, "old", normalization, out var oldOperations, out var oldFailure))
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.TokenResolutionFailure,
                oldFailure ?? "old body token resolution failed",
                side: "old");
        if (!TryBuildOperations(newBody, newSide, "new", normalization, out var newOperations, out var newFailure))
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
            if (!TargetsMatch(oldOperations, nextOld, newOperations, nextNew, oldToNew))
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
            : diffRows.All(static row => row.Operation.IsExceptionClause)
                ? IlBodyDiffOutcome.ExceptionRegionDiff
                : opcodeSequenceExact
                    ? IlBodyDiffOutcome.OperandDiff
                    : IlBodyDiffOutcome.OpcodeDiff;
        return new IlBodyDiffResult(outcome, Failure: null, diffRows);
    }

    /// <summary>
    /// The canonical sequence of one body, drained from its
    /// <see cref="IlCensusCursor"/>: every instruction as an operation, then
    /// every exception clause as an atom in declaration order, so a clause is
    /// compared as the decoder validated it and never by raw offsets. The
    /// cursor is the one producer of canonical IL; this is its drain without
    /// the Finding projection.
    /// </summary>
    static bool TryBuildOperations(
        MethodInstructions body,
        IlCensusSide? side,
        string label,
        IlBodyDiffNormalization normalization,
        out ImmutableArray<CanonicalIlOperation> operations,
        out string? failure)
    {
        var cursor = IlCensusCursor.Create(body, side, normalization);
        var builder = ImmutableArray.CreateBuilder<CanonicalIlOperation>(body.Instructions.Length + body.Blocks.Regions.Length);
        while (cursor.Advance())
            builder.Add(cursor.Current!);
        if (cursor.Terminal!.Kind != FindingCursorTerminalKind.Complete)
        {
            operations = [];
            failure = label.Length == 0 ? cursor.Terminal.Reason : $"{label} body {cursor.Terminal.Reason}";
            return false;
        }

        operations = builder.ToImmutable();
        failure = null;
        return true;
    }

    /// <summary>
    /// An exception clause as a canonical atom: its kind as the family, its
    /// catch Type as a token operand under the same normalization as any type
    /// operand, and its extent boundaries as targets.
    /// </summary>
    internal static bool TryToClause(
        ExceptionRegionModel clause,
        IlCensusSide? side,
        InstructionMap map,
        out CanonicalIlOperation atom,
        out string? failure)
    {
        string family = clause.Kind switch
        {
            HandlerKind.Catch => "catch",
            HandlerKind.Filter => "filter",
            HandlerKind.Finally => "finally",
            HandlerKind.Fault => "fault",
            _ => throw new ArgumentOutOfRangeException(nameof(clause), clause.Kind, "Unknown exception clause kind."),
        };
        ImmutableArray<int> targets = clause.Kind == HandlerKind.Filter
            ? [clause.TryStart, clause.TryEnd, clause.HandlerStart, clause.HandlerEnd, clause.FilterStart, clause.FilterEnd]
            : [clause.TryStart, clause.TryEnd, clause.HandlerStart, clause.HandlerEnd];
        ImmutableArray<int> indexes = map.ClauseBoundaryIndexes(targets);
        IlOperandIdentity? operand = null;
        if (clause.Kind == HandlerKind.Catch)
        {
            if (side is null)
            {
                atom = new CanonicalIlOperation(clause.TryStart, family, Operand: null, targets, indexes);
                failure = $"catch Type of the clause at IL_{clause.TryStart:X4} requires a MetadataReader-backed comparison";
                return false;
            }
            if (!side.TryResolveCatchType(clause.CatchTypeToken, clause.TryStart, out operand, out failure))
            {
                atom = new CanonicalIlOperation(clause.TryStart, family, Operand: null, targets, indexes);
                return false;
            }
        }

        atom = new CanonicalIlOperation(clause.TryStart, family, operand, targets, indexes);
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
        string subject = operation.IsExceptionClause
            ? "IL exception clause"
            : operation.Operand?.Kind is IlOperandIdentityKind.BranchTarget or IlOperandIdentityKind.SwitchTargets
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

    /// <summary>
    /// Whether two atoms are equal in their canonical bytes: the family, the
    /// operand identity under the atoms' policy, and each target as an
    /// instruction index of its own body, which is the equality a lockstep
    /// reader of two cursors decides (<c>docs/design/finding-census.md#the-cursor</c>).
    /// An undecided target equals only an undecided target; how a lockstep
    /// reader leaves such a position pending is its own rule.
    /// </summary>
    public static bool AtomsEqual(CanonicalIlOperation oldOperation, CanonicalIlOperation newOperation)
    {
        ArgumentNullException.ThrowIfNull(oldOperation);
        ArgumentNullException.ThrowIfNull(newOperation);
        return CanonicalEquals(oldOperation, newOperation)
            && CanonicalIlOperation.IndexesOf(oldOperation).SequenceEqual(CanonicalIlOperation.IndexesOf(newOperation));
    }

    static bool CanonicalEquals(CanonicalIlOperation oldOperation, CanonicalIlOperation newOperation)
    {
        if (oldOperation.OpcodeFamily != newOperation.OpcodeFamily)
            return false;
        if (oldOperation.Operand?.Kind is IlOperandIdentityKind.BranchTarget)
            return true;
        if (oldOperation.Operand?.Kind is IlOperandIdentityKind.SwitchTargets)
            return newOperation.Operand?.Kind is IlOperandIdentityKind.SwitchTargets
                && oldOperation.Targets.Length == newOperation.Targets.Length;
        return Equals(oldOperation.Operand, newOperation.Operand);
    }

    internal static bool TryToOperation(
        DecodedInstruction instruction,
        IlCensusSide? side,
        IlBodyDiffNormalization normalization,
        InstructionMap map,
        out CanonicalIlOperation operation,
        out string? failure)
    {
        if (!TryOperandIdentity(instruction, side, normalization, out var operand, out failure))
        {
            operation = new CanonicalIlOperation(
                instruction.Offset,
                OpcodeFamily(instruction, normalization),
                Operand: null);
            return false;
        }

        ImmutableArray<int> targets = IlAblation.NoTargets || instruction.BranchTargets.IsDefault ? [] : instruction.BranchTargets;
        operation = new CanonicalIlOperation(
            instruction.Offset,
            OpcodeFamily(instruction, normalization),
            operand,
            targets,
            IlAblation.NoTargets ? [] : map.BranchTargetIndexes(targets));
        return true;
    }

    // Each opcode's family is spelled once per policy; the per-instruction
    // spelling allocated three strings per atom (docs/design/finding-census.md,
    // Exists, ablation of the gap).
    static readonly string?[] s_familyPlain = new string?[0x200];
    static readonly string?[] s_familyVariableLayout = new string?[0x200];

    static string OpcodeFamily(
        DecodedInstruction instruction,
        IlBodyDiffNormalization normalization)
    {
        int value = (int)instruction.OpCode;
        int index = value <= 0xFF ? value : 0x100 + (value & 0xFF);
        string?[] table = (normalization & IlBodyDiffNormalization.NormalizeVariableLayout) != 0
            ? s_familyVariableLayout
            : s_familyPlain;
        return table[index] ??= ComputeOpcodeFamily(instruction.OpCode, normalization);
    }

    static string ComputeOpcodeFamily(ILOpCode opcode, IlBodyDiffNormalization normalization)
    {
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
        IlCensusSide? side,
        IlBodyDiffNormalization normalization,
        out IlOperandIdentity? operand,
        out string? failure)
    {
        if (IsMetadataTokenOperand(instruction.Operand))
        {
            if (side is null)
            {
                operand = null;
                failure = $"metadata token operand at IL_{instruction.Offset:X4} requires a MetadataReader-backed comparison";
                return false;
            }

            if (IlAblation.NoTokens)
            {
                operand = null;
                failure = null;
                return true;
            }
            return side.TryResolve(instruction, out operand, out failure);
        }

        operand = IlAblation.NoImmediates
            ? null
            : ImmediateOperandIdentity(instruction) ?? OperandIdentityByKind(instruction, normalization);
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
    /// True when a matched operation's targets still correspond under the
    /// given alignment map of operation ordinals: a branch was not retargeted,
    /// a switch's arms still point at aligned instructions, and an exception
    /// clause's extents start and end at aligned instructions, where the end of
    /// the body aligns only with the end of the body. An undecided target,
    /// which only an atom of a failed stream carries, corresponds only to an
    /// undecided target at the same offset. Exposed so the evidence adapter
    /// reuses this decision verbatim rather than reimplementing it.
    /// </summary>
    public static bool TargetsMatch(
        ImmutableArray<CanonicalIlOperation> oldOperations,
        int oldIndex,
        ImmutableArray<CanonicalIlOperation> newOperations,
        int newIndex,
        IReadOnlyDictionary<int, int> oldToNew)
    {
        ArgumentNullException.ThrowIfNull(oldToNew);
        CanonicalIlOperation oldOperation = oldOperations[oldIndex];
        CanonicalIlOperation newOperation = newOperations[newIndex];
        ImmutableArray<int> oldTargets = CanonicalIlOperation.IndexesOf(oldOperation);
        ImmutableArray<int> newTargets = CanonicalIlOperation.IndexesOf(newOperation);
        if (oldTargets.IsEmpty && newTargets.IsEmpty)
            return true;
        if (oldTargets.Length != newTargets.Length)
            return false;

        for (int i = 0; i < oldTargets.Length; i++)
        {
            int oldTarget = oldTargets[i];
            int newTarget = newTargets[i];
            if (oldTarget == CanonicalIlOperation.UndecidedTarget || newTarget == CanonicalIlOperation.UndecidedTarget)
            {
                if (oldTarget != newTarget || oldOperation.Targets[i] != newOperation.Targets[i])
                    return false;
                continue;
            }
            if (oldTarget == CanonicalIlOperation.BodyEndTarget || newTarget == CanonicalIlOperation.BodyEndTarget)
            {
                if (oldTarget != newTarget)
                    return false;
                continue;
            }
            if (!oldToNew.TryGetValue(oldTarget, out int mappedNewTarget) || mappedNewTarget != newTarget)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The instruction boundaries of one body, as the cursor's pre-scan builds
    /// them: each instruction's start offset and whether a target may land on
    /// it (an instruction following a prefix cannot be targeted).
    /// </summary>
    internal sealed class InstructionMap : IInstructionBoundaries
    {
        readonly int[] _starts;
        readonly bool[] _admitted;

        InstructionMap(int[] starts, bool[] admitted, int count, int ilLength)
        {
            _starts = starts;
            _admitted = admitted;
            InstructionCount = count;
            IlLength = ilLength;
        }

        public static InstructionMap Empty { get; } = new([], [], 0, 0);

        public int InstructionCount { get; }

        public int IlLength { get; }

        /// <summary>The map of an already decoded body.</summary>
        public static InstructionMap FromInstructions(ImmutableArray<DecodedInstruction> instructions)
        {
            var starts = new int[instructions.Length];
            var admitted = new bool[instructions.Length];
            for (int i = 0; i < instructions.Length; i++)
            {
                starts[i] = instructions[i].Offset;
                admitted[i] = i == 0 || !instructions[i - 1].OpCode.IsPrefix();
            }

            int ilLength = instructions.Length == 0 ? 0 : instructions[^1].NextOffset;
            return new InstructionMap(starts, admitted, instructions.Length, ilLength);
        }

        /// <summary>
        /// The boundary pre-scan over raw IL: instruction lengths only, resolving
        /// nothing. An instruction the decoder rejects ends the scan at its
        /// offset, reported as the fault; the map then covers the prefix before it.
        /// </summary>
        public static InstructionMap Scan(byte[] il, out int faultOffset, out string? faultReason)
        {
            var starts = new int[Math.Max(4, Math.Min(il.Length, 256))];
            var admitted = new bool[starts.Length];
            int count = 0;
            int offset = 0;
            bool previousPrefix = false;
            faultOffset = -1;
            faultReason = null;
            while (true)
            {
                try
                {
                    if (!InstructionDecoder.TryReadNext(il, ref offset, out InstructionEntry entry))
                        break;
                    if (count == starts.Length)
                    {
                        Array.Resize(ref starts, count * 2);
                        Array.Resize(ref admitted, count * 2);
                    }
                    starts[count] = entry.Offset;
                    admitted[count] = !previousPrefix;
                    count++;
                    previousPrefix = entry.OpCode.IsPrefix();
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException or ArgumentOutOfRangeException)
                {
                    faultOffset = offset;
                    faultReason = ex.Message;
                    break;
                }
            }

            return new InstructionMap(starts, admitted, count, il.Length);
        }

        /// <summary>
        /// The index of the admitted instruction starting at <paramref name="offset"/>,
        /// or <see cref="CanonicalIlOperation.UndecidedTarget"/>: a branch or switch
        /// target may not land at or past the end of the body, inside an
        /// instruction, or on one that follows a prefix.
        /// </summary>
        public int BranchTargetIndex(int offset)
        {
            int index = Array.BinarySearch(_starts, 0, InstructionCount, offset);
            return index >= 0 && _admitted[index] ? index : CanonicalIlOperation.UndecidedTarget;
        }

        /// <summary>
        /// The index a clause boundary starts, <see cref="CanonicalIlOperation.BodyEndTarget"/>
        /// at the end of the body, or <see cref="CanonicalIlOperation.UndecidedTarget"/>.
        /// </summary>
        public int ClauseBoundaryIndex(int offset)
            => offset == IlLength ? CanonicalIlOperation.BodyEndTarget : BranchTargetIndex(offset);

        /// <summary>The decoder's admitted boundaries, answered from the map: a clause boundary this map places.</summary>
        public bool Contains(int offset) => ClauseBoundaryIndex(offset) != CanonicalIlOperation.UndecidedTarget;

        public ImmutableArray<int> BranchTargetIndexes(ImmutableArray<int> targets)
        {
            if (targets.IsEmpty)
                return [];
            var indexes = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                indexes[i] = BranchTargetIndex(targets[i]);
            return ImmutableCollectionsMarshal.AsImmutableArray(indexes);
        }

        public ImmutableArray<int> ClauseBoundaryIndexes(ImmutableArray<int> boundaries)
        {
            var indexes = new int[boundaries.Length];
            for (int i = 0; i < boundaries.Length; i++)
                indexes[i] = ClauseBoundaryIndex(boundaries[i]);
            return ImmutableCollectionsMarshal.AsImmutableArray(indexes);
        }
    }
}
