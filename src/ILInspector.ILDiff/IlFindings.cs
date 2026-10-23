using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using Inspector.Findings;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.ILDiff;

/// <summary>
/// Adapts IL method bodies onto the domain-free finding substrate: it drains each body's
/// <see cref="IlCensusCursor"/> into <see cref="Finding{T}"/> atoms, runs the shared
/// <see cref="FindingMatcher"/>, and folds the alignment into <see cref="PairFinding{T}"/>
/// transitions. The cursor is the one producer of canonical IL, shared with
/// <see cref="IlBodyDiff"/>, so the census the matcher aligns is the census a lockstep
/// comparison reads. The committed LCS core reproduces <see cref="IlBodyDiff"/> on move-free
/// bodies, and the move pass recovers relocations that the order-preserving diff cannot. A
/// single-version body is just its <see cref="Inspect(MethodInstructions?, MetadataReader?, FindingSubject, IlBodyDiffNormalization)"/>
/// census: no matcher, no pairs, and no bound, which is the matcher's and applied at
/// <see cref="Compare(FindingInspection{CanonicalIlOperation}, FindingInspection{CanonicalIlOperation}, int)"/>.
/// </summary>
public static class IlFindings
{
    /// <summary>The finding descriptor for a single IL operation occurrence.</summary>
    public static readonly FindingDescriptor OperationDescriptor = new("il.op", "IL operation");

    /// <summary>The descriptor used when an IL inspection cannot produce a census.</summary>
    public static readonly FindingDescriptor InspectionDescriptor = new("il.inspect", "IL inspection");

    /// <summary>
    /// The maximum canonical IL operations a census may hold for the ordered matcher to run over
    /// it. The matrix is <c>(oldCount + 1) * (newCount + 1)</c>, so two censuses at this same limit
    /// remain within <see cref="FindingMatcher.MaxOrderedMatchCells"/>. The bound is the matcher's:
    /// a census beyond it still drains <c>Complete</c>, and the comparison of such a pair is a
    /// visible <see cref="FindingComparison{T}.Failed"/> rather than a matcher exception.
    /// </summary>
    public static readonly int MaxCanonicalOperations =
        (int)Math.Sqrt(FindingMatcher.MaxOrderedMatchCells) - 1;

    /// <summary>
    /// Inspects one method body into a complete census, an absent-body state, or a canonicalization
    /// failure, by draining the body's <see cref="IlCensusCursor"/>. A null body represents a method
    /// with no IL body, such as an abstract or extern method.
    /// </summary>
    /// <param name="normalization">
    /// The normalization the census is taken under; a consumer's named policy,
    /// such as Finding Diff Any's Body policy, or none.
    /// </param>
    public static FindingInspection<CanonicalIlOperation> Inspect(
        MethodInstructions? body,
        MetadataReader? reader,
        FindingSubject subject,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => IlCensusCursor.Create(body, reader, subject, normalization).Drain();

    /// <summary>Inspects one method body through its image's side, under the side's policy.</summary>
    public static FindingInspection<CanonicalIlOperation> Inspect(
        IlCensusSide side,
        MethodInstructions? body,
        FindingSubject subject)
        => IlCensusCursor.Create(side, body, subject).Drain();

    public static FindingComparison<CanonicalIlOperation> Compare(
        MethodInstructions? oldBody,
        MetadataReader? oldReader,
        MethodInstructions? newBody,
        MetadataReader? newReader,
        FindingSubject subject,
        int acceptanceThreshold = 100,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var oldInspection = Inspect(oldBody, oldReader, subject, normalization);
        var newInspection = Inspect(newBody, newReader, subject, normalization);
        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    /// <summary>
    /// Inspects a method definition directly from its PE and metadata readers, by draining the
    /// cursor over the method's raw IL.
    /// </summary>
    public static FindingInspection<CanonicalIlOperation> Inspect(
        PEReader pe,
        MetadataReader reader,
        MethodDefinitionHandle method,
        FindingSubject subject,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => IlCensusCursor.Create(pe, reader, method, subject, normalization).Drain();

    /// <summary>Inspects a method definition from its image through the image's side.</summary>
    public static FindingInspection<CanonicalIlOperation> Inspect(
        IlCensusSide side,
        PEReader pe,
        MethodDefinitionHandle method,
        FindingSubject subject)
        => IlCensusCursor.Create(side, pe, method, subject).Drain();

    /// <summary>
    /// Compares already-acquired inspections. This overload preserves explicit absent sides for
    /// added and removed methods. Target validation runs over the atoms' instruction indexes, so
    /// it needs no body; a census beyond <see cref="MaxCanonicalOperations"/> on either side is a
    /// failed comparison with both inspections retained.
    /// </summary>
    public static FindingComparison<CanonicalIlOperation> Compare(
        FindingInspection<CanonicalIlOperation> oldInspection,
        FindingInspection<CanonicalIlOperation> newInspection,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldInspection);
        ArgumentNullException.ThrowIfNull(newInspection);
        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    public static FindingComparison<CanonicalIlOperation> Compare(
        PEReader oldPe,
        MetadataReader oldReader,
        MethodDefinitionHandle oldMethod,
        PEReader newPe,
        MetadataReader newReader,
        MethodDefinitionHandle newMethod,
        FindingSubject subject,
        int acceptanceThreshold = 100,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
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

        var oldInspection = Inspect(oldPe, oldReader, oldMethod, subject, normalization);
        var newInspection = Inspect(newPe, newReader, newMethod, subject, normalization);
        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    /// <summary>Compares two method definitions through their images' sides, under the sides' one policy.</summary>
    public static FindingComparison<CanonicalIlOperation> Compare(
        IlCensusSide oldSide,
        PEReader oldPe,
        MethodDefinitionHandle oldMethod,
        IlCensusSide newSide,
        PEReader newPe,
        MethodDefinitionHandle newMethod,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldSide);
        ArgumentNullException.ThrowIfNull(newSide);
        IlBodyDiff.RequireOnePolicy(oldSide, newSide);
        var oldInspection = Inspect(oldSide, oldPe, oldMethod, subject);
        var newInspection = Inspect(newSide, newPe, newMethod, subject);
        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    internal static FindingComparison<CanonicalIlOperation> CompareInspections(
        FindingInspection<CanonicalIlOperation> oldInspection,
        FindingInspection<CanonicalIlOperation> newInspection,
        int acceptanceThreshold)
    {
        // The bound is the matcher's, applied after both drains: a pair beyond it is
        // a visible failed comparison over two retained inspections, never a matcher
        // exception and never an empty diff.
        string? bound = BoundFailure(oldInspection, "old");
        string? newBound = BoundFailure(newInspection, "new");
        if (bound is not null || newBound is not null)
        {
            return FindingComparison.Failed(
                oldInspection,
                newInspection,
                string.Join("; ", new[] { bound, newBound }.Where(static failure => failure is not null)));
        }

        var comparison = FindingComparison.Compare(
            oldInspection,
            newInspection,
            acceptanceThreshold: acceptanceThreshold);
        var complete = comparison switch
        {
            FindingComparison<CanonicalIlOperation>.Complete value => value,
            FindingComparison<CanonicalIlOperation>.Failed => null,
        };
        if (complete is null || complete.OldAtoms.IsEmpty || complete.NewAtoms.IsEmpty)
        {
            return comparison;
        }

        return comparison.TransformPairs(pairs => ApplyBranchTargetValidation(
            pairs,
            [.. complete.OldAtoms.Select(static atom => atom.Payload)],
            [.. complete.NewAtoms.Select(static atom => atom.Payload)]));
    }

    static string? BoundFailure(FindingInspection<CanonicalIlOperation> inspection, string side)
    {
        if (inspection.Value is not FindingInspection<CanonicalIlOperation>.Complete complete
            || complete.Findings.Length <= MaxCanonicalOperations)
        {
            return null;
        }

        return $"IL comparison skipped: the {side} body has {complete.Findings.Length:N0} canonical operations; " +
            $"the ordered matcher's limit is {MaxCanonicalOperations:N0}";
    }

    internal static FindingInspection<CanonicalIlOperation> InspectMethod(
        IlCensusSide side,
        PEReader pe,
        MethodDefinitionHandle methodHandle,
        FindingSubject subject,
        out MethodInstructions? body,
        out MethodBodyBlock? methodBody)
    {
        body = null;
        methodBody = null;
        try
        {
            MetadataReader reader = side.Reader;
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                return Inspect(side, null, subject);

            methodBody = pe.GetMethodBody(method.RelativeVirtualAddress);
            body = MethodInstructions.Decode(methodBody);
            return IlCensusCursor.Create(side, body, subject).Drain();
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

    // IdentityKey deliberately ignores an operation's targets (matching CanonicalEquals): a branch's
    // target, a switch's arms, and an exception clause's extents. A matched branch whose target was
    // retargeted, or a clause whose extent moved, would otherwise read as unchanged. Reuse
    // IlBodyDiff's own alignment map and target decision verbatim (not a reimplementation) over the
    // atoms' instruction indexes, then overlay the finding Moved mappings so a branch that targets a
    // relocated instruction is judged in the finding frame (its target moved with it) rather than
    // being falsely retargeted. On move-free inputs there are no Moved pairs, so the map is exactly
    // IlBodyDiff's.
    static ImmutableArray<PairFinding<CanonicalIlOperation>> ApplyBranchTargetValidation(
        ImmutableArray<PairFinding<CanonicalIlOperation>> pairs,
        ImmutableArray<CanonicalIlOperation> oldOps,
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
                && !IlBodyDiff.TargetsMatch(
                    oldOps,
                    RequiredOrdinal(present.Old),
                    newOps,
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
                    present.Detail is null ? MovedTargetDetail(present.Old) : $"{present.Detail}; {MovedTargetDetail(present.Old)}"));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.ToImmutable();
    }

    static string MovedTargetDetail(Finding<CanonicalIlOperation> finding)
        => finding.Payload.IsExceptionClause ? "clause extent moved" : "branch retargeted";

    static int RequiredOrdinal(Finding<CanonicalIlOperation> finding)
        => finding.Ordinal
            ?? throw new InvalidOperationException("An IL operation finding must retain its stream ordinal.");

    /// <summary>
    /// The canonical content key for an operation, the matcher projection of its canonical bytes,
    /// defined so that key equality is exactly the <see cref="IlBodyDiff"/> canonical-equality
    /// relation (branch targets ignored; switch arms compared by count). This keeps the committed
    /// core aligned with the existing diff. A token operand's key carries its Symbol Key's bytes
    /// losslessly, one char per byte, never its formatted text, so two spellings whose bytes
    /// differ never share a key string. ScopeKey is left null: move detection is corroborated
    /// by run contiguity, and EH/loop-region scope is the Attach layer's concern (issue #2564).
    /// </summary>
    public static string GetIdentityKey(CanonicalIlOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Operand is null)
            return operation.OpcodeFamily;

        IlOperandIdentity operand = operation.Operand;
        return operand.Kind switch
        {
            IlOperandIdentityKind.BranchTarget => $"{operation.OpcodeFamily}|br",
            IlOperandIdentityKind.SwitchTargets => $"{operation.OpcodeFamily}|switch:{operation.Targets.Length}",
            _ when operand.TryGetKey(out StagedSymbolKey<IlOperandPart> key) => KeyText($"{operation.OpcodeFamily}|{operand.Kind}:", key),
            _ => $"{operation.OpcodeFamily}|{operand.Kind}:{operand.Value}",
        };
    }

    // A key's bytes widened one per char after the prefix. A key's first byte is
    // a marker below 0x40, which no formatted value's text begins with, so a
    // value identity and a key identity of one kind never share a string.
    static string KeyText(string prefix, StagedSymbolKey<IlOperandPart> key)
        => string.Create(prefix.Length + key.Length, (prefix, key), static (chars, state) =>
        {
            state.prefix.AsSpan().CopyTo(chars);
            Span<char> rest = chars[state.prefix.Length..];
            byte[]? rented = null;
            Span<byte> bytes = state.key.Length <= 512
                ? stackalloc byte[512]
                : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(state.key.Length));
            bytes = bytes[..state.key.Length];
            state.key.CopyTo(bytes);
            Encoding.Latin1.GetChars(bytes, rest);
            if (rented is not null)
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        });
}
