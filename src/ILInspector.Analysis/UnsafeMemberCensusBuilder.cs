using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// One physical method's facts for the unsafe member census: its body
/// availability and analysis outcome, its authenticated owner, and its
/// inventory roles.
/// </summary>
internal readonly record struct UnsafeMemberBodyFacts(
    int Token,
    MethodIdentity? Body,
    bool InScope,
    bool IsExtensionDeclarationSkeleton,
    MethodBodyAvailability Availability,
    bool AnalysisFailed,
    string? FailureDetail,
    bool RequiresDeclaredOwner,
    DeclaredOwnerResolution OwnerResolution,
    bool OwnerResolutionFailed,
    MethodIdentity? DeclaredOwner,
    ImmutableArray<UnsafeMemberUseEvidence> Evidence);

/// <summary>Public root membership for exposure.</summary>
internal sealed record UnsafeMemberRootSet(
    IReadOnlySet<int> RootTokens,
    bool IsComplete,
    bool Failed)
{
    public static UnsafeMemberRootSet Unavailable { get; } =
        new(new HashSet<int>(), IsComplete: false, Failed: true);
}

/// <summary>
/// Folds physical-body inventory roles into one finding per declared member
/// under docs/design/unsafe-member-findings.md.
/// </summary>
internal static class UnsafeMemberCensusBuilder
{
    public static UnsafeMemberCensus Build(
        IReadOnlyList<UnsafeMemberBodyFacts> bodies,
        bool hasFullMethodEvidenceScope,
        ReferenceAssemblyState referenceAssembly,
        UnsafeMemberRootSet roots)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(roots);

        var limitations =
            ImmutableArray.CreateBuilder<UnsafeMemberLimitation>();
        if (!hasFullMethodEvidenceScope)
            limitations.Add(ImageLimitation(UnsafeMemberLimitationReason.ScopedReceipt));
        if (referenceAssembly == ReferenceAssemblyState.Reference)
            limitations.Add(ImageLimitation(UnsafeMemberLimitationReason.ReferenceAssembly));
        else if (referenceAssembly == ReferenceAssemblyState.Undecidable)
            limitations.Add(ImageLimitation(UnsafeMemberLimitationReason.ReferenceAssemblyUndecidable));
        if (roots.Failed)
            limitations.Add(ImageLimitation(UnsafeMemberLimitationReason.PublicRootInventoryFailed));
        else if (!roots.IsComplete)
            limitations.Add(ImageLimitation(UnsafeMemberLimitationReason.PublicRootInventoryBounded));

        var identities = new Dictionary<int, MethodIdentity>();
        foreach (UnsafeMemberBodyFacts body in bodies)
        {
            if (body.Body is { } identity)
                identities[body.Token] = identity;
        }

        var evidenceByOwner =
            new SortedDictionary<int, ImmutableArray<UnsafeMemberFindingEvidence>.Builder>();
        var owners = new Dictionary<int, MethodIdentity>();
        var uninspectedByOwner =
            new Dictionary<int, ImmutableArray<UnsafeMemberLimitation>.Builder>();
        var bodyLimitations =
            new List<(int Token, UnsafeMemberLimitation Limitation)>();

        foreach (UnsafeMemberBodyFacts body in bodies)
        {
            // A scoped receipt does not classify bodies outside its scope;
            // its one receipt-level limitation stands for them.
            if (!hasFullMethodEvidenceScope && !body.InScope)
                continue;
            // An extension declaration copy duplicates its implementation
            // method's contract and has no implementation body.
            if (body.IsExtensionDeclarationSkeleton)
                continue;
            if (body.Body is not { } physical)
            {
                if (body.AnalysisFailed)
                {
                    bodyLimitations.Add((
                        body.Token,
                        new UnsafeMemberLimitation(
                            UnsafeMemberLimitationReason.BodyAnalysisFailed,
                            body.Token,
                            Body: null,
                            DeclaredMember: null,
                            Evidence: [],
                            body.FailureDetail)));
                }
                continue;
            }

            MethodIdentity? owner = ResolveOwner(body, physical, identities);
            UnsafeMemberLimitationReason? failure =
                body.AnalysisFailed
                    ? UnsafeMemberLimitationReason.BodyAnalysisFailed
                    : null;
            ImmutableArray<UnsafeMemberFindingEvidence> evidence =
                ProjectEvidence(body, physical, ownBody: owner == physical);

            if (owner is null)
            {
                // An unattributed body joins no finding; it is a limitation
                // only when it could hide or carries evidence.
                if (failure is not null || !evidence.IsEmpty)
                {
                    bodyLimitations.Add((
                        body.Token,
                        new UnsafeMemberLimitation(
                            failure ?? UnsafeMemberLimitationReason.UnattributedGeneratedBody,
                            body.Token,
                            physical,
                            DeclaredMember: null,
                            evidence,
                            body.FailureDetail)));
                }
                continue;
            }

            owners.TryAdd(owner.MetadataToken, owner);
            if (!evidence.IsEmpty)
            {
                if (!evidenceByOwner.TryGetValue(owner.MetadataToken, out var ownerEvidence))
                {
                    ownerEvidence = ImmutableArray.CreateBuilder<UnsafeMemberFindingEvidence>();
                    evidenceByOwner.Add(owner.MetadataToken, ownerEvidence);
                }
                ownerEvidence.AddRange(evidence);
            }

            if (failure is { } reason)
            {
                var limitation = new UnsafeMemberLimitation(
                    reason,
                    body.Token,
                    physical,
                    owner,
                    evidence,
                    body.FailureDetail);
                bodyLimitations.Add((body.Token, limitation));
                if (!uninspectedByOwner.TryGetValue(owner.MetadataToken, out var ownerGaps))
                {
                    ownerGaps = ImmutableArray.CreateBuilder<UnsafeMemberLimitation>();
                    uninspectedByOwner.Add(owner.MetadataToken, ownerGaps);
                }
                ownerGaps.Add(limitation);
            }
        }

        var members = ImmutableArray.CreateBuilder<UnsafeMemberFinding>(evidenceByOwner.Count);
        foreach ((int token, var evidence) in evidenceByOwner)
        {
            MethodIdentity member = owners[token];
            members.Add(new UnsafeMemberFinding(
                member,
                member.CallerUnsafeMode == CallerUnsafeMode.Explicit,
                evidence.ToImmutable(),
                uninspectedByOwner.TryGetValue(token, out var gaps)
                    ? gaps.ToImmutable()
                    : [],
                Exposure(token, roots)));
        }

        foreach ((_, UnsafeMemberLimitation limitation) in bodyLimitations
            .OrderBy(static entry => entry.Token))
        {
            limitations.Add(limitation);
        }

        return new UnsafeMemberCensus(
            members.MoveToImmutable(),
            limitations.ToImmutable());
    }

    // A body that needs no declared owner is its own declared member; a
    // generated body joins only an owner the resolution authenticated.
    static MethodIdentity? ResolveOwner(
        UnsafeMemberBodyFacts body,
        MethodIdentity physical,
        IReadOnlyDictionary<int, MethodIdentity> identities)
    {
        if (!body.RequiresDeclaredOwner)
            return physical;
        if (body.OwnerResolutionFailed
            || body.OwnerResolution != DeclaredOwnerResolution.Resolved
            || body.DeclaredOwner is not { } declared
            || declared.ModuleVersionId != physical.ModuleVersionId)
        {
            return null;
        }
        return identities.TryGetValue(declared.MetadataToken, out MethodIdentity? canonical)
            ? canonical
            : declared;
    }

    // An explicit contract belongs to the member that declares it; generated
    // bodies contribute body roles and explicit-contract calls only.
    static ImmutableArray<UnsafeMemberFindingEvidence> ProjectEvidence(
        UnsafeMemberBodyFacts body,
        MethodIdentity physical,
        bool ownBody)
    {
        if (body.Evidence.IsDefaultOrEmpty)
            return [];
        var evidence = ImmutableArray.CreateBuilder<UnsafeMemberFindingEvidence>(body.Evidence.Length);
        foreach (UnsafeMemberUseEvidence item in body.Evidence)
        {
            if (!ownBody && item.Kind == UnsafeMemberUseKind.ExplicitContract)
                continue;
            evidence.Add(new UnsafeMemberFindingEvidence(
                item.Kind,
                physical,
                item.ILOffset,
                item.Detail,
                item.ContractSource));
        }
        return evidence.ToImmutable();
    }

    static UnsafeMemberExposure Exposure(int token, UnsafeMemberRootSet roots)
        => roots.RootTokens.Contains(token)
            ? UnsafeMemberExposure.Public
            : roots.IsComplete && !roots.Failed
                ? UnsafeMemberExposure.NonPublic
                : UnsafeMemberExposure.Unknown;

    static UnsafeMemberLimitation ImageLimitation(UnsafeMemberLimitationReason reason)
        => new(reason, BodyToken: null, Body: null, DeclaredMember: null, Evidence: []);
}
