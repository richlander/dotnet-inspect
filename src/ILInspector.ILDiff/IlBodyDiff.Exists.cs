using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.ILDiff;

// SPIKE (exp/staged-diff-spike): an early-exit form of the canonical body
// comparison's Exact decision, for eng/measure-staged-diff.cs only. It reuses
// the canonical comparison's own primitives (decode, TryToOperation,
// CanonicalEquals, BranchTargetsMatch, MetadataOperandResolver) and decides
// exactly "IlBodyDiff.Compare(...).Outcome == Exact" without building the LCS:
// Exact means a full-length common subsequence, which for two sequences of one
// length is the identity alignment, so the decision is "same length, each
// operation canonically equal in lockstep, each branch target at the same
// index". It stops at the first operation that differs. Product behaviour is
// unchanged; nothing in the product calls it.
internal enum IlExistsOutcome
{
    Exact,
    Changed,
    Unavailable,
}

internal sealed class IlBodyExistsComparer
{
    readonly IlBodyDiff.MetadataOperandResolver _old;
    readonly IlBodyDiff.MetadataOperandResolver _new;
    readonly IlBodyDiffNormalization _normalization;

    public IlBodyExistsComparer(
        MetadataReader oldReader,
        MetadataReader newReader,
        IlBodyDiffNormalization normalization)
    {
        if ((normalization & IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals) != 0)
            throw new ArgumentOutOfRangeException(nameof(normalization), "Spike does not build ordinal correspondence.");
        _old = IlBodyDiff.CreateSpikeResolver(oldReader, normalization);
        _new = IlBodyDiff.CreateSpikeResolver(newReader, normalization);
        _normalization = normalization;
    }

    // SPIKE (exp/unified-diff-exists): a comparer over a correspondence built once per image pair.
    public IlBodyExistsComparer(
        MetadataReader oldReader,
        MetadataReader newReader,
        IlBodyDiffNormalization normalization,
        CompilerGeneratedOrdinalCorrespondence oldCorrespondence,
        CompilerGeneratedOrdinalCorrespondence newCorrespondence)
    {
        _old = new IlBodyDiff.MetadataOperandResolver(oldReader, normalization, oldCorrespondence);
        _new = new IlBodyDiff.MetadataOperandResolver(newReader, normalization, newCorrespondence);
        _normalization = normalization;
    }

    // SPIKE (exp/unified-diff-exists): attribution counters, read only when Instrument is set.
    public bool Instrument { get; set; }
    public long DecodeTicks { get; set; }
    public long ResolveTicks { get; set; }
    public long ResolveMisses { get; set; }
    public long InstructionsCompared { get; set; }

    /// <summary>Tokens resolved so far (both sides), for work accounting.</summary>
    public long TokensResolved { get; private set; }

    /// <summary>
    /// Lockstep canonical comparison with early exit. When
    /// <paramref name="rawFastPath"/> is set and the raw IL bytes are equal,
    /// only token operands are resolved and compared: every other operand,
    /// opcode, and branch target is byte-identical, so equal token identities
    /// make the bodies canonically Exact.
    /// </summary>
    public IlExistsOutcome Compare(
        MethodBodyBlock oldBody,
        MethodBodyBlock newBody,
        bool rawFastPath,
        out bool rawEqual,
        out string? failure)
        => Compare(oldBody, newBody, rawFastPath, memoTokens: false, liteDecode: false, out rawEqual, out failure);

    /// <param name="memoTokens">Memoize each side's token identities by (operand kind, token).</param>
    /// <param name="liteDecode">
    /// Decode instructions only (InstructionDecoder), skipping the EH-aware block graph that
    /// MethodInstructions.Decode builds and the canonical comparison uses to reject a body as
    /// incomplete. Not equivalent: a body whose block graph is incomplete is compared anyway.
    /// </param>
    public IlExistsOutcome Compare(
        MethodBodyBlock oldBody,
        MethodBodyBlock newBody,
        bool rawFastPath,
        bool memoTokens,
        bool liteDecode,
        out bool rawEqual,
        out string? failure)
    {
        byte[] oldIl = oldBody.GetILBytes() ?? [];
        byte[] newIl = newBody.GetILBytes() ?? [];
        rawEqual = oldIl.AsSpan().SequenceEqual(newIl);
        long decodeStart = Instrument ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        ImmutableArray<DecodedInstruction> oldInstructions, newInstructions;
        if (liteDecode)
        {
            try
            {
                oldInstructions = InstructionDecoder.Decode(oldIl);
                newInstructions = InstructionDecoder.Decode(newIl);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException)
            {
                failure = ex.Message;
                return IlExistsOutcome.Unavailable;
            }
        }
        else
        {
            var oldDecoded = MethodInstructions.Decode(oldIl, oldIl.Length, oldBody.ExceptionRegions);
            if (!oldDecoded.IsComplete)
            {
                failure = oldDecoded.Blocks.IncompleteReason ?? "old body decode failed";
                return IlExistsOutcome.Unavailable;
            }
            var newDecoded = MethodInstructions.Decode(newIl, newIl.Length, newBody.ExceptionRegions);
            if (!newDecoded.IsComplete)
            {
                failure = newDecoded.Blocks.IncompleteReason ?? "new body decode failed";
                return IlExistsOutcome.Unavailable;
            }
            oldInstructions = oldDecoded.Instructions;
            newInstructions = newDecoded.Instructions;
        }

        if (Instrument)
            DecodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - decodeStart;
        return CompareDecoded(oldInstructions, newInstructions, rawFastPath && rawEqual, memoTokens, out failure);
    }

    readonly Dictionary<(OperandKind, long), IlOperandIdentity> _oldMemo = [];
    readonly Dictionary<(OperandKind, long), IlOperandIdentity> _newMemo = [];

    bool ToOperation(
        DecodedInstruction instruction,
        IlBodyDiff.MetadataOperandResolver resolver,
        Dictionary<(OperandKind, long), IlOperandIdentity>? memo,
        out CanonicalIlOperation operation,
        out string? failure)
    {
        if (memo is null || !IlBodyDiff.SpikeIsTokenOperand(instruction.Operand))
            return IlBodyDiff.SpikeToOperation(instruction, resolver, _normalization, out operation, out failure);
        var key = (instruction.Operand, instruction.OperandValue);
        if (!memo.TryGetValue(key, out var operand))
        {
            long resolveStart = Instrument ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            bool ok = resolver.TryResolve(instruction, out var resolved, out failure);
            if (Instrument)
            {
                ResolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - resolveStart;
                ResolveMisses++;
            }
            if (!ok)
            {
                operation = default!;
                return false;
            }
            operand = resolved!;
            memo[key] = operand;
        }
        operation = new CanonicalIlOperation(instruction.Offset, IlBodyDiff.SpikeOpcodeFamily(instruction, _normalization), operand);
        failure = null;
        return true;
    }

    IlExistsOutcome CompareDecoded(
        ImmutableArray<DecodedInstruction> oldInstructions,
        ImmutableArray<DecodedInstruction> newInstructions,
        bool tokensOnly,
        bool memoTokens,
        out string? failure)
    {
        failure = null;
        if (oldInstructions.Length != newInstructions.Length)
            return IlExistsOutcome.Changed;

        var identity = new IdentityMap(oldInstructions.Length);
        for (int i = 0; i < oldInstructions.Length; i++)
        {
            var oldInstruction = oldInstructions[i];
            var newInstruction = newInstructions[i];
            if (tokensOnly && !IlBodyDiff.SpikeIsTokenOperand(oldInstruction.Operand))
                continue;

            InstructionsCompared++;
            if (!ToOperation(oldInstruction, _old, memoTokens ? _oldMemo : null, out var oldOperation, out failure)
                || !ToOperation(newInstruction, _new, memoTokens ? _newMemo : null, out var newOperation, out failure))
            {
                return IlExistsOutcome.Unavailable;
            }
            if (IlBodyDiff.SpikeIsTokenOperand(oldInstruction.Operand))
                TokensResolved += 2;
            if (!IlBodyDiff.SpikeCanonicalEquals(oldOperation, newOperation))
                return IlExistsOutcome.Changed;
            if (!tokensOnly
                && !IlBodyDiff.BranchTargetsMatch(oldInstructions, i, newInstructions, i, identity))
            {
                return IlExistsOutcome.Changed;
            }
        }

        return IlExistsOutcome.Exact;
    }

    sealed class IdentityMap(int count) : IReadOnlyDictionary<int, int>
    {
        public int this[int key] => TryGetValue(key, out int value) ? value : throw new KeyNotFoundException();
        public IEnumerable<int> Keys => Enumerable.Range(0, count);
        public IEnumerable<int> Values => Keys;
        public int Count => count;
        public bool ContainsKey(int key) => (uint)key < (uint)count;
        public bool TryGetValue(int key, [MaybeNullWhen(false)] out int value)
        {
            value = key;
            return ContainsKey(key);
        }
        public IEnumerator<KeyValuePair<int, int>> GetEnumerator()
            => Keys.Select(key => KeyValuePair.Create(key, key)).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public static partial class IlBodyDiff
{
    internal static MetadataOperandResolver CreateSpikeResolver(MetadataReader reader, IlBodyDiffNormalization normalization)
        => new(reader, normalization, CompilerGeneratedOrdinalCorrespondence.Empty);

    internal static bool SpikeToOperation(
        DecodedInstruction instruction,
        MetadataOperandResolver resolver,
        IlBodyDiffNormalization normalization,
        out CanonicalIlOperation operation,
        out string? failure)
        => TryToOperation(instruction, resolver, normalization, out operation, out failure);

    internal static bool SpikeCanonicalEquals(CanonicalIlOperation oldOperation, CanonicalIlOperation newOperation)
        => CanonicalEquals(oldOperation, newOperation);

    internal static string SpikeOpcodeFamily(DecodedInstruction instruction, IlBodyDiffNormalization normalization)
        => OpcodeFamily(instruction, normalization);

    internal static bool SpikeIsTokenOperand(OperandKind kind)
        => IsMetadataTokenOperand(kind);
}
