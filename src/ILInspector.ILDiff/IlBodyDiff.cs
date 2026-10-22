using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

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

public sealed record IlOperandIdentity(IlOperandIdentityKind Kind, string Value);

/// <param name="Targets">
/// The IL offsets an operation points at: a branch's target, a switch's arms,
/// or an exception clause's extent boundaries (try start and end, handler
/// start and end, and for a filter its filter start and end). Targets are not
/// part of an operation's identity; the diff validates them over its
/// alignment, by the instruction each one starts.
/// </param>
public sealed record CanonicalIlOperation(
    int Offset,
    string OpcodeFamily,
    IlOperandIdentity? Operand,
    ImmutableArray<int> Targets = default)
{
    /// <summary>
    /// Whether this is an exception clause atom, which canonicalization appends
    /// after the body's instructions in declaration order.
    /// </summary>
    public bool IsExceptionClause => OpcodeFamily is "catch" or "filter" or "finally" or "fault";

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
    const IlBodyDiffNormalization SupportedNormalizations =
        IlBodyDiffNormalization.NormalizeVariableLayout
        | IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
        | IlBodyDiffNormalization.NormalizePlatformAssemblyScope
        | IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals
        | IlBodyDiffNormalization.NormalizeAssemblyScope;

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
                ? CompilerGeneratedOrdinalCorrespondence.Build(oldReader, newReader, normalization)
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
        => TryCanonicalize(body, reader, IlBodyDiffNormalization.None, out operations, out failure);

    /// <summary>
    /// Canonicalizes one body under a normalization a single side can apply.
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
        if ((normalization & ~SupportedNormalizations) != 0
            || (normalization & IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalization),
                normalization,
                "Unsupported single-side IL canonicalization normalization.");
        }
        if (!body.IsComplete)
        {
            operations = [];
            failure = body.Blocks.IncompleteReason ?? "body decode failed";
            return false;
        }

        var resolver = reader is null ? null : new MetadataOperandResolver(reader, normalization, CompilerGeneratedOrdinalCorrespondence.Empty);
        return TryBuildOperations(
            body,
            resolver,
            "body",
            normalization,
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
        if (!TryBuildOperations(oldBody, oldResolver, "old", normalization, out var oldOperations, out var oldFailure))
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.TokenResolutionFailure,
                oldFailure ?? "old body token resolution failed",
                side: "old");
        if (!TryBuildOperations(newBody, newResolver, "new", normalization, out var newOperations, out var newFailure))
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
            if (!TargetsMatch(oldOperations, nextOld, oldInstructions, newOperations, nextNew, newInstructions, oldToNew))
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
    /// The canonical sequence of one body: every instruction as an operation,
    /// then every exception clause as an atom in declaration order, so a clause
    /// is compared as the decoder validated it and never by raw offsets.
    /// </summary>
    static bool TryBuildOperations(
        MethodInstructions body,
        MetadataOperandResolver? resolver,
        string side,
        IlBodyDiffNormalization normalization,
        out ImmutableArray<CanonicalIlOperation> operations,
        out string? failure)
    {
        var instructions = body.Instructions;
        var clauses = body.Blocks.Regions;
        var builder = ImmutableArray.CreateBuilder<CanonicalIlOperation>(instructions.Length + clauses.Length);
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
        foreach (var clause in clauses)
        {
            if (!TryToClause(clause, resolver, out var atom, out var clauseFailure))
            {
                operations = [];
                failure = $"{side} body {clauseFailure}";
                return false;
            }

            builder.Add(atom);
        }

        operations = builder.MoveToImmutable();
        failure = null;
        return true;
    }

    /// <summary>
    /// An exception clause as a canonical atom: its kind as the family, its
    /// catch Type as a token operand under the same normalization as any type
    /// operand, and its extent boundaries as targets.
    /// </summary>
    static bool TryToClause(
        ExceptionRegionModel clause,
        MetadataOperandResolver? resolver,
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
        IlOperandIdentity? operand = null;
        if (clause.Kind == HandlerKind.Catch)
        {
            if (resolver is null)
            {
                atom = new CanonicalIlOperation(clause.TryStart, family, Operand: null, targets);
                failure = $"catch Type of the clause at IL_{clause.TryStart:X4} requires a MetadataReader-backed comparison";
                return false;
            }
            if (!resolver.TryResolveCatchType(clause.CatchTypeToken, clause.TryStart, out operand, out failure))
            {
                atom = new CanonicalIlOperation(clause.TryStart, family, Operand: null, targets);
                return false;
            }
        }

        atom = new CanonicalIlOperation(clause.TryStart, family, operand, targets);
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
            operand,
            instruction.BranchTargets.IsDefault ? [] : instruction.BranchTargets);
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
    /// True when a matched operation's targets still correspond under the
    /// given alignment map: a branch was not retargeted, a switch's arms still
    /// point at aligned instructions, and an exception clause's extents start
    /// and end at aligned instructions, where the end of the body aligns with
    /// the end of the body. Exposed so the evidence adapter reuses this
    /// decision verbatim rather than reimplementing it.
    /// </summary>
    public static bool TargetsMatch(
        ImmutableArray<CanonicalIlOperation> oldOperations,
        int oldIndex,
        ImmutableArray<DecodedInstruction> oldInstructions,
        ImmutableArray<CanonicalIlOperation> newOperations,
        int newIndex,
        ImmutableArray<DecodedInstruction> newInstructions,
        IReadOnlyDictionary<int, int> oldToNew)
    {
        ImmutableArray<int> oldTargets = oldOperations[oldIndex].Targets;
        ImmutableArray<int> newTargets = newOperations[newIndex].Targets;
        if (oldTargets.IsDefaultOrEmpty && newTargets.IsDefaultOrEmpty)
            return true;
        if (oldTargets.IsDefaultOrEmpty || newTargets.IsDefaultOrEmpty || oldTargets.Length != newTargets.Length)
            return false;

        for (int i = 0; i < oldTargets.Length; i++)
        {
            int oldTargetIndex = InstructionIndexAt(oldInstructions, oldTargets[i]);
            int newTargetIndex = InstructionIndexAt(newInstructions, newTargets[i]);
            if (oldTargetIndex < 0 || newTargetIndex < 0)
                return oldTargets[i] == newTargets[i];
            if (oldTargetIndex == oldInstructions.Length || newTargetIndex == newInstructions.Length)
            {
                if (oldTargetIndex != oldInstructions.Length || newTargetIndex != newInstructions.Length)
                    return false;
                continue;
            }
            if (!oldToNew.TryGetValue(oldTargetIndex, out int mappedNewTarget))
                return false;
            if (mappedNewTarget != newTargetIndex)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The index of the instruction that starts at <paramref name="offset"/>,
    /// the instruction count for the end of the body, or -1.
    /// </summary>
    static int InstructionIndexAt(ImmutableArray<DecodedInstruction> instructions, int offset)
    {
        for (int i = 0; i < instructions.Length; i++)
            if (instructions[i].Offset == offset)
                return i;
        if (instructions.Length > 0 && instructions[^1].NextOffset == offset)
            return instructions.Length;
        return -1;
    }

}
