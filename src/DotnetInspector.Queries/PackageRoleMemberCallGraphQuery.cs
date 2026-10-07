using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact implementation member selected from one package-role projection.
/// </summary>
public sealed record PackageRoleMemberCallGraphFocus
{
    public PackageRoleMemberCallGraphFocus(
        PackageRootIdentity package,
        Guid moduleVersionId,
        int methodToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A package-role call-graph focus requires a module version identifier.",
                nameof(moduleVersionId));
        }

        Package = package;
        ModuleVersionId = moduleVersionId;
        MethodToken = methodToken;
    }

    public PackageRootIdentity Package { get; }

    public Guid ModuleVersionId { get; }

    public int MethodToken { get; }
}

public enum PackageRoleMemberCallGraphUnavailableReason
{
    FocusParticipantUnavailable,
    FocusParticipantAmbiguous,
    FocusMethodInvalid,
    FocusMethodBodyUnavailable,
}

/// <summary>
/// Expected reason an exact package implementation member cannot produce a
/// call graph.
/// </summary>
public sealed record PackageRoleMemberCallGraphFailure(
    PackageRoleMemberCallGraphUnavailableReason Reason,
    string Detail);

/// <summary>
/// Terminal result of one package-role external-focused member call graph.
/// </summary>
public abstract record PackageRoleMemberCallGraphOutcome
{
    private PackageRoleMemberCallGraphOutcome()
    {
    }

    public sealed record Available(
        InspectionGraphDocument Document,
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages,
        ImmutableArray<PackageRoleMemberCallGraphNodePlatform> NodePlatforms,
        PackageIntrinsicCoreLibraryIneligibilityReceipt?
            IntrinsicCoreLibraryIneligibility,
        ImmutableArray<PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
            IntrinsicCoreLibraryOccurrences,
        ImmutableArray<PackageAssemblyReferenceCallOccurrenceEvidence>
            AssemblyReferenceOccurrences)
        : PackageRoleMemberCallGraphOutcome;

    public sealed record Unavailable(
        PackageRoleMemberCallGraphFailure Failure)
        : PackageRoleMemberCallGraphOutcome;
}

/// <summary>
/// Exact package Root that uniquely contributed one graph node.
/// </summary>
public sealed record PackageRoleMemberCallGraphNodePackage(
    int NodeId,
    PackageRootIdentity Package);

/// <summary>
/// Exact certified Platform target and Library that uniquely contributed one
/// graph node.
/// </summary>
public sealed record PackageRoleMemberCallGraphNodePlatform(
    int NodeId,
    PlatformFamilyTarget Target,
    AssemblyReferenceIdentity LibraryIdentity);

/// <summary>
/// Owner-issued evidence that one physical intrinsic CoreLib call occurrence
/// belongs to an exact CoreLib-ineligible package context.
/// </summary>
public sealed class PackageIntrinsicCoreLibraryCallOccurrenceEvidence
{
    internal PackageIntrinsicCoreLibraryCallOccurrenceEvidence(
        int occurrenceId,
        CallGraphCallSiteEvidence callSite,
        Analysis.MemberCorrespondenceEvidence.UnresolvedBinding
            correspondence,
        PackageIntrinsicCoreLibraryParticipantEvidence origin,
        PackageIntrinsicCoreLibraryIneligibilityReceipt context)
    {
        OccurrenceId = occurrenceId;
        CallSite = callSite;
        Correspondence = correspondence;
        Origin = origin;
        Context = context;
    }

    public int OccurrenceId { get; }

    public CallGraphCallSiteEvidence CallSite { get; }

    public Analysis.MemberCorrespondenceEvidence.UnresolvedBinding
        Correspondence { get; }

    public PackageIntrinsicCoreLibraryParticipantEvidence Origin { get; }

    public PackageIntrinsicCoreLibraryIneligibilityReceipt Context { get; }
}

/// <summary>
/// Owner-issued evidence for one ordinary AssemblyRef call occurrence from
/// an exact package participant and binding-policy state.
/// </summary>
public class PackageAssemblyReferenceBindingEvidence
{
    internal PackageAssemblyReferenceBindingEvidence(
        CallGraphCallSiteEvidence callSite,
        PackageAssemblyRoleParticipant origin,
        AssemblyBindingRequest request,
        AssemblyBindingSelectionSnapshot context)
    {
        CallSite = callSite;
        Origin = origin;
        Request = request;
        Context = context;
    }

    public CallGraphCallSiteEvidence CallSite { get; }

    public PackageAssemblyRoleParticipant Origin { get; }

    public AssemblyBindingRequest Request { get; }

    public AssemblyBindingSelectionSnapshot Context { get; }

    public AssemblyBindingPolicyVersion BindingPolicyVersion =>
        Context.Version;
}

/// <summary>
/// Owner-issued evidence for one unresolved ordinary AssemblyRef call
/// occurrence.
/// </summary>
public sealed class PackageAssemblyReferenceCallOccurrenceEvidence :
    PackageAssemblyReferenceBindingEvidence
{
    internal PackageAssemblyReferenceCallOccurrenceEvidence(
        int occurrenceId,
        CallGraphCallSiteEvidence callSite,
        Analysis.MemberCorrespondenceEvidence.UnresolvedBinding
            correspondence,
        PackageAssemblyRoleParticipant origin,
        AssemblyBindingRequest request,
        AssemblyBindingSelectionSnapshot context)
        : base(callSite, origin, request, context)
    {
        OccurrenceId = occurrenceId;
        Correspondence = correspondence;
    }

    public int OccurrenceId { get; }

    public Analysis.MemberCorrespondenceEvidence.UnresolvedBinding
        Correspondence { get; }
}

/// <summary>
/// Projects one exact implementation MethodDef through an existing
/// package-role context.
/// </summary>
public static class PackageRoleMemberCallGraphQuery
{
    public static InspectionQuery<PackageRoleMemberCallGraphOutcome>
        Definition
    { get; } =
        new(
            "Package-role external-focused member call graph",
            InspectionCost.Unbounded);

    public static PackageRoleMemberCallGraphOutcome Execute(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request) =>
        ExecuteCore(
            projection,
            focus,
            request,
            PackageSupplyChainBaselinePolicy.CreateNothing(
                [focus.Package.PackageId]),
            CancellationToken.None);

    public static PackageRoleMemberCallGraphOutcome Execute(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        PackageSupplyChainBaselinePolicy baseline) =>
        ExecuteCore(
            projection,
            focus,
            request,
            baseline,
            CancellationToken.None);

    internal static PackageRoleMemberCallGraphOutcome
        ExecuteWithCancellation(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        PackageSupplyChainBaselinePolicy baseline,
        CancellationToken cancellationToken) =>
        ExecuteCore(
            projection,
            focus,
            request,
            baseline,
            cancellationToken);

    public static PackageAssemblyReferenceBindingEvidence
        CreateContinuationEvidence(
        PackageAssemblyContextProjection projection,
        InspectionGraphDocument document,
        PackageAssemblyReferenceCallOccurrenceEvidence predecessor)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(predecessor);

        CallGraphCallSiteEvidence[] callSites =
        [
            .. document.Occurrences
                .Select(static occurrence => occurrence.Evidence)
                .OfType<CallGraphCallSiteEvidence>()
                .Where(candidate =>
                    SamePhysicalOccurrence(
                        candidate,
                        predecessor.CallSite)),
        ];
        if (callSites.Length != 1
            || callSites[0].Identity.SourceRegistration
                is not { } source)
        {
            throw new InvalidOperationException(
                "The successor graph does not retain one exact physical call occurrence.");
        }

        PackageAssemblyContextRoleProjection role =
            projection.ImplementationRole
            ?? projection.SurfaceRole;
        PackageAssemblyRoleParticipant[] origins =
        [
            .. role.Participants.Where(
                participant => ReferenceEquals(
                    participant.Participant.Assembly.Registration,
                    source)),
        ];
        if (origins.Length != 1)
        {
            throw new InvalidOperationException(
                "The successor call occurrence does not identify one exact package participant.");
        }

        PackageAssemblyRoleParticipant origin = origins[0];
        var request = new AssemblyBindingRequest(
            predecessor.Request.Target,
            AssemblyBindingOrigin.FromAssembly(
                origin.Participant.Assembly),
            predecessor.Request.Scope);
        return new(
            callSites[0],
            origin,
            request,
            origin.Participant.BindingPolicy.Select(request));
    }

    private static PackageRoleMemberCallGraphOutcome ExecuteCore(
        PackageAssemblyContextProjection projection,
        PackageRoleMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest request,
        PackageSupplyChainBaselinePolicy baseline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(baseline);
        if (!baseline.RootPackageIds.Contains(
                focus.Package.PackageId,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The package supply-chain baseline does not contain the focused root Package ID.",
                nameof(baseline));
        }
        cancellationToken.ThrowIfCancellationRequested();

        PackageAssemblyContextRoleProjection role =
            projection.ImplementationRole
            ?? projection.SurfaceRole;
        ImmutableArray<PackageAssemblyRoleParticipant> participants =
            role.Participants;
        ImmutableArray<PackageAssemblyContextPlatformParticipant>
            platformParticipants = role.PlatformParticipants;
        PackageIntrinsicCoreLibraryIneligibilityReceipt?
            intrinsicCoreLibraryIneligibility =
                role.IntrinsicCoreLibraryIneligibility;

        return role.Use<PackageRoleMemberCallGraphOutcome>(group =>
        {
            var candidates =
                ImmutableArray.CreateBuilder<
                    PackageAssemblyRoleParticipant>();
            foreach (PackageAssemblyRoleParticipant participant
                in participants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(
                        participant.Package,
                        focus.Package))
                {
                    continue;
                }

                AssemblyImageAccessResult<Guid> module =
                    group.UseAssemblySession(
                        participant.Participant.Assembly,
                        static session => session.ModuleVersionId());
                if (module is AssemblyImageAccessResult<Guid>.Available
                        available
                    && available.Value == focus.ModuleVersionId)
                {
                    candidates.Add(participant);
                }
            }
            if (candidates.Count == 0)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusParticipantUnavailable,
                    "The exact root package implementation module is not present in the package-role context.");
            }
            if (candidates.Count != 1)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusParticipantAmbiguous,
                    "The exact root package implementation module identifies more than one package-role participant.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            PackageAssemblyRoleParticipant selected = candidates[0];
            bool? hasBody;
            try
            {
                using var metadata =
                    PdbContext.OpenMetadataOnly(
                        selected.Participant.Assembly);
                hasBody = metadata.MethodHasBody(focus.MethodToken);
            }
            catch (BadImageFormatException)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusMethodInvalid,
                    "The selected implementation MethodDef could not be decoded.");
            }

            if (hasBody is null)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusMethodInvalid,
                    "The selected implementation MethodDef is not valid in the exact implementation module.");
            }
            if (hasBody is false)
            {
                return Unavailable(
                    PackageRoleMemberCallGraphUnavailableReason
                        .FocusMethodBodyUnavailable,
                    "The selected implementation MethodDef has no managed body.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var session = new MemberCallGraphSession(
                group,
                selected.Participant.Assembly,
                focus.MethodToken);
            var nodePackages =
                new Dictionary<
                    Analysis.GraphNodeIdentity,
                    PackageRootIdentity>();
            var nodePlatforms =
                new Dictionary<
                    Analysis.GraphNodeIdentity,
                    PackageAssemblyContextPlatformParticipant>();
            InspectionGraphDocument document =
                session.CrossLibraryCalleeNeighborhoodWithCancellation(
                    request,
                    node =>
                    {
                        GraphScopeMembership membership =
                            ClassifyPackageMembership(
                                participants,
                                platformParticipants,
                                node,
                                baseline,
                                out PackageRootIdentity? package,
                                out PackageAssemblyContextPlatformParticipant?
                                    platform);
                        if (package is not null)
                            nodePackages.Add(node.Identity, package);
                        if (platform is not null)
                            nodePlatforms.Add(node.Identity, platform);
                        return membership;
                    },
                    cancellationToken);
            return new PackageRoleMemberCallGraphOutcome.Available(
                document,
                NodePackages(document, nodePackages),
                NodePlatforms(document, nodePlatforms),
                intrinsicCoreLibraryIneligibility,
                IntrinsicCoreLibraryOccurrences(
                    document,
                    intrinsicCoreLibraryIneligibility),
                AssemblyReferenceOccurrences(
                    document,
                    participants));
        });
    }

    static ImmutableArray<PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
        IntrinsicCoreLibraryOccurrences(
        InspectionGraphDocument document,
        PackageIntrinsicCoreLibraryIneligibilityReceipt? context)
    {
        if (context is null)
            return [];

        var result =
            ImmutableArray.CreateBuilder<
                PackageIntrinsicCoreLibraryCallOccurrenceEvidence>();
        var seen =
            new HashSet<(int OccurrenceId, MetadataTypeDefinitionName Type)>();
        foreach (InspectionGraphOccurrence occurrence
            in document.Occurrences)
        {
            if (occurrence.Evidence
                    is not CallGraphCallSiteEvidence callSite)
            {
                continue;
            }

            AssemblyAcquisitionRegistration? source =
                callSite.Identity.SourceRegistration;
            if (source is null)
                continue;

            PackageIntrinsicCoreLibraryParticipantEvidence[] evidenceOrigins =
                [
                    .. context.Participants.Where(
                        participant =>
                            ReferenceEquals(
                                participant.Registration,
                                source)),
                ];
            if (evidenceOrigins.Length != 1)
            {
                throw new InvalidOperationException(
                    "A physical intrinsic CoreLib call occurrence does not identify exactly one participant in its package-role context.");
            }

            if (callSite.TargetEvidence?.Correspondence
                    is not Analysis.CatalogMemberJoinProjection.Issued issued)
            {
                continue;
            }

            foreach (Analysis.MemberCorrespondenceEvidence.UnresolvedBinding
                correspondence in issued.Evidence.OfType<
                    Analysis.MemberCorrespondenceEvidence
                        .UnresolvedBinding>())
            {
                if (correspondence.Outcome
                        is not TypeResolutionOutcome.Unavailable
                        {
                            Target:
                                AssemblyBindingTarget
                                    .IntrinsicCoreLibrary,
                            Origin:
                                AssemblyBindingOrigin
                                    .RequestingAssembly origin,
                        })
                {
                    continue;
                }
                if (!ReferenceEquals(origin.Registration, source))
                {
                    throw new InvalidOperationException(
                        "An intrinsic CoreLib request does not retain its physical call occurrence origin.");
                }
                if (!seen.Add(
                        (occurrence.Id, correspondence.Type)))
                {
                    continue;
                }

                result.Add(
                    new PackageIntrinsicCoreLibraryCallOccurrenceEvidence(
                        occurrence.Id,
                        callSite,
                        correspondence,
                        evidenceOrigins[0],
                        context));
            }
        }

        return result.ToImmutable();
    }

    static ImmutableArray<PackageAssemblyReferenceCallOccurrenceEvidence>
        AssemblyReferenceOccurrences(
        InspectionGraphDocument document,
        ImmutableArray<PackageAssemblyRoleParticipant> participants)
    {
        var result =
            ImmutableArray.CreateBuilder<
                PackageAssemblyReferenceCallOccurrenceEvidence>();
        foreach (InspectionGraphOccurrence occurrence
            in document.Occurrences)
        {
            if (occurrence.Evidence
                    is not CallGraphCallSiteEvidence callSite
                || callSite.Identity.SourceRegistration
                    is not { } source
                || callSite.TargetEvidence?.Correspondence
                    is not Analysis.CatalogMemberJoinProjection.Issued
                        issued)
            {
                continue;
            }

            PackageAssemblyRoleParticipant[] origins =
            [
                .. participants.Where(
                    participant =>
                        ReferenceEquals(
                            participant.Participant.Assembly.Registration,
                            source)),
            ];
            if (origins.Length != 1)
                continue;

            PackageAssemblyRoleParticipant originParticipant =
                origins[0];
            foreach (Analysis.MemberCorrespondenceEvidence.UnresolvedBinding
                correspondence in issued.Evidence.OfType<
                    Analysis.MemberCorrespondenceEvidence
                        .UnresolvedBinding>())
            {
                AssemblyBindingRequest? request =
                    AssemblyReferenceRequest(correspondence.Outcome);
                if (request is null)
                    continue;
                if (request.Origin
                    is not AssemblyBindingOrigin.RequestingAssembly origin)
                    throw new InvalidOperationException(
                        "An AssemblyRef continuation request must retain its requesting assembly.");
                if (!ReferenceEquals(origin.Registration, source))
                {
                    throw new InvalidOperationException(
                        "An AssemblyRef request does not retain its physical call occurrence origin.");
                }

                AssemblyBindingSelectionSnapshot context =
                    originParticipant.Participant.BindingPolicy.Select(
                        request);
                result.Add(
                    new PackageAssemblyReferenceCallOccurrenceEvidence(
                        occurrence.Id,
                        callSite,
                        correspondence,
                        originParticipant,
                        request,
                        context));
            }
        }

        return result.ToImmutable();
    }

    static AssemblyBindingRequest? AssemblyReferenceRequest(
        TypeResolutionOutcome outcome) =>
        outcome switch
        {
            TypeResolutionOutcome.Unavailable
            {
                Target:
                    AssemblyBindingTarget.AssemblyReference target,
                Origin: var origin,
                Scope: var scope,
            } => new(target, origin, scope),
            TypeResolutionOutcome.UnboundBinding
            {
                Target:
                    AssemblyBindingTarget.AssemblyReference target,
                Origin: var origin,
                Scope: var scope,
            } => new(target, origin, scope),
            _ => null,
        };

    static bool SamePhysicalOccurrence(
        CallGraphCallSiteEvidence candidate,
        CallGraphCallSiteEvidence expected) =>
        candidate.CallerModuleVersionId
            == expected.CallerModuleVersionId
        && candidate.CallerMethodToken
            == expected.CallerMethodToken
        && candidate.ILOffset == expected.ILOffset
        && candidate.OperandToken == expected.OperandToken;

    static ImmutableArray<PackageRoleMemberCallGraphNodePackage>
        NodePackages(
            InspectionGraphDocument document,
            IReadOnlyDictionary<
                Analysis.GraphNodeIdentity,
                PackageRootIdentity> packages)
    {
        var result =
            ImmutableArray.CreateBuilder<
                PackageRoleMemberCallGraphNodePackage>();
        foreach (InspectionGraphNode node in document.Nodes)
        {
            Analysis.GraphNodeIdentity identity = node.Subject
                is InspectionGraphSubject.MemberSubject
            {
                Identity:
                        InspectionGraphMemberIdentity.CallGraph callGraph,
            }
                    ? callGraph.Identity
                    : throw new InvalidOperationException(
                        "A package-role member call graph contained a non-member node.");
            if (packages.TryGetValue(
                    identity,
                    out PackageRootIdentity? package))
            {
                result.Add(
                    new PackageRoleMemberCallGraphNodePackage(
                        node.Id,
                        package));
            }
        }

        return result.ToImmutable();
    }

    static ImmutableArray<PackageRoleMemberCallGraphNodePlatform>
        NodePlatforms(
        InspectionGraphDocument document,
        IReadOnlyDictionary<
            Analysis.GraphNodeIdentity,
            PackageAssemblyContextPlatformParticipant> platforms)
    {
        var result =
            ImmutableArray.CreateBuilder<
                PackageRoleMemberCallGraphNodePlatform>();
        foreach (InspectionGraphNode node in document.Nodes)
        {
            Analysis.GraphNodeIdentity identity = node.Subject
                is InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CallGraph callGraph,
                }
                    ? callGraph.Identity
                    : throw new InvalidOperationException(
                        "A package-role member call graph contained a non-member node.");
            if (platforms.TryGetValue(
                    identity,
                    out PackageAssemblyContextPlatformParticipant?
                        platform))
            {
                result.Add(
                    new(
                        node.Id,
                        platform.Target,
                        platform.Participant.Assembly.Identity));
            }
        }

        return result.ToImmutable();
    }

    internal static GraphScopeMembership
        ClassifyPackageMembership(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        CallGraphNode node,
        PackageSupplyChainBaselinePolicy baseline,
        out PackageRootIdentity? package) =>
        ClassifyPackageMembership(
            participants,
            [],
            node,
            baseline,
            out package,
            out _);

    internal static GraphScopeMembership
        ClassifyPackageMembership(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        ImmutableArray<PackageAssemblyContextPlatformParticipant>
            platformParticipants,
        CallGraphNode node,
        PackageSupplyChainBaselinePolicy baseline,
        out PackageRootIdentity? package,
        out PackageAssemblyContextPlatformParticipant? platform)
    {
        AssemblyReferenceIdentity? identity =
            node.DefinitionAssemblyIdentity;
        if (identity is null)
        {
            AssemblyReferenceIdentity? reference =
                ReferencedAssemblyIdentity(node.Member);
            AssemblyReferenceIdentity? resolution =
                node.ResolutionAssemblyIdentity;
            if (reference is null
                || resolution is null
                || !reference.IsEquivalentTo(resolution))
            {
                package = null;
                platform = null;
                return GraphScopeMembership.Unknown;
            }
            identity = reference;
        }

        (package, bool ambiguous) =
            MatchPackage(
                participants,
                identity);
        if (package is null || ambiguous)
        {
            package = null;
            platform = MatchPlatform(platformParticipants, identity);
            return platform is not null
                ? baseline.Kind
                    is PackageSupplyChainBaseline
                        .SelfAndRegisteredEcosystems
                    ? GraphScopeMembership.Inside
                    : GraphScopeMembership.Outside
                : GraphScopeMembership.Unknown;
        }

        platform = null;
        return baseline.Classify(package.PackageId)
                is PackageSupplyChainClassification.Baseline
            ? GraphScopeMembership.Inside
            : GraphScopeMembership.Outside;
    }

    static PackageAssemblyContextPlatformParticipant? MatchPlatform(
        ImmutableArray<PackageAssemblyContextPlatformParticipant>
            participants,
        AssemblyReferenceIdentity definitionIdentity)
    {
        PackageAssemblyContextPlatformParticipant? match = null;
        foreach (PackageAssemblyContextPlatformParticipant participant
            in participants)
        {
            if (!participant.Participant.Assembly.Identity
                    .IsEquivalentTo(definitionIdentity))
            {
                continue;
            }
            if (match is not null
                && (!ReferenceEquals(match.Library, participant.Library)
                    || match.Target != participant.Target))
            {
                return null;
            }
            match = participant;
        }
        return match;
    }

    internal static (PackageRootIdentity? Package, bool Ambiguous)
        MatchPackage(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        AssemblyReferenceIdentity? definitionIdentity)
    {
        if (definitionIdentity is null)
            return (null, false);

        PackageRootIdentity? package = null;
        foreach (PackageAssemblyRoleParticipant participant
            in participants)
        {
            if (!participant.Participant.Assembly.Identity
                    .IsEquivalentTo(definitionIdentity))
            {
                continue;
            }
            if (package is null)
            {
                package = participant.Package;
            }
            else if (!ReferenceEquals(package, participant.Package))
            {
                return (null, true);
            }
        }

        return (package, false);
    }

    static AssemblyReferenceIdentity? ReferencedAssemblyIdentity(
        Analysis.MemberRef member)
    {
        Analysis.TypeRef type = member.DeclaringType;
        while (type.Kind == Analysis.TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }
        if (type.Kind != Analysis.TypeRefKind.Definition)
            return null;

        return type.Resolution?.Origin switch
        {
            Analysis.TypeReferenceOrigin.AssemblyReference reference =>
                reference.Assembly,
            Analysis.TypeReferenceOrigin.CurrentAssembly current =>
                current.Assembly,
            _ => null,
        };
    }

    private static PackageRoleMemberCallGraphOutcome.Unavailable Unavailable(
        PackageRoleMemberCallGraphUnavailableReason reason,
        string detail) =>
        new(new(reason, detail));
}
