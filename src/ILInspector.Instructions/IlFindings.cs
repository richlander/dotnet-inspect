using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Findings;

namespace ILInspector.Instructions;

/// <summary>
/// Adapts IL method bodies onto the domain-free finding substrate: it canonicalizes each body
/// (reusing <see cref="IlBodyDiff"/> exactly), projects operations into <see cref="Finding{T}"/>
/// atoms, runs the shared <see cref="FindingMatcher"/>, and folds the alignment into
/// <see cref="PairFinding{T}"/> transitions. This is the "IL as finding" pilot: the committed LCS
/// core reproduces <see cref="IlBodyDiff"/> on move-free bodies, and the move pass recovers
/// relocations that the order-preserving diff cannot. A single-version body is just its
/// <see cref="Inspect"/> census — no matcher, no pairs.
/// </summary>
public static class IlFindings
{
    /// <summary>The finding descriptor for a single IL operation occurrence.</summary>
    public static readonly FindingDescriptor OperationDescriptor = new("il.op", "IL operation");

    /// <summary>
    /// The census (one feed): inspects a single method body and lazily yields one
    /// <see cref="Finding{T}"/> per operation — the complete, unjudged raw view, in the spirit of
    /// <c>docker inspect</c>. Paired with <see cref="Compare"/> (two feeds). Like
    /// <see cref="Compare"/> it canonicalizes the body internally (reusing <see cref="IlBodyDiff"/>)
    /// and fails closed — an uninspectable body yields an empty census — so a caller never touches
    /// <see cref="IlBodyDiff"/>. Existence/filter/count are LINQ over the returned stream
    /// (<c>Any</c>/<c>Where</c>/<c>Count</c>), short-circuiting without allocating a list.
    /// </summary>
    public static IEnumerable<Finding<CanonicalIlOperation>> Inspect(
        MethodInstructions body,
        MetadataReader? reader,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(subject);

        // Fail closed: an uninspectable body is an empty census, not an exception through a query.
        if (!IlBodyDiff.TryCanonicalize(body, reader, out var operations, out _))
            return [];

        return ProjectAtoms(operations, subject);
    }

    public static IlFindingsResult Compare(
        MethodInstructions oldBody,
        MetadataReader? oldReader,
        MethodInstructions newBody,
        MetadataReader? newReader,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);
        ArgumentNullException.ThrowIfNull(subject);

        if (!IlBodyDiff.TryCanonicalize(oldBody, oldReader, out var oldOps, out var oldFailure))
            return IlFindingsResult.Failed(oldFailure ?? "old body canonicalization failed");
        if (!IlBodyDiff.TryCanonicalize(newBody, newReader, out var newOps, out var newFailure))
            return IlFindingsResult.Failed(newFailure ?? "new body canonicalization failed");

        // The projection is lazy; the diff enumerates each side more than once (keys for Match,
        // atoms for ToPairs, and again on the result), so materialize both once here. That is the
        // "materialize iff you enumerate more than once" rule: a single-version census consumer
        // (see Inspect) stays lazy, the diff pays one array per side at this boundary.
        var oldAtoms = ProjectAtoms(oldOps, subject).ToImmutableArray();
        var newAtoms = ProjectAtoms(newOps, subject).ToImmutableArray();

        FindingMatch match;
        try
        {
            match = FindingMatcher.Match(oldAtoms.Keys(), newAtoms.Keys());
        }
        catch (ArgumentException ex)
        {
            // Fail closed like the canonicalization path: a pathological body that exceeds the
            // ordered matcher's size guard returns a failure result instead of throwing at the caller.
            return IlFindingsResult.Failed(ex.Message);
        }

        var pairs = FindingFold.ToPairs(match, oldAtoms, newAtoms, acceptanceThreshold);
        pairs = ApplyBranchTargetValidation(pairs, oldBody.Instructions, oldOps, newBody.Instructions, newOps);
        return new IlFindingsResult(pairs, match, oldAtoms, newAtoms, Failure: null);
    }

    // IdentityKey deliberately ignores a branch/switch operation's targets (matching CanonicalEquals),
    // so a matched branch whose target was retargeted would otherwise read as unchanged. Reuse
    // IlBodyDiff's own alignment map and branch-target decision verbatim (not a reimplementation),
    // then overlay the finding Moved mappings so a branch that targets a relocated instruction is
    // judged in the finding frame (its target moved with it) rather than being falsely retargeted.
    // On move-free inputs there are no Moved pairs, so the map is exactly IlBodyDiff's.
    static ImmutableArray<PairFinding<CanonicalIlOperation>> ApplyBranchTargetValidation(
        ImmutableArray<PairFinding<CanonicalIlOperation>> pairs,
        ImmutableArray<DecodedInstruction> oldInstructions,
        ImmutableArray<CanonicalIlOperation> oldOps,
        ImmutableArray<DecodedInstruction> newInstructions,
        ImmutableArray<CanonicalIlOperation> newOps)
    {
        var alignment = new Dictionary<int, int>(IlBodyDiff.BuildAlignmentMap(oldOps, newOps));
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<CanonicalIlOperation>.Present { Difference: FindingDifferenceKind.Moved } moved)
                alignment[moved.Old.Position] = moved.New.Position;
        }

        var builder = ImmutableArray.CreateBuilder<PairFinding<CanonicalIlOperation>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<CanonicalIlOperation>.Present present
                && !IlBodyDiff.BranchTargetsMatch(oldInstructions, present.Old.Position, newInstructions, present.New.Position, alignment))
            {
                // A moved-and-retargeted operation keeps its move Detail; append the retarget so
                // neither the distance nor the retarget note is lost. Promoting the Present case to
                // Changed keeps both sides while flipping the polarity.
                builder.Add(new PairFinding<CanonicalIlOperation>.Changed(
                    present.Old,
                    present.New,
                    present.Difference,
                    present.Detail is null ? "branch retargeted" : $"{present.Detail}; branch retargeted"));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.ToImmutable();
    }

    // The shared ops->findings projection used by Inspect (lazy) and Compare (materialized). One
    // Finding per operation, carrying its content key and stream position. Private: canonicalized
    // ops are an internal shape; callers reach findings through Inspect or Compare, which own
    // canonicalization. Both callers guard their arguments before reaching here.
    static IEnumerable<Finding<CanonicalIlOperation>> ProjectAtoms(
        ImmutableArray<CanonicalIlOperation> operations,
        FindingSubject subject)
    {
        for (int i = 0; i < operations.Length; i++)
        {
            // ScopeKey is left null in the pilot: move detection is corroborated by run
            // contiguity, and EH/loop-region scope is the Attach layer's concern (issue #2564).
            yield return new Finding<CanonicalIlOperation>(
                subject,
                OperationDescriptor,
                new FindingKey(GetIdentityKey(operations[i])),
                i,
                operations[i]);
        }
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

/// <summary>The outcome of an <see cref="IlFindings.Compare"/> call.</summary>
public sealed record IlFindingsResult(
    ImmutableArray<PairFinding<CanonicalIlOperation>> Pairs,
    FindingMatch Match,
    ImmutableArray<Finding<CanonicalIlOperation>> OldAtoms,
    ImmutableArray<Finding<CanonicalIlOperation>> NewAtoms,
    string? Failure)
{
    /// <summary>True when the bodies are exact under the fidelity fold (no adds/removes/moves).</summary>
    public bool IsExact => Failure is null && FindingEquivalence.Exact.IsEquivalent(Pairs);

    public static IlFindingsResult Failed(string failure)
        => new([], new FindingMatch([], []), [], [], failure);
}
