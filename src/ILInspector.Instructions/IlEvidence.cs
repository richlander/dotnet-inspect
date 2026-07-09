using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Evidence;

namespace ILInspector.Instructions;

/// <summary>
/// Adapts IL method bodies onto the domain-free evidence substrate: it canonicalizes each body
/// (reusing <see cref="IlBodyDiff"/> exactly), projects operations into
/// <see cref="EvidenceOccurrence"/>s, runs the shared <see cref="EvidenceMatcher"/>, and
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

        EvidenceMatch correspondence;
        try
        {
            correspondence = EvidenceMatcher.Match(oldStream, newStream);
        }
        catch (ArgumentException ex)
        {
            // Fail closed like the canonicalization path: a pathological body that exceeds the
            // ordered matcher's size guard returns a failure result instead of throwing at the caller.
            return IlEvidenceResult.Failed(ex.Message);
        }

        var rows = EvidenceFold.ToRows(correspondence, oldStream, newStream, subject, OperationDescriptor, acceptanceThreshold);
        rows = ApplyBranchTargetValidation(rows, oldBody.Instructions, oldOps, newBody.Instructions, newOps);
        return new IlEvidenceResult(rows, correspondence, oldStream, newStream, Failure: null);
    }

    // IdentityKey deliberately ignores a branch/switch operation's targets (matching CanonicalEquals),
    // so a matched branch whose target was retargeted would otherwise read as unchanged. Reuse
    // IlBodyDiff's own alignment map and branch-target decision verbatim (not a reimplementation),
    // then overlay the evidence Moved mappings so a branch that targets a relocated instruction is
    // judged in the evidence frame (its target moved with it) rather than being falsely retargeted.
    // On move-free inputs there are no Moved rows, so the map is exactly IlBodyDiff's.
    static ImmutableArray<EvidenceRow> ApplyBranchTargetValidation(
        ImmutableArray<EvidenceRow> rows,
        ImmutableArray<DecodedInstruction> oldInstructions,
        ImmutableArray<CanonicalIlOperation> oldOps,
        ImmutableArray<DecodedInstruction> newInstructions,
        ImmutableArray<CanonicalIlOperation> newOps)
    {
        var alignment = new Dictionary<int, int>(IlBodyDiff.BuildAlignmentMap(oldOps, newOps));
        foreach (var row in rows)
        {
            if (row.DifferenceKind == EvidenceDifferenceKind.Moved
                && row.Anchor.OldIndex >= 0
                && row.Anchor.NewIndex >= 0)
            {
                alignment[row.Anchor.OldIndex] = row.Anchor.NewIndex;
            }
        }

        var builder = ImmutableArray.CreateBuilder<EvidenceRow>(rows.Length);
        foreach (var row in rows)
        {
            if (row.Kind == EvidenceRowKind.Present
                && row.Anchor.OldIndex >= 0
                && row.Anchor.NewIndex >= 0
                && !IlBodyDiff.BranchTargetsMatch(oldInstructions, row.Anchor.OldIndex, newInstructions, row.Anchor.NewIndex, alignment))
            {
                builder.Add(row with { Kind = EvidenceRowKind.Changed, Detail = "branch retargeted" });
            }
            else
            {
                builder.Add(row);
            }
        }

        return builder.ToImmutable();
    }

    static ImmutableArray<EvidenceOccurrence> BuildOccurrences(ImmutableArray<CanonicalIlOperation> operations)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceOccurrence>(operations.Length);
        foreach (var operation in operations)
        {
            // ScopeKey is left null in the pilot: move detection is corroborated by run
            // contiguity, and EH/loop-region scope is the Attach layer's concern (issue #2564).
            builder.Add(new EvidenceOccurrence(GetIdentityKey(operation), ScopeKey: null, Payload: operation));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// The canonical content key for an operation, defined so that key equality is exactly the
    /// <see cref="IlBodyDiff"/> canonical-equality relation (branch targets ignored; switch
    /// arms compared by count). This keeps the committed core aligned with the existing diff.
    /// </summary>
    public static string GetIdentityKey(CanonicalIlOperation operation)
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
    EvidenceMatch Match,
    ImmutableArray<EvidenceOccurrence> OldOccurrences,
    ImmutableArray<EvidenceOccurrence> NewOccurrences,
    string? Failure)
{
    /// <summary>True when the bodies are exact under the fidelity fold (no adds/removes/moves).</summary>
    public bool IsExact => Failure is null && EvidenceEquivalence.Exact.IsEquivalent(Rows);

    public static IlEvidenceResult Failed(string failure)
        => new([], new EvidenceMatch([], []), [], [], failure);
}
