using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;

using ILInspector.Evidence;

namespace ILInspector.Instructions;

/// <summary>
/// Adapts IL method bodies onto the domain-free evidence substrate: it canonicalizes each body
/// (reusing <see cref="IlBodyDiff"/> exactly), projects operations into
/// <see cref="EvidenceOccurrence"/>s, runs the shared <see cref="CorrespondenceEngine"/>, and
/// folds the correspondence into <see cref="EvidenceRow"/>s. This is the "IL as evidence" pilot:
/// the committed LCS core reproduces <see cref="IlBodyDiff"/> on move-free bodies, and the move
/// pass recovers relocations that the order-preserving diff cannot.
/// </summary>
public static class IlEvidence
{
    /// <summary>The evidence descriptor for a single IL operation occurrence.</summary>
    public static readonly EvidenceDescriptor OperationDescriptor = new("il.op", "IL operation");

    public static IlEvidenceResult Compare(
        MethodInstructions oldBody,
        MetadataReader? oldReader,
        MethodInstructions newBody,
        MetadataReader? newReader,
        EvidenceSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);
        ArgumentNullException.ThrowIfNull(subject);

        if (!IlBodyDiff.TryCanonicalize(oldBody, oldReader, out var oldOps, out var oldFailure))
            return IlEvidenceResult.Failed(oldFailure ?? "old body canonicalization failed");
        if (!IlBodyDiff.TryCanonicalize(newBody, newReader, out var newOps, out var newFailure))
            return IlEvidenceResult.Failed(newFailure ?? "new body canonicalization failed");

        var oldStream = BuildOccurrences(oldOps);
        var newStream = BuildOccurrences(newOps);
        var correspondence = CorrespondenceEngine.Match(oldStream, newStream);
        var rows = EvidenceFold.ToRows(correspondence, oldStream, newStream, subject, OperationDescriptor, acceptanceThreshold);
        rows = ApplyBranchTargetValidation(rows, oldOps, newOps, correspondence);
        return new IlEvidenceResult(rows, correspondence, oldStream, newStream, Failure: null);
    }

    // ContentKey deliberately ignores a branch/switch operation's targets (matching CanonicalEquals),
    // so a matched branch whose target was retargeted would otherwise read as unchanged. Reproduce
    // IlBodyDiff.BranchTargetsMatch over the correspondence's index map and downgrade such rows to
    // Changed, so a real control-flow retarget is never silently dropped from the evidence stream.
    static ImmutableArray<EvidenceRow> ApplyBranchTargetValidation(
        ImmutableArray<EvidenceRow> rows,
        ImmutableArray<CanonicalIlOperation> oldOps,
        ImmutableArray<CanonicalIlOperation> newOps,
        Correspondence correspondence)
    {
        var oldToNew = new Dictionary<int, int>();
        foreach (var link in correspondence.Links)
        {
            if (link.Kind is EvidenceLinkKind.Matched or EvidenceLinkKind.Moved)
                oldToNew[link.OldIndex] = link.NewIndex;
        }

        var oldOffsetToIndex = BuildOffsetIndex(oldOps);
        var newOffsetToIndex = BuildOffsetIndex(newOps);

        var builder = ImmutableArray.CreateBuilder<EvidenceRow>(rows.Length);
        foreach (var row in rows)
        {
            if (row.Polarity == EvidencePolarity.Present
                && row.Anchor.OldPosition >= 0
                && row.Anchor.NewPosition >= 0
                && oldOps[row.Anchor.OldPosition].Operand is { Kind: IlOperandIdentityKind.BranchTarget or IlOperandIdentityKind.SwitchTargets } oldOperand
                && !BranchTargetsCorrespond(oldOperand, newOps[row.Anchor.NewPosition].Operand, oldToNew, oldOffsetToIndex, newOffsetToIndex))
            {
                builder.Add(row with { Polarity = EvidencePolarity.Changed, Detail = "branch retargeted" });
            }
            else
            {
                builder.Add(row);
            }
        }

        return builder.ToImmutable();
    }

    static bool BranchTargetsCorrespond(
        IlOperandIdentity oldOperand,
        IlOperandIdentity? newOperand,
        IReadOnlyDictionary<int, int> oldToNew,
        IReadOnlyDictionary<int, int> oldOffsetToIndex,
        IReadOnlyDictionary<int, int> newOffsetToIndex)
    {
        if (newOperand is null)
            return false;

        var oldTargets = ParseTargetOffsets(oldOperand.Value);
        var newTargets = ParseTargetOffsets(newOperand.Value);
        if (oldTargets.Length != newTargets.Length)
            return false;

        for (int i = 0; i < oldTargets.Length; i++)
        {
            bool haveOld = oldOffsetToIndex.TryGetValue(oldTargets[i], out int oldTargetIndex);
            bool haveNew = newOffsetToIndex.TryGetValue(newTargets[i], out int newTargetIndex);
            if (!haveOld || !haveNew)
            {
                if (oldTargets[i] != newTargets[i])
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

    static Dictionary<int, int> BuildOffsetIndex(ImmutableArray<CanonicalIlOperation> operations)
    {
        var map = new Dictionary<int, int>(operations.Length);
        for (int i = 0; i < operations.Length; i++)
            map[operations[i].Offset] = i;
        return map;
    }

    static int[] ParseTargetOffsets(string value)
    {
        var parts = value.Split(',');
        var offsets = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.StartsWith("IL_", StringComparison.Ordinal)
                && int.TryParse(token.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int offset))
            {
                offsets.Add(offset);
            }
        }

        return [.. offsets];
    }

    static ImmutableArray<EvidenceOccurrence> BuildOccurrences(ImmutableArray<CanonicalIlOperation> operations)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceOccurrence>(operations.Length);
        foreach (var operation in operations)
        {
            // ScopeKey is left null in the pilot: move detection is corroborated by run
            // contiguity, and EH/loop-region scope is the Attach layer's concern (issue #2564).
            builder.Add(new EvidenceOccurrence(ContentKey(operation), ScopeKey: null, Payload: operation));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// The canonical content key for an operation, defined so that key equality is exactly the
    /// <see cref="IlBodyDiff"/> canonical-equality relation (branch targets ignored; switch
    /// arms compared by count). This keeps the committed core aligned with the existing diff.
    /// </summary>
    public static string ContentKey(CanonicalIlOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Operand is null)
            return operation.OpcodeFamily;

        return operation.Operand.Kind switch
        {
            IlOperandIdentityKind.BranchTarget => $"{operation.OpcodeFamily}|br",
            IlOperandIdentityKind.SwitchTargets => $"{operation.OpcodeFamily}|switch:{operation.Operand.Value.Split(',').Length}",
            _ => $"{operation.OpcodeFamily}|{operation.Operand.Kind}:{operation.Operand.Value}",
        };
    }
}

/// <summary>The outcome of an <see cref="IlEvidence.Compare"/> call.</summary>
public sealed record IlEvidenceResult(
    ImmutableArray<EvidenceRow> Rows,
    Correspondence Correspondence,
    ImmutableArray<EvidenceOccurrence> OldStream,
    ImmutableArray<EvidenceOccurrence> NewStream,
    string? Failure)
{
    /// <summary>True when the bodies are exact under the fidelity fold (no adds/removes/moves).</summary>
    public bool IsExact => Failure is null && EvidenceEquivalenceFold.Exact.IsEquivalent(Rows);

    public static IlEvidenceResult Failed(string failure)
        => new([], new Correspondence([], []), [], [], failure);
}
