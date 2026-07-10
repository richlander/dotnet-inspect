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

    /// <summary>The descriptor used when an IL inspection cannot produce a census.</summary>
    public static readonly FindingDescriptor InspectionDescriptor = new("il.inspect", "IL inspection");

    /// <summary>
    /// The maximum canonical IL operations a single inspection accepts. The ordered matcher matrix
    /// is <c>(oldCount + 1) * (newCount + 1)</c>, so two inspections at this same limit remain within
    /// <see cref="FindingMatcher.MaxOrderedMatchCells"/>.
    /// </summary>
    public static readonly int MaxCanonicalOperations =
        (int)Math.Sqrt(FindingMatcher.MaxOrderedMatchCells) - 1;

    /// <summary>
    /// Inspects one method body into a complete census, an absent-body state, or a canonicalization
    /// failure. A null body represents a method with no IL body, such as an abstract or extern method.
    /// </summary>
    public static FindingInspection<CanonicalIlOperation> Inspect(
        MethodInstructions? body,
        MetadataReader? reader,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (body is null)
            return new FindingInspection<CanonicalIlOperation>.Absent("Method has no IL body.");
        if (body.IsComplete && body.Instructions.Length > MaxCanonicalOperations)
        {
            return new FindingInspection<CanonicalIlOperation>.Failed(
                new InspectionError(
                    subject,
                    InspectionDescriptor,
                    $"IL inspection skipped: body has {body.Instructions.Length:N0} canonical operations; " +
                    $"limit is {MaxCanonicalOperations:N0}."));
        }

        if (!IlBodyDiff.TryCanonicalize(body, reader, out var operations, out var failure))
        {
            return new FindingInspection<CanonicalIlOperation>.Failed(
                new InspectionError(
                    subject,
                    InspectionDescriptor,
                    failure ?? "IL canonicalization failed."));
        }

        return new FindingInspection<CanonicalIlOperation>.Complete(
            [.. ProjectAtoms(operations, subject)]);
    }

    public static FindingComparison<CanonicalIlOperation> Compare(
        MethodInstructions? oldBody,
        MetadataReader? oldReader,
        MethodInstructions? newBody,
        MetadataReader? newReader,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var oldInspection = Inspect(oldBody, oldReader, subject);
        var newInspection = Inspect(newBody, newReader, subject);
        if (oldInspection is FindingInspection<CanonicalIlOperation>.Failed
            || newInspection is FindingInspection<CanonicalIlOperation>.Failed)
        {
            return new FindingComparison<CanonicalIlOperation>.Failed(
                oldInspection,
                newInspection);
        }

        var oldAtoms = InspectionAtoms(oldInspection);
        var newAtoms = InspectionAtoms(newInspection);
        var match = FindingMatcher.Match(oldAtoms.Keys(), newAtoms.Keys());
        var pairs = FindingFold.ToPairs(match, oldAtoms, newAtoms, acceptanceThreshold);
        if (oldBody is not null && newBody is not null)
        {
            pairs = ApplyBranchTargetValidation(
                pairs,
                oldBody.Instructions,
                [.. oldAtoms.Select(static atom => atom.Payload)],
                newBody.Instructions,
                [.. newAtoms.Select(static atom => atom.Payload)]);
        }

        return new FindingComparison<CanonicalIlOperation>.Complete(
            pairs,
            match,
            oldInspection,
            newInspection);
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

    // One Finding per operation, carrying its content key and stream position. Private:
    // canonicalized operations remain an internal shape.
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

    static ImmutableArray<Finding<CanonicalIlOperation>> InspectionAtoms(
        FindingInspection<CanonicalIlOperation> inspection)
        => inspection switch
        {
            FindingInspection<CanonicalIlOperation>.Complete complete => complete.Findings,
            FindingInspection<CanonicalIlOperation>.Absent => [],
            FindingInspection<CanonicalIlOperation>.Failed => throw new InvalidOperationException(
                "A failed inspection cannot be matched."),
        };

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
