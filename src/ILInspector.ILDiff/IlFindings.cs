using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
        return CompareInspections(
            oldInspection,
            newInspection,
            oldBody,
            newBody,
            acceptanceThreshold);
    }

    /// <summary>
    /// Inspects a method definition directly from its PE and metadata readers.
    /// </summary>
    public static FindingInspection<CanonicalIlOperation> Inspect(
        PEReader pe,
        MetadataReader reader,
        MethodDefinitionHandle method,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(pe);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(subject);
        if (method.IsNil)
            throw new ArgumentException("Method handle must not be nil.", nameof(method));

        return InspectMethod(pe, reader, method, subject, out _);
    }

    /// <summary>
    /// Compares already-acquired inspections. This overload preserves explicit absent sides for
    /// added and removed methods; branch validation is unnecessary unless both sides have bodies.
    /// </summary>
    public static FindingComparison<CanonicalIlOperation> Compare(
        FindingInspection<CanonicalIlOperation> oldInspection,
        FindingInspection<CanonicalIlOperation> newInspection,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldInspection);
        ArgumentNullException.ThrowIfNull(newInspection);
        return CompareInspections(
            oldInspection,
            newInspection,
            oldBody: null,
            newBody: null,
            acceptanceThreshold);
    }

    public static FindingComparison<CanonicalIlOperation> Compare(
        PEReader oldPe,
        MetadataReader oldReader,
        MethodDefinitionHandle oldMethod,
        PEReader newPe,
        MetadataReader newReader,
        MethodDefinitionHandle newMethod,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldPe);
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(newPe);
        ArgumentNullException.ThrowIfNull(newReader);
        ArgumentNullException.ThrowIfNull(subject);
        if (oldMethod.IsNil)
            throw new ArgumentException("Old method handle must not be nil.", nameof(oldMethod));
        if (newMethod.IsNil)
            throw new ArgumentException("New method handle must not be nil.", nameof(newMethod));

        var oldInspection = InspectMethod(
            oldPe,
            oldReader,
            oldMethod,
            subject,
            out var oldBody);
        var newInspection = InspectMethod(
            newPe,
            newReader,
            newMethod,
            subject,
            out var newBody);
        return CompareInspections(
            oldInspection,
            newInspection,
            oldBody,
            newBody,
            acceptanceThreshold);
    }

    static FindingComparison<CanonicalIlOperation> CompareInspections(
        FindingInspection<CanonicalIlOperation> oldInspection,
        FindingInspection<CanonicalIlOperation> newInspection,
        MethodInstructions? oldBody,
        MethodInstructions? newBody,
        int acceptanceThreshold)
    {
        var comparison = FindingComparison.Compare(
            oldInspection,
            newInspection,
            acceptanceThreshold: acceptanceThreshold);
        var complete = comparison switch
        {
            FindingComparison<CanonicalIlOperation>.Complete value => value,
            FindingComparison<CanonicalIlOperation>.Failed => null,
        };
        if (oldBody is null || newBody is null || complete is null)
        {
            return comparison;
        }

        return comparison.TransformPairs(pairs => ApplyBranchTargetValidation(
            pairs,
            oldBody.Instructions,
            [.. complete.OldAtoms.Select(static atom => atom.Payload)],
            newBody.Instructions,
            [.. complete.NewAtoms.Select(static atom => atom.Payload)]));
    }

    static FindingInspection<CanonicalIlOperation> InspectMethod(
        PEReader pe,
        MetadataReader reader,
        MethodDefinitionHandle methodHandle,
        FindingSubject subject,
        out MethodInstructions? body)
    {
        body = null;
        try
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                return Inspect(null, reader, subject);

            body = MethodInstructions.Decode(pe.GetMethodBody(method.RelativeVirtualAddress));
            return Inspect(body, reader, subject);
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            return new FindingInspection<CanonicalIlOperation>.Failed(
                new InspectionError(subject, InspectionDescriptor, ex.Message));
        }
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
                alignment[RequiredOrdinal(moved.Old)] = RequiredOrdinal(moved.New);
        }

        var builder = ImmutableArray.CreateBuilder<PairFinding<CanonicalIlOperation>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<CanonicalIlOperation>.Present present
                && !IlBodyDiff.BranchTargetsMatch(
                    oldInstructions,
                    RequiredOrdinal(present.Old),
                    newInstructions,
                    RequiredOrdinal(present.New),
                    alignment))
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

    static int RequiredOrdinal(Finding<CanonicalIlOperation> finding)
        => finding.Ordinal
            ?? throw new InvalidOperationException("An IL operation finding must retain its stream ordinal.");

    // One Finding per operation, carrying its content key and stream ordinal. Private:
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
                operations[i],
                Ordinal: i);
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
