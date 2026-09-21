using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Sections;

public sealed record SelectedContextExactPackageInspectionRequest
{
    public SelectedContextExactPackageInspectionRequest(
        string packageId,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        string? normalizedVersion = null;
        if (version is not null
            && !PackageExtractor.TryNormalizePackageVersion(
                version,
                out normalizedVersion))
        {
            throw new ArgumentException(
                "Exact Package inspection requires a valid exact version.",
                nameof(version));
        }

        PackageId = packageId;
        Version = normalizedVersion;
    }

    public string PackageId { get; }

    public string? Version { get; }
}

public enum SelectedContextExactPackageOutcome
{
    Available,
    MissingSelectedContext,
    ContextUnavailable,
    NotFound,
    VersionMismatch,
    CorrespondenceFailure,
}

public sealed record SelectedContextPackageRoutingCandidate(
    string PackageId,
    string? DeclaredVersion,
    string EffectiveVersion,
    int ContextOrder,
    int MemberOrder,
    string? TargetFramework,
    string? RuntimeIdentifier,
    PackageCompileAssetSelectionStatus AssetSelectionStatus);

public sealed record SelectedContextPackageRoutingEvidence(
    int ConsideredOccurrenceCount,
    ImmutableArray<SelectedContextPackageRoutingCandidate> ExactCandidates,
    SelectedContextPackageRoutingCandidate? Selected);

public sealed record SelectedContextExactPackageFailure(
    SelectedContextExactPackageOutcome Outcome,
    string Message,
    SelectedContextPackageRoutingEvidence Evidence);

public abstract record SelectedContextExactPackageOperationResult<TContent>
{
    private protected SelectedContextExactPackageOperationResult()
    {
    }

    public sealed record Completed(
        InspectionEnvelope<TContent> Envelope,
        SelectedContextPackageRoutingEvidence Evidence)
        : SelectedContextExactPackageOperationResult<TContent>;

    public sealed record Failed(SelectedContextExactPackageFailure Failure)
        : SelectedContextExactPackageOperationResult<TContent>;
}

public abstract record
    SelectedContextExactPackageEvidenceOperationResult<TContent>
{
    private protected
        SelectedContextExactPackageEvidenceOperationResult()
    {
    }

    public sealed record Completed(
        EvidenceInspectionEnvelope<
            TContent,
            SelectedContextPackageRoutingEvidence> Envelope)
        : SelectedContextExactPackageEvidenceOperationResult<TContent>;

    public sealed record Failed(SelectedContextExactPackageFailure Failure)
        : SelectedContextExactPackageEvidenceOperationResult<TContent>;
}

/// <summary>
/// Operation-bounded companion for one selected Workspace Package Root.
/// </summary>
public sealed class SelectedContextExactPackageLiveTarget
{
    internal SelectedContextExactPackageLiveTarget(
        PackageRootBinding root,
        PackageRootOccurrenceBinding occurrence,
        WorkspaceMemberCoordinate.PackageMember declaredMember,
        string? contextTargetFramework)
    {
        Root = root;
        Occurrence = occurrence;
        DeclaredMember = declaredMember;
        ContextTargetFramework = contextTargetFramework;
    }

    public PackageRootBinding Root { get; }

    public PackageRootOccurrenceBinding Occurrence { get; }

    public WorkspaceMemberCoordinate.PackageMember DeclaredMember { get; }

    public string? ContextTargetFramework { get; }

    public TResult UseContent<TResult>(
        Func<IPackageContent, TResult> operation) =>
        Root.Root.UseContent(operation);
}

/// <summary>
/// Selects and inspects one exact direct Package occurrence from the restored
/// scenario's selected context.
/// </summary>
public static class SelectedContextExactPackageInspectionOperation
{
    public static SelectedContextExactPackageOperationResult<TContent>
        Execute<TContent>(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactPackageInspectionRequest request,
            InspectionContentKind contentKind,
            Func<SelectedContextExactPackageLiveTarget, TContent> inspect,
            ViewFacetId? facet = null,
            InspectionPortableProjection.NonProjectable? shareRefusal = null)
    {
        CoreResult<TContent> result = ExecuteCoreAsync(
            authority,
            activation,
            request,
            contentKind,
            target => new ValueTask<TContent>(inspect(target)),
            facet,
            shareRefusal).GetAwaiter().GetResult();
        return result.Failure is { } failure
            ? new SelectedContextExactPackageOperationResult<TContent>
                .Failed(failure)
            : new SelectedContextExactPackageOperationResult<TContent>
                .Completed(result.Envelope!, result.Evidence);
    }

    public static
        SelectedContextExactPackageEvidenceOperationResult<TContent>
        ExecuteWithEvidence<TContent>(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactPackageInspectionRequest request,
            InspectionContentKind contentKind,
            Func<SelectedContextExactPackageLiveTarget, TContent> inspect,
            ViewFacetId? facet = null,
            InspectionPortableProjection.NonProjectable? shareRefusal = null)
    {
        CoreResult<TContent> result = ExecuteCoreAsync(
            authority,
            activation,
            request,
            contentKind,
            target => new ValueTask<TContent>(inspect(target)),
            facet,
            shareRefusal).GetAwaiter().GetResult();
        return result.Failure is { } failure
            ? new SelectedContextExactPackageEvidenceOperationResult<TContent>
                .Failed(failure)
            : new SelectedContextExactPackageEvidenceOperationResult<TContent>
                .Completed(
                    new(
                        result.Envelope!,
                        result.Evidence));
    }

    public static async ValueTask<
        SelectedContextExactPackageOperationResult<TContent>>
        ExecuteAsync<TContent>(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactPackageInspectionRequest request,
            InspectionContentKind contentKind,
            Func<
                SelectedContextExactPackageLiveTarget,
                ValueTask<TContent>> inspect,
            ViewFacetId? facet = null,
            InspectionPortableProjection.NonProjectable? shareRefusal = null)
    {
        CoreResult<TContent> result = await ExecuteCoreAsync(
            authority,
            activation,
            request,
            contentKind,
            inspect,
            facet,
            shareRefusal).ConfigureAwait(false);
        return result.Failure is { } failure
            ? new SelectedContextExactPackageOperationResult<TContent>
                .Failed(failure)
            : new SelectedContextExactPackageOperationResult<TContent>
                .Completed(result.Envelope!, result.Evidence);
    }

    public static async ValueTask<
        SelectedContextExactPackageEvidenceOperationResult<TContent>>
        ExecuteWithEvidenceAsync<TContent>(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactPackageInspectionRequest request,
            InspectionContentKind contentKind,
            Func<
                SelectedContextExactPackageLiveTarget,
                ValueTask<TContent>> inspect,
            ViewFacetId? facet = null,
            InspectionPortableProjection.NonProjectable? shareRefusal = null)
    {
        CoreResult<TContent> result = await ExecuteCoreAsync(
            authority,
            activation,
            request,
            contentKind,
            inspect,
            facet,
            shareRefusal).ConfigureAwait(false);
        return result.Failure is { } failure
            ? new SelectedContextExactPackageEvidenceOperationResult<TContent>
                .Failed(failure)
            : new SelectedContextExactPackageEvidenceOperationResult<TContent>
                .Completed(new(result.Envelope!, result.Evidence));
    }

    public static async ValueTask<
        SelectedContextExactPackageEvidenceOperationResult<TContent>>
        ExecuteWithEvidenceAsync<TContent>(
            InspectionWorkspace workspace,
            CompleteWorkspaceActivation activation,
            SelectedContextExactPackageInspectionRequest request,
            InspectionContentKind contentKind,
            Func<
                SelectedContextExactPackageLiveTarget,
                ValueTask<TContent>> inspect,
            ViewFacetId? facet = null,
            InspectionPortableProjection.NonProjectable? shareRefusal = null)
    {
        CoreResult<TContent> result = await ExecuteCoreAsync(
            workspace,
            activation,
            request,
            contentKind,
            inspect,
            facet,
            shareRefusal).ConfigureAwait(false);
        return result.Failure is { } failure
            ? new SelectedContextExactPackageEvidenceOperationResult<TContent>
                .Failed(failure)
            : new SelectedContextExactPackageEvidenceOperationResult<TContent>
                .Completed(new(result.Envelope!, result.Evidence));
    }

    static async ValueTask<CoreResult<TContent>> ExecuteCoreAsync<TContent>(
        WorkspaceRealizationOperationLease authority,
        CompleteWorkspaceActivation activation,
        SelectedContextExactPackageInspectionRequest request,
        InspectionContentKind contentKind,
        Func<
            SelectedContextExactPackageLiveTarget,
            ValueTask<TContent>> inspect,
        ViewFacetId? facet,
        InspectionPortableProjection.NonProjectable? shareRefusal)
    {
        ArgumentNullException.ThrowIfNull(authority);
        using WorkspaceRealizationOperationUse operation =
            authority.EnterUse();
        return await ExecuteCoreAsync(
            operation.Workspace,
            activation,
            request,
            contentKind,
            inspect,
            facet,
            shareRefusal).ConfigureAwait(false);
    }

    static async ValueTask<CoreResult<TContent>> ExecuteCoreAsync<TContent>(
        InspectionWorkspace workspace,
        CompleteWorkspaceActivation activation,
        SelectedContextExactPackageInspectionRequest request,
        InspectionContentKind contentKind,
        Func<
            SelectedContextExactPackageLiveTarget,
            ValueTask<TContent>> inspect,
        ViewFacetId? facet,
        InspectionPortableProjection.NonProjectable? shareRefusal)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(inspect);

        if (activation.SelectedContext is not { } context)
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.MissingSelectedContext,
                "The restored Workspace has no selected context.",
                considered: 0,
                []);
        }
        if (context.ContextLoadOutcome
            is not WorkspaceContextLoadOutcome.Loaded loaded)
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.ContextUnavailable,
                "The selected Workspace context is not available.",
                considered: 0,
                []);
        }
        if (!ReferenceEquals(workspace.Identity, activation.Workspace)
            || !ReferenceEquals(loaded.Workspace, activation.Workspace))
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.CorrespondenceFailure,
                "The selected context and Package Roots do not belong to "
                    + "the active Workspace realization.",
                loaded.PackageRoots.Length,
                []);
        }
        if (context.Receipt.Request
            is not WorkspaceDeclarationRequest.ContextLoad declaration)
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.CorrespondenceFailure,
                "The selected context does not retain its direct Package "
                    + "declaration order.",
                loaded.PackageRoots.Length,
                []);
        }

        var candidates =
            ImmutableArray.CreateBuilder<ResolvedCandidate>();
        foreach (PackageRootBinding root in loaded.PackageRoots)
        {
            (
                WorkspaceMemberCoordinate.PackageMember Member,
                int Order)[] members =
            [
                .. declaration.Input.Members
                    .Select((member, index) => (member, index))
                    .Where(item =>
                        item.member
                            is WorkspaceMemberCoordinate.PackageMember package
                        && string.Equals(
                            package.PackageId,
                            root.Root.PackageId,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(item =>
                        (
                            Member:
                                (WorkspaceMemberCoordinate.PackageMember)
                                    item.member,
                            Order: item.index)),
            ];
            if (members.Length != 1)
            {
                return Failed<TContent>(
                    SelectedContextExactPackageOutcome
                        .CorrespondenceFailure,
                    "A Package Root did not correspond to exactly one "
                        + "direct Package declaration.",
                    loaded.PackageRoots.Length,
                    []);
            }

            WorkspaceMemberCoordinate.PackageMember member =
                members[0].Member;
            int memberOrder = members[0].Order;
            candidates.Add(
                new(
                    root,
                    member,
                    new SelectedContextPackageRoutingCandidate(
                        root.Root.PackageId,
                        member.Version,
                        root.Root.PackageVersion,
                        context.Receipt.Order,
                        memberOrder,
                        declaration.Input.Framework
                            ?? root.Root.RequestedTargetFramework
                            ?? root.Root.AssetSelection.TargetFramework,
                        root.Root.RequestedRuntimeIdentifier,
                        root.Root.AssetSelection.Status)));
        }

        ImmutableArray<ResolvedCandidate> idMatches =
        [
            .. candidates.Where(candidate =>
                string.Equals(
                    candidate.Root.Root.PackageId,
                    request.PackageId,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        ImmutableArray<SelectedContextPackageRoutingCandidate> evidenceMatches =
            [.. idMatches.Select(static candidate => candidate.Evidence)];
        if (idMatches.Length == 0)
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.NotFound,
                $"Package '{request.PackageId}' is not a direct occurrence "
                    + "in the selected Workspace context.",
                loaded.PackageRoots.Length,
                evidenceMatches);
        }

        ImmutableArray<ResolvedCandidate> exactMatches =
            request.Version is null
                ? idMatches
                :
                [
                    .. idMatches.Where(candidate =>
                        VersionsEqual(
                            candidate.Root.Root.PackageVersion,
                            request.Version)),
                ];
        if (exactMatches.Length == 0)
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.VersionMismatch,
                $"Package '{request.PackageId}' is present in the selected "
                    + $"Workspace context, but not at version "
                    + $"'{request.Version}'.",
                loaded.PackageRoots.Length,
                evidenceMatches);
        }
        if (exactMatches.Length != 1)
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.CorrespondenceFailure,
                "Package selection produced more than one direct occurrence "
                    + "for one canonical Package ID.",
                loaded.PackageRoots.Length,
                evidenceMatches);
        }

        ResolvedCandidate selected = exactMatches[0];
        InspectionWorkspacePackageOccurrenceView occurrenceView =
            workspace.CreatePackageOccurrenceView(
                [selected.Root]);
        InspectionWorkspacePackageOccurrenceActivation occurrenceActivation =
            occurrenceView.Activate(
                occurrenceView.Occurrences[0].Action);
        if (occurrenceActivation
            is not InspectionWorkspacePackageOccurrenceActivation.Activated
                activatedOccurrence)
        {
            return Failed<TContent>(
                SelectedContextExactPackageOutcome.CorrespondenceFailure,
                "The selected Package occurrence is no longer active in "
                    + "the restored Workspace.",
                loaded.PackageRoots.Length,
                evidenceMatches);
        }

        var target = new SelectedContextExactPackageLiveTarget(
            selected.Root,
            activatedOccurrence.Occurrence,
            selected.Member,
            declaration.Input.Framework);
        TContent content = await inspect(target).ConfigureAwait(false);

        InspectionPortableProjection share =
            shareRefusal
            ?? ProjectShare(
                activation,
                context,
                request,
                selected,
                facet);
        var envelope = new InspectionEnvelope<TContent>(
            contentKind,
            content,
            share);
        var evidence = new SelectedContextPackageRoutingEvidence(
            loaded.PackageRoots.Length,
            evidenceMatches,
            selected.Evidence);
        return new(envelope, evidence, Failure: null);
    }

    static InspectionPortableProjection ProjectShare(
        CompleteWorkspaceActivation activation,
        WorkspaceDeclarationContext context,
        SelectedContextExactPackageInspectionRequest request,
        ResolvedCandidate selected,
        ViewFacetId? facet)
    {
        if (request.Version is not null
            && selected.Member.Version is null)
        {
            return new InspectionPortableProjection.NonProjectable(
                "workspace.package.version",
                InspectionPortableProjectionFailureReason.NotSupported);
        }

        WorkspaceSharePacketProjectionResult projection =
            WorkspacePackageScenarioProjection.Project(
                activation,
                context,
                selected.Member,
                selected.Root,
                facet);
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure
                ?? throw new InvalidOperationException(
                    "A failed Package scenario projection requires a failure.");
            return new InspectionPortableProjection.NonProjectable(
                failure.Path,
                failure.Kind
                    is WorkspaceSharePacketProjectionFailureKind
                        .InvalidDefinitionSet
                    ? InspectionPortableProjectionFailureReason.Invalid
                    : InspectionPortableProjectionFailureReason.NotSupported,
                failure.Message);
        }

        WorkspaceSharePacket packet =
            projection.Packet
            ?? throw new InvalidOperationException(
                "A successful Package scenario projection requires a packet.");
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        return new InspectionPortableProjection.Available(
            "https://dotnet-inspect.net/?w=" + encoded,
            encoded);
    }

    static CoreResult<TContent> Failed<TContent>(
        SelectedContextExactPackageOutcome outcome,
        string message,
        int considered,
        ImmutableArray<SelectedContextPackageRoutingCandidate> candidates)
    {
        var evidence = new SelectedContextPackageRoutingEvidence(
            considered,
            candidates,
            Selected: null);
        return new(
            Envelope: null,
            evidence,
            new SelectedContextExactPackageFailure(
                outcome,
                message,
                evidence));
    }

    static bool VersionsEqual(string left, string right) =>
        PackageExtractor.TryNormalizePackageVersion(
            left,
            out string leftVersion)
        && PackageExtractor.TryNormalizePackageVersion(
            right,
            out string rightVersion)
        && string.Equals(
            leftVersion,
            rightVersion,
            StringComparison.OrdinalIgnoreCase);

    sealed record ResolvedCandidate(
        PackageRootBinding Root,
        WorkspaceMemberCoordinate.PackageMember Member,
        SelectedContextPackageRoutingCandidate Evidence);

    sealed record CoreResult<TContent>(
        InspectionEnvelope<TContent>? Envelope,
        SelectedContextPackageRoutingEvidence Evidence,
        SelectedContextExactPackageFailure? Failure);
}
