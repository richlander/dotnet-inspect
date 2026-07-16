using System.Collections.Immutable;
using System.Text;

using ILInspector.Findings;

namespace ILInspector.Analysis;

/// <summary>Analysis observations and comparisons over the Finding substrate.</summary>
public static class AnalysisFindings
{
    public static readonly FindingDescriptor AllocationDescriptor =
        new("analysis.allocation", "Allocation occurrence");

    public static readonly FindingDescriptor CallSiteDescriptor =
        new("analysis.call-site", "Direct call site");

    public static readonly FindingDescriptor UnsafetyDescriptor =
        new("analysis.unsafety", "Unsafe operation");

    public static readonly FindingDescriptor UnsafeEvidenceDescriptor =
        new("analysis.unsafe-evidence", "Unsafe evidence");

    public static readonly FindingDescriptor ResourceLifecycleDescriptor =
        new("analysis.resource-lifecycle", "Resource lifecycle occurrence");

    /// <summary>
    /// Projects one method's allocation occurrences into IL order. An empty occurrence sequence is
    /// a complete empty census; acquisition failures belong to the caller that builds the body index.
    /// </summary>
    public static ImmutableArray<Finding<AllocationOccurrence>> InspectAllocations(
        IEnumerable<AllocationOccurrence> occurrences,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(subject);

        var ordered = occurrences
            .OrderBy(static occurrence => occurrence.ILOffset)
            .ThenBy(GetAllocationIdentityKey, StringComparer.Ordinal)
            .ToImmutableArray();
        var findings = ImmutableArray.CreateBuilder<Finding<AllocationOccurrence>>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            AllocationOccurrence occurrence = ordered[i];
            findings.Add(new Finding<AllocationOccurrence>(
                subject,
                AllocationDescriptor,
                new FindingKey(GetAllocationIdentityKey(occurrence)),
                occurrence,
                Ordinal: i,
                Detail: occurrence.Detail));
        }

        return findings.MoveToImmutable();
    }

    /// <summary>
    /// Compares two allocation censuses conservatively. Hard correspondence requires the same
    /// allocation source, kind, and allocated type; version-local IL coordinates never establish
    /// cross-version identity.
    /// </summary>
    public static FindingComparison<AllocationOccurrence> CompareAllocations(
        IEnumerable<AllocationOccurrence> oldOccurrences,
        IEnumerable<AllocationOccurrence> newOccurrences,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldOccurrences);
        ArgumentNullException.ThrowIfNull(newOccurrences);
        ArgumentNullException.ThrowIfNull(subject);

        FindingInspection<AllocationOccurrence> oldInspection =
            new FindingInspection<AllocationOccurrence>.Complete(
                InspectAllocations(oldOccurrences, subject));
        FindingInspection<AllocationOccurrence> newInspection =
            new FindingInspection<AllocationOccurrence>.Complete(
                InspectAllocations(newOccurrences, subject));
        return FindingComparison.Compare<AllocationOccurrence>(
                oldInspection,
                newInspection,
                acceptanceThreshold: acceptanceThreshold)
            .TransformPairs(ClassifyFacetChanges);
    }

    public static string GetAllocationIdentityKey(AllocationOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        // Analysis observations are heuristic evidence and may lack a resolved TypeRef. Keep the
        // census usable by falling back through the producer's remaining type evidence. Unknown
        // candidates remain conservatively aligned by stream order, and any non-provenance payload
        // difference is still classified as Changed.
        string allocatedType = occurrence.AllocatedType?.ToQualifiedDisplayString()
            ?? occurrence.RuntimeAllocationType
            ?? occurrence.Detail
            ?? "?";
        return CompositeKey(
            ((int)occurrence.Source).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)occurrence.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            allocatedType);
    }

    /// <summary>
    /// Projects one method's direct calls into IL order. The callee signature establishes exact
    /// correspondence; call opcode, dispatch kind, loop/multiplicity context, and version-local
    /// coordinates remain payload facets or provenance.
    /// </summary>
    public static ImmutableArray<Finding<DirectCall>> InspectCallSites(
        IEnumerable<DirectCall> calls,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(subject);

        var ordered = calls
            .OrderBy(static call => call.ILOffset)
            .ThenBy(GetCallSiteIdentityKey, StringComparer.Ordinal)
            .ToImmutableArray();
        var findings = ImmutableArray.CreateBuilder<Finding<DirectCall>>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            DirectCall call = ordered[i];
            findings.Add(new Finding<DirectCall>(
                subject,
                CallSiteDescriptor,
                new FindingKey(GetCallSiteIdentityKey(call)),
                call,
                Ordinal: i,
                Detail: call.Callee.Name));
        }

        return findings.MoveToImmutable();
    }

    /// <summary>
    /// Compares two direct-call censuses. Exact correspondence requires the same instantiated
    /// callee signature; dispatch, opcode, multiplicity, and loop-context changes remain
    /// classified transitions.
    /// </summary>
    public static FindingComparison<DirectCall> CompareCallSites(
        IEnumerable<DirectCall> oldCalls,
        IEnumerable<DirectCall> newCalls,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldCalls);
        ArgumentNullException.ThrowIfNull(newCalls);
        ArgumentNullException.ThrowIfNull(subject);

        FindingInspection<DirectCall> oldInspection =
            new FindingInspection<DirectCall>.Complete(InspectCallSites(oldCalls, subject));
        FindingInspection<DirectCall> newInspection =
            new FindingInspection<DirectCall>.Complete(InspectCallSites(newCalls, subject));
        return FindingComparison.Compare<DirectCall>(
                oldInspection,
                newInspection,
                acceptanceThreshold: acceptanceThreshold)
            .TransformPairs(ClassifyCallSiteFacetChanges);
    }

    public static string GetCallSiteIdentityKey(DirectCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var callee = call.Callee;
        var key = new StringBuilder();
        if (call.Kind == CallKind.CallIndirect && callee.Kind == MemberKind.Unsupported)
        {
            // Malformed signatures cannot supply structural identity. Keep the fallback
            // token-free and deliberately coarse so version-local token churn does not
            // fabricate a removal/addition.
            AppendKeyPart(key, "calli-unsupported");
            return key.ToString();
        }

        AppendMemberIdentity(key, callee);
        return key.ToString();
    }

    /// <summary>
    /// Projects exception-path resource lifecycle observations into acquisition order. Identity
    /// uses the containing method and exact boundary operations; version-local IL coordinates
    /// remain payload evidence rather than correspondence.
    /// </summary>
    public static ImmutableArray<Finding<ResourceLifecycleOccurrence>> InspectResourceLifecycles(
        IEnumerable<ResourceLifecycleOccurrence> occurrences,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(subject);

        var ordered = occurrences
            .OrderBy(static occurrence => occurrence.Method.MetadataToken)
            .ThenBy(static occurrence => occurrence.AcquireOffset)
            .ThenBy(GetResourceLifecycleIdentityKey, StringComparer.Ordinal)
            .ToImmutableArray();
        var findings =
            ImmutableArray.CreateBuilder<Finding<ResourceLifecycleOccurrence>>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            ResourceLifecycleOccurrence occurrence = ordered[i];
            findings.Add(new Finding<ResourceLifecycleOccurrence>(
                subject,
                ResourceLifecycleDescriptor,
                new FindingKey(GetResourceLifecycleIdentityKey(occurrence)),
                occurrence,
                Ordinal: i,
                Detail: occurrence.Shape));
        }

        return findings.MoveToImmutable();
    }

    public static string GetResourceLifecycleIdentityKey(
        ResourceLifecycleOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        var key = new StringBuilder();
        MethodIdentity method = occurrence.Method;
        AppendKeyPart(key, method.AssemblyName);
        AppendKeyPart(key, GenericMemberIdentity.KeyFragment(method.DeclaringType));
        AppendKeyPart(key, method.Name);
        AppendTypes(key, method.ParameterTypes);
        AppendKeyPart(key, GenericMemberIdentity.KeyFragment(method.ReturnType));
        AppendKeyPart(key, occurrence.Resource);
        AppendKeyPart(key, occurrence.Shape);
        AppendKeyPart(
            key,
            occurrence.Boundaries.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        foreach (ResourceBoundaryEvidence boundary in occurrence.Boundaries)
            AppendMemberIdentity(key, boundary.Operation);
        return key.ToString();
    }

    /// <summary>
    /// Projects one method's definite unsafe IL operations into IL order. An empty sequence is a
    /// complete safe-operation census; declaration and signature evidence use
    /// <see cref="InspectUnsafeEvidence"/> instead.
    /// </summary>
    public static ImmutableArray<Finding<UnsafetyOccurrence>> InspectUnsafety(
        IEnumerable<UnsafetyOccurrence> occurrences,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(subject);

        var ordered = occurrences
            .OrderBy(static occurrence => occurrence.ILOffset)
            .ThenBy(GetUnsafetyIdentityKey, StringComparer.Ordinal)
            .ToImmutableArray();
        var findings = ImmutableArray.CreateBuilder<Finding<UnsafetyOccurrence>>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            UnsafetyOccurrence occurrence = ordered[i];
            findings.Add(new Finding<UnsafetyOccurrence>(
                subject,
                UnsafetyDescriptor,
                new FindingKey(GetUnsafetyIdentityKey(occurrence)),
                occurrence,
                Ordinal: i,
                Detail: occurrence.Detail));
        }

        return findings.MoveToImmutable();
    }

    public static FindingComparison<UnsafetyOccurrence> CompareUnsafety(
        IEnumerable<UnsafetyOccurrence> oldOccurrences,
        IEnumerable<UnsafetyOccurrence> newOccurrences,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldOccurrences);
        ArgumentNullException.ThrowIfNull(newOccurrences);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare<UnsafetyOccurrence>(
            new FindingInspection<UnsafetyOccurrence>.Complete(
                InspectUnsafety(oldOccurrences, subject)),
            new FindingInspection<UnsafetyOccurrence>.Complete(
                InspectUnsafety(newOccurrences, subject)),
            acceptanceThreshold: acceptanceThreshold);
    }

    public static string GetUnsafetyIdentityKey(UnsafetyOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return CompositeKey(
            ((int)occurrence.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            occurrence.Detail ?? "");
    }

    /// <summary>
    /// Projects one method's declaration, signature, local, call, and opcode evidence as an
    /// identity multiset. Evidence may have no IL coordinate, so it does not fabricate ordinals.
    /// Cross-family deduplication against definite unsafe operations is consumer projection policy.
    /// </summary>
    public static ImmutableArray<Finding<UnsafeEvidence>> InspectUnsafeEvidence(
        IEnumerable<UnsafeEvidence> evidence,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(subject);

        return
        [
            .. evidence
                .OrderBy(GetUnsafeEvidenceIdentityKey, StringComparer.Ordinal)
                .ThenBy(static item => item.ILOffset ?? -1)
                .Select(item => new Finding<UnsafeEvidence>(
                    subject,
                    UnsafeEvidenceDescriptor,
                    new FindingKey(GetUnsafeEvidenceIdentityKey(item)),
                    item,
                    Detail: item.Detail)),
        ];
    }

    public static FindingComparison<UnsafeEvidence> CompareUnsafeEvidence(
        IEnumerable<UnsafeEvidence> oldEvidence,
        IEnumerable<UnsafeEvidence> newEvidence,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldEvidence);
        ArgumentNullException.ThrowIfNull(newEvidence);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare<UnsafeEvidence>(
            new FindingInspection<UnsafeEvidence>.Complete(
                InspectUnsafeEvidence(oldEvidence, subject)),
            new FindingInspection<UnsafeEvidence>.Complete(
                InspectUnsafeEvidence(newEvidence, subject)),
            new FindingMatchOptions { MatchMode = FindingMatchMode.IdentitySet });
    }

    public static string GetUnsafeEvidenceIdentityKey(UnsafeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return CompositeKey(
            evidence.ILOffset is null ? "member" : "body",
            evidence.Reason,
            evidence.Kind,
            evidence.Detail);
    }

    static ImmutableArray<PairFinding<AllocationOccurrence>> ClassifyFacetChanges(
        ImmutableArray<PairFinding<AllocationOccurrence>> pairs)
    {
        var builder = ImmutableArray.CreateBuilder<PairFinding<AllocationOccurrence>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<AllocationOccurrence>.Present present
                && !SameSemanticFacets(present.Old.Payload, present.New.Payload))
            {
                builder.Add(new PairFinding<AllocationOccurrence>.Changed(
                    present.Old,
                    present.New,
                    present.Difference,
                    DescribeFacetChanges(present.Old.Payload, present.New.Payload)));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.MoveToImmutable();
    }

    static ImmutableArray<PairFinding<DirectCall>> ClassifyCallSiteFacetChanges(
        ImmutableArray<PairFinding<DirectCall>> pairs)
    {
        var builder = ImmutableArray.CreateBuilder<PairFinding<DirectCall>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<DirectCall>.Present present
                && !SameCallSiteFacets(present.Old.Payload, present.New.Payload))
            {
                builder.Add(new PairFinding<DirectCall>.Changed(
                    present.Old,
                    present.New,
                    present.Difference,
                    DescribeCallSiteFacetChanges(present.Old.Payload, present.New.Payload)));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.MoveToImmutable();
    }

    static bool SameSemanticFacets(AllocationOccurrence oldOccurrence, AllocationOccurrence newOccurrence)
        => oldOccurrence with
        {
            Method = newOccurrence.Method,
            ILOffset = newOccurrence.ILOffset,
            OperandToken = newOccurrence.OperandToken,
        } == newOccurrence;

    static string DescribeFacetChanges(
        AllocationOccurrence oldOccurrence,
        AllocationOccurrence newOccurrence)
    {
        var changes = new List<string>();
        AddChange(changes, "detail", oldOccurrence.Detail, newOccurrence.Detail);
        AddChange(changes, "heap allocation", oldOccurrence.CountsAsHeapAllocation, newOccurrence.CountsAsHeapAllocation);
        AddChange(changes, "frequency", oldOccurrence.Frequency, newOccurrence.Frequency);
        AddChange(changes, "in loop", oldOccurrence.InLoop, newOccurrence.InLoop);
        AddChange(changes, "escape", oldOccurrence.Escape, newOccurrence.Escape);
        AddChange(changes, "runtime type", oldOccurrence.RuntimeAllocationType, newOccurrence.RuntimeAllocationType);
        AddChange(changes, "path", oldOccurrence.PathContext, newOccurrence.PathContext);
        AddChange(changes, "path confidence", oldOccurrence.PathConfidence, newOccurrence.PathConfidence);
        AddChange(changes, "estimated size", oldOccurrence.EstimatedSizeBytes, newOccurrence.EstimatedSizeBytes);
        AddChange(changes, "size tier", oldOccurrence.SizeTier, newOccurrence.SizeTier);
        AddChange(changes, "post-dominance", oldOccurrence.PostDominance, newOccurrence.PostDominance);
        AddChange(changes, "escape kind", oldOccurrence.EscapeKind, newOccurrence.EscapeKind);
        AddChange(changes, "multiplicity", oldOccurrence.Multiplicity, newOccurrence.Multiplicity);
        AddChange(changes, "churned type", oldOccurrence.ChurnedType, newOccurrence.ChurnedType);
        return changes.Count == 0 ? "other facets changed" : string.Join("; ", changes);
    }

    static bool SameCallSiteFacets(DirectCall oldCall, DirectCall newCall)
        // Deliberate allow list: new DirectCall fields are identity or provenance unless
        // explicitly opted into transition classification here.
        => oldCall.Kind == newCall.Kind
            && oldCall.InLoop == newCall.InLoop
            && oldCall.Multiplicity == newCall.Multiplicity
            && string.Equals(oldCall.Opcode, newCall.Opcode, StringComparison.Ordinal);

    static string DescribeCallSiteFacetChanges(DirectCall oldCall, DirectCall newCall)
    {
        var changes = new List<string>();
        AddChange(changes, "kind", oldCall.Kind, newCall.Kind);
        AddChange(changes, "in loop", oldCall.InLoop, newCall.InLoop);
        AddChange(changes, "multiplicity", oldCall.Multiplicity, newCall.Multiplicity);
        AddChange(changes, "opcode", oldCall.Opcode, newCall.Opcode);
        return changes.Count == 0 ? "other facets changed" : string.Join("; ", changes);
    }

    static string CompositeKey(params string[] parts)
    {
        var key = new StringBuilder();
        foreach (string part in parts)
            AppendKeyPart(key, part);
        return key.ToString();
    }

    static void AppendTypes(StringBuilder key, ImmutableArray<TypeRef> types)
    {
        AppendKeyPart(key, types.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (TypeRef type in types)
            AppendKeyPart(key, GenericMemberIdentity.KeyFragment(type));
    }

    static void AppendMemberIdentity(StringBuilder key, MemberRef member)
    {
        AppendKeyPart(
            key,
            ((int)member.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        AppendKeyPart(key, GenericMemberIdentity.KeyFragment(member.DeclaringType));
        AppendKeyPart(key, member.Name);
        AppendKeyPart(key, member.HasThis ? "instance" : "static");
        AppendKeyPart(
            key,
            member.SignatureHeader.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        AppendKeyPart(
            key,
            member.GenericArity.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        AppendTypes(key, member.TypeArguments);
        AppendTypes(key, member.ParameterTypes);
        AppendKeyPart(key, GenericMemberIdentity.KeyFragment(member.ReturnType));
        AppendTypes(key, member.OpenSignatureParameters);
        AppendKeyPart(key, GenericMemberIdentity.KeyFragment(member.OpenSignatureReturn));
    }

    static void AppendKeyPart(StringBuilder key, string part)
    {
        key.Append(part.Length);
        key.Append(':');
        key.Append(part);
    }

    static void AddChange<T>(List<string> changes, string name, T oldValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
            return;

        changes.Add($"{name}: {Format(oldValue)} -> {Format(newValue)}");
    }

    static string Format<T>(T value) => value?.ToString() ?? "none";
}
