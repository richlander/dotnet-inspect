using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal abstract record BrowserFrameworkDeclarationDestination
{
    private protected BrowserFrameworkDeclarationDestination()
    {
    }

    internal abstract WorkspaceDeclarationMember Observation { get; }

    internal sealed record Library : BrowserFrameworkDeclarationDestination
    {
        internal Library(WorkspaceDeclarationMember observation)
        {
            ArgumentNullException.ThrowIfNull(observation);
            Observation = observation;
        }

        internal override WorkspaceDeclarationMember Observation { get; }
    }

    internal sealed record Type : BrowserFrameworkDeclarationDestination
    {
        internal Type(TypeDeclarationLocatorCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            Candidate = candidate;
        }

        internal TypeDeclarationLocatorCandidate Candidate { get; }

        internal override WorkspaceDeclarationMember Observation =>
            Candidate.Observation;
    }
}

internal enum BrowserFrameworkDeclarationUnavailableReason
{
    OwnerClosed,
    NoActiveRealization,
    CoordinatorClosed,
    RuntimeUnavailable,
    RegistrationUnavailable,
    ScopeUnavailable,
    ContextUnavailable,
    LibraryUnavailable,
    TypeUnavailable,
}

internal enum BrowserFrameworkDeclarationStaleReason
{
    Result,
    Realization,
    Workspace,
    RegistrationRevision,
    ScopeRevision,
    ScopePublicationBase,
    PackageOccurrence,
}

internal enum BrowserFrameworkDeclarationAmbiguousReason
{
    Context,
    Type,
}

internal enum BrowserFrameworkDeclarationRefusedReason
{
    ForeignAction,
    CandidateNotInResult,
    ObservationNotInPopulation,
    NonPlatformObservation,
    UnsupportedPlatformFamily,
    Forwarder,
    UnsupportedDeclarationKind,
    MalformedBinding,
}

internal enum BrowserFrameworkDeclarationFailureReason
{
    SurfaceUnavailable,
    SurfaceIncomplete,
}

internal abstract record BrowserFrameworkDeclarationActivationBlock
{
    private protected BrowserFrameworkDeclarationActivationBlock()
    {
    }

    internal sealed record Unavailable(
        BrowserFrameworkDeclarationUnavailableReason Reason) :
        BrowserFrameworkDeclarationActivationBlock;

    internal sealed record Stale(
        BrowserFrameworkDeclarationStaleReason Reason) :
        BrowserFrameworkDeclarationActivationBlock;

    internal sealed record Ambiguous(
        BrowserFrameworkDeclarationAmbiguousReason Reason) :
        BrowserFrameworkDeclarationActivationBlock;

    internal sealed record Refused(
        BrowserFrameworkDeclarationRefusedReason Reason) :
        BrowserFrameworkDeclarationActivationBlock;

    internal sealed record Failed(
        BrowserFrameworkDeclarationFailureReason Reason) :
        BrowserFrameworkDeclarationActivationBlock;

    internal sealed record Canceled :
        BrowserFrameworkDeclarationActivationBlock;
}

internal sealed class BrowserFrameworkDeclarationAction
{
    internal BrowserFrameworkDeclarationAction(
        object issuer,
        object publication,
        object token)
    {
        Issuer = issuer;
        Publication = publication;
        Token = token;
    }

    internal object Issuer { get; }

    internal object Publication { get; }

    internal object Token { get; }
}

internal sealed class BrowserFrameworkDeclarationResultAuthority
{
    internal BrowserFrameworkDeclarationResultAuthority(
        object issuer,
        object publication,
        long resultGeneration)
    {
        Issuer = issuer;
        Publication = publication;
        ResultGeneration = resultGeneration;
    }

    internal object Issuer { get; }

    internal object Publication { get; }

    internal long ResultGeneration { get; }
}

internal abstract record BrowserFrameworkDeclarationResultAdmission
{
    private protected BrowserFrameworkDeclarationResultAdmission()
    {
    }

    internal sealed record Admitted(
        BrowserFrameworkDeclarationResultAuthority Authority) :
        BrowserFrameworkDeclarationResultAdmission;

    internal sealed record Blocked(
        BrowserFrameworkDeclarationActivationBlock Block) :
        BrowserFrameworkDeclarationResultAdmission;
}

internal abstract record BrowserFrameworkDeclarationActionPublication(
    BrowserFrameworkDeclarationDestination Destination)
{
    internal sealed record Published(
        BrowserFrameworkDeclarationDestination Destination,
        BrowserFrameworkDeclarationAction Action) :
        BrowserFrameworkDeclarationActionPublication(Destination);

    internal sealed record Blocked(
        BrowserFrameworkDeclarationDestination Destination,
        BrowserFrameworkDeclarationActivationBlock Block) :
        BrowserFrameworkDeclarationActionPublication(Destination);
}

internal sealed record BrowserFrameworkDeclarationPublication(
    long ResultGeneration,
    ImmutableArray<BrowserFrameworkDeclarationActionPublication> Actions);

internal abstract record BrowserFrameworkDeclarationEffect
{
    private protected BrowserFrameworkDeclarationEffect()
    {
    }

    internal sealed record Library(
        BrowserPackageSurfaceInfo Surface,
        ExactLibrarySourceCoordinate.Platform Coordinate,
        WorkspaceDeclarationMember Observation) :
        BrowserFrameworkDeclarationEffect;

    internal sealed record Type(
        BrowserPackageSurfaceInfo Surface,
        BrowserTypeSurfaceInfo SelectedType,
        ExactLibrarySourceCoordinate.Platform Coordinate,
        WorkspaceDeclarationMember Observation,
        MetadataTypeDefinitionName Name) :
        BrowserFrameworkDeclarationEffect;
}

internal abstract record BrowserFrameworkDeclarationActivationResult(
    BrowserFrameworkDeclarationAction Action)
{
    internal sealed record Settled(
        BrowserFrameworkDeclarationAction Action,
        BrowserFrameworkDeclarationEffect Effect) :
        BrowserFrameworkDeclarationActivationResult(Action);

    internal sealed record Blocked(
        BrowserFrameworkDeclarationAction Action,
        BrowserFrameworkDeclarationActivationBlock Block) :
        BrowserFrameworkDeclarationActivationResult(Action);
}

/// <summary>
/// Issues current-result Browser actions for exact framework declaration
/// observations without exposing their live Metadata association.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserFrameworkDeclarationActivation : IDisposable
{
    readonly object _gate = new();
    readonly object _issuer = new();
    readonly BrowserWorkspaceRealizationHost _host;
    PublicationState? _current;
    long _latestResultGeneration = -1;
    bool _closed;

    internal BrowserFrameworkDeclarationActivation(
        BrowserWorkspaceRealizationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    internal BrowserFrameworkDeclarationResultAdmission BeginResult(
        long resultGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resultGeneration);
        lock (_gate)
        {
            if (_closed)
            {
                return new
                    BrowserFrameworkDeclarationResultAdmission.Blocked(
                        new BrowserFrameworkDeclarationActivationBlock
                            .Unavailable(
                                BrowserFrameworkDeclarationUnavailableReason
                                    .OwnerClosed));
            }
            if (resultGeneration <= _latestResultGeneration)
            {
                return new
                    BrowserFrameworkDeclarationResultAdmission.Blocked(
                        new BrowserFrameworkDeclarationActivationBlock.Stale(
                            BrowserFrameworkDeclarationStaleReason.Result));
            }

            _latestResultGeneration = resultGeneration;
            var state = new PublicationState(resultGeneration);
            _current = state;
            return new BrowserFrameworkDeclarationResultAdmission.Admitted(
                new BrowserFrameworkDeclarationResultAuthority(
                    _issuer,
                    state.Identity,
                    resultGeneration));
        }
    }

    internal async ValueTask<BrowserFrameworkDeclarationPublication>
        PublishAsync(
            BrowserFrameworkDeclarationResultAuthority resultAuthority,
            WorkspaceRealizationOperationLease operation,
            BrowserSpotlightActivationBasis basis,
            TypeDeclarationLocatorResult.Evaluated result,
            ImmutableArray<BrowserFrameworkDeclarationDestination>
                destinations,
            ImmutableArray<WorkspaceDeclarationContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(resultAuthority);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(result);
        if (destinations.IsDefault
            || destinations.Any(static destination => destination is null))
        {
            throw new ArgumentException(
                "Framework declaration destinations must be initialized.",
                nameof(destinations));
        }
        if (contexts.IsDefault
            || contexts.Any(static context => context is null))
        {
            throw new ArgumentException(
                "Framework declaration contexts must be initialized.",
                nameof(contexts));
        }

        if (resultAuthority.ResultGeneration != basis.ResultGeneration
            || !TryGetPublication(resultAuthority, out PublicationState? state))
        {
            return BlockedPublication(
                basis.ResultGeneration,
                destinations,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Result));
        }

        InspectionWorkspace workspace = operation.Workspace;
        if (!ReferenceEquals(operation.Realization, workspace.Identity)
            || !ReferenceEquals(
                result.Population.Workspace,
                workspace.Identity)
            || !ReferenceEquals(
                basis.Scope.Revision.Workspace,
                workspace.Identity)
            || !ReferenceEquals(
                basis.Registrations.Workspace,
                workspace.Identity))
        {
            return BlockedPublication(
                basis.ResultGeneration,
                destinations,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Workspace));
        }
        if (!ReferenceEquals(
                _host.Current?.Identity,
                operation.Realization))
        {
            return BlockedPublication(
                basis.ResultGeneration,
                destinations,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Realization));
        }

        BrowserSpotlightActivationBasisRead authority =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                workspace,
                basis).ConfigureAwait(false);
        if (authority.Block is { } authorityBlock)
        {
            return BlockedPublication(
                basis.ResultGeneration,
                destinations,
                FromAuthority(authorityBlock));
        }
        if (!IsCurrent(state)
            || !ReferenceEquals(
                _host.Current?.Identity,
                operation.Realization))
        {
            return BlockedPublication(
                basis.ResultGeneration,
                destinations,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    IsCurrent(state)
                        ? BrowserFrameworkDeclarationStaleReason.Realization
                        : BrowserFrameworkDeclarationStaleReason.Result));
        }

        var entries =
            ImmutableDictionary.CreateBuilder<object, ActionEntry>(
                ReferenceEqualityComparer.Instance);
        var publications =
            ImmutableArray.CreateBuilder<
                BrowserFrameworkDeclarationActionPublication>(
                    destinations.Length);
        foreach (BrowserFrameworkDeclarationDestination destination
            in destinations)
        {
            (ActionEntry? entry,
                BrowserFrameworkDeclarationActivationBlock? block) =
                Bind(
                    workspace,
                    operation.Realization,
                    basis,
                    result,
                    destination,
                    contexts);
            if (block is not null)
            {
                publications.Add(
                    new BrowserFrameworkDeclarationActionPublication.Blocked(
                        destination,
                        block));
                continue;
            }

            object token = new();
            entries.Add(token, entry!);
            publications.Add(
                new BrowserFrameworkDeclarationActionPublication.Published(
                    destination,
                    new BrowserFrameworkDeclarationAction(
                        _issuer,
                        state.Identity,
                        token)));
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_current, state))
            {
                return BlockedPublication(
                    basis.ResultGeneration,
                    destinations,
                    new BrowserFrameworkDeclarationActivationBlock.Stale(
                        BrowserFrameworkDeclarationStaleReason.Result));
            }
            state.Entries = entries.ToImmutable();
        }

        return new(
            basis.ResultGeneration,
            publications.ToImmutable());
    }

    internal async ValueTask<BrowserFrameworkDeclarationActivationResult>
        ActivateAsync(
            BrowserFrameworkDeclarationAction action,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (cancellationToken.IsCancellationRequested)
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Canceled());
        }

        if (!ReferenceEquals(action.Issuer, _issuer))
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Refused(
                    BrowserFrameworkDeclarationRefusedReason.ForeignAction));
        }
        if (!TryGetCurrentEntry(action, out ActionEntry? entry))
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Result));
        }

        WorkspaceRealizationOperationAdmission admission;
        try
        {
            admission = await _host.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Canceled());
        }
        if (admission
            is WorkspaceRealizationOperationAdmission.Unavailable unavailable)
        {
            return Blocked(
                action,
                FromAdmission(unavailable.Reason));
        }

        using WorkspaceRealizationOperationLease operation =
            ((WorkspaceRealizationOperationAdmission.Admitted)admission).Lease;
        InspectionWorkspace workspace = operation.Workspace;
        if (!ReferenceEquals(operation.Realization, entry.Realization)
            || !ReferenceEquals(workspace.Identity, entry.Realization))
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Realization));
        }

        BrowserSpotlightActivationBasisRead authority =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                workspace,
                entry.Basis).ConfigureAwait(false);
        if (authority.Block is { } authorityBlock)
            return Blocked(action, FromAuthority(authorityBlock));
        if (cancellationToken.IsCancellationRequested)
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Canceled());
        }
        if (!ReferenceEquals(_host.Current?.Identity, entry.Realization))
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Realization));
        }
        if (!TryGetCurrentEntry(action, out ActionEntry? current)
            || !ReferenceEquals(current, entry))
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Result));
        }

        if (!entry.Context.TryGetTarget(
                out WorkspaceDeclarationContext? context)
            || !entry.Group.TryGetTarget(
                out AssemblyContextGroup? group)
            || !entry.Participant.TryGetTarget(
                out AssemblyContextParticipant? participant)
            || !ReferenceEquals(context.Group, group)
            || workspace.CaptureDeclarationPopulation([context])
                is WorkspaceDeclarationPopulationCapture.Rejected)
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Unavailable(
                    BrowserFrameworkDeclarationUnavailableReason
                        .ContextUnavailable));
        }

        BrowserPlatformSurfaceProjectionResult projection;
        try
        {
            projection = BrowserPlatformSurfaceProjection.TryProject(
                group,
                participant,
                entry.Projection.Family,
                entry.Projection.Version,
                entry.Projection.Framework);
        }
        catch (ObjectDisposedException exception)
            when (exception.ObjectName
                == typeof(AssemblyContextGroup).FullName)
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Unavailable(
                    BrowserFrameworkDeclarationUnavailableReason
                        .ContextUnavailable));
        }
        if (projection
            is BrowserPlatformSurfaceProjectionResult.Unavailable)
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Failed(
                    BrowserFrameworkDeclarationFailureReason
                        .SurfaceUnavailable));
        }

        BrowserPlatformSurfaceProjectionValue projected =
            ((BrowserPlatformSurfaceProjectionResult.Projected)projection)
                .Projection;
        BrowserFrameworkDeclarationEffect effect;
        if (entry.Name is null)
        {
            effect = new BrowserFrameworkDeclarationEffect.Library(
                projected.Surface,
                entry.Library,
                entry.Observation);
        }
        else
        {
            BrowserTypeSurfaceInfo[] selected =
            [
                .. projected.Surface.Types.Where(type =>
                    string.Equals(
                        type.DefinitionId,
                        entry.Name.ToEscapedFullName(),
                        StringComparison.Ordinal)
                    && string.Equals(
                        type.AssemblyName,
                        entry.Observation.AssemblyIdentity.Name,
                        StringComparison.OrdinalIgnoreCase)),
            ];
            if (selected.Length == 0)
            {
                BrowserFrameworkDeclarationActivationBlock missing =
                    projected.Surface.InspectionErrors.Length == 0
                        ? new BrowserFrameworkDeclarationActivationBlock
                            .Unavailable(
                                BrowserFrameworkDeclarationUnavailableReason
                                    .TypeUnavailable)
                        : new BrowserFrameworkDeclarationActivationBlock
                            .Failed(
                                BrowserFrameworkDeclarationFailureReason
                                    .SurfaceIncomplete);
                return Blocked(action, missing);
            }
            if (selected.Length > 1)
            {
                return Blocked(
                    action,
                    new BrowserFrameworkDeclarationActivationBlock.Ambiguous(
                        BrowserFrameworkDeclarationAmbiguousReason.Type));
            }
            effect = new BrowserFrameworkDeclarationEffect.Type(
                projected.Surface,
                selected[0],
                entry.Library,
                entry.Observation,
                entry.Name);
        }

        authority = await BrowserSpotlightActivationAuthority.ReadAsync(
            workspace,
            entry.Basis).ConfigureAwait(false);
        if (authority.Block is { } finalBlock)
            return Blocked(action, FromAuthority(finalBlock));
        if (cancellationToken.IsCancellationRequested)
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Canceled());
        }
        if (!ReferenceEquals(_host.Current?.Identity, entry.Realization))
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Realization));
        }
        if (!TryGetCurrentEntry(action, out current)
            || !ReferenceEquals(current, entry))
        {
            return Blocked(
                action,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Result));
        }

        return new BrowserFrameworkDeclarationActivationResult.Settled(
            action,
            effect);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            _current = null;
        }
    }

    static (
        ActionEntry? Entry,
        BrowserFrameworkDeclarationActivationBlock? Block) Bind(
        InspectionWorkspace workspace,
        InspectionWorkspaceIdentity realization,
        BrowserSpotlightActivationBasis basis,
        TypeDeclarationLocatorResult.Evaluated result,
        BrowserFrameworkDeclarationDestination destination,
        ImmutableArray<WorkspaceDeclarationContext> contexts)
    {
        WorkspaceDeclarationMember observation = destination.Observation;
        if (!result.Population.Members.Any(
                member => ReferenceEquals(member, observation)))
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Refused(
                    BrowserFrameworkDeclarationRefusedReason
                        .ObservationNotInPopulation));
        }

        MetadataTypeDefinitionName? name = null;
        if (destination
            is BrowserFrameworkDeclarationDestination.Type type)
        {
            if (!result.Answers.Any(answer =>
                    answer.Candidates.Any(candidate =>
                        ReferenceEquals(candidate, type.Candidate))))
            {
                return (
                    null,
                    new BrowserFrameworkDeclarationActivationBlock.Refused(
                        BrowserFrameworkDeclarationRefusedReason
                            .CandidateNotInResult));
            }
            if (type.Candidate.Kind
                == AssemblyTypeDeclarationKind.Forwarder)
            {
                return (
                    null,
                    new BrowserFrameworkDeclarationActivationBlock.Refused(
                        BrowserFrameworkDeclarationRefusedReason.Forwarder));
            }
            if (type.Candidate.Kind
                != AssemblyTypeDeclarationKind.Definition)
            {
                return (
                    null,
                    new BrowserFrameworkDeclarationActivationBlock.Refused(
                        BrowserFrameworkDeclarationRefusedReason
                            .UnsupportedDeclarationKind));
            }
            name = type.Candidate.Name;
        }

        if (observation.Coordinate
                is not ExactLibrarySourceCoordinate.Platform library)
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Refused(
                    BrowserFrameworkDeclarationRefusedReason
                        .NonPlatformObservation));
        }
        WorkspaceDeclarationContext[] matchingContexts =
        [
            .. contexts.Where(context =>
                context.Receipt.Members.Any(member =>
                    ReferenceEquals(member, observation))),
        ];
        if (matchingContexts.Length == 0)
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Unavailable(
                    BrowserFrameworkDeclarationUnavailableReason
                        .ContextUnavailable));
        }
        if (matchingContexts.Length > 1)
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Ambiguous(
                    BrowserFrameworkDeclarationAmbiguousReason.Context));
        }

        WorkspaceDeclarationContext context = matchingContexts[0];
        if (!ReferenceEquals(context.Receipt.Workspace, realization))
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    BrowserFrameworkDeclarationStaleReason.Workspace));
        }
        if (context.Group is not { } group
            || workspace.CaptureDeclarationPopulation([context])
                is WorkspaceDeclarationPopulationCapture.Rejected)
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Unavailable(
                    BrowserFrameworkDeclarationUnavailableReason
                        .ContextUnavailable));
        }

        int memberIndex = -1;
        for (int index = 0;
            index < context.Receipt.Members.Length;
            index++)
        {
            if (ReferenceEquals(
                    context.Receipt.Members[index],
                    observation))
            {
                memberIndex = index;
                break;
            }
        }
        if (memberIndex < 0)
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Refused(
                    BrowserFrameworkDeclarationRefusedReason
                        .MalformedBinding));
        }

        AssemblyContextParticipant participant;
        FrameworkProjectionBasis projection;
        switch (observation.Origin)
        {
            case WorkspaceDeclarationOrigin.ContextLoad
            {
                Realized:
                    RealizedMemberCoordinate.Platform observedCoordinate,
            }:
            {
                if (context.ContextLoadOutcome
                        is not WorkspaceContextLoadOutcome.Loaded loaded
                    || !ReferenceEquals(loaded.Workspace, realization)
                    || !ReferenceEquals(group, loaded.Group)
                    || memberIndex >= loaded.Members.Length
                    || loaded.Members[memberIndex].Realized
                        is not RealizedMemberCoordinate.Platform
                            liveCoordinate
                    || !Equals(liveCoordinate, observedCoordinate)
                    || (liveCoordinate.Assembly is not null
                        && !string.Equals(
                            liveCoordinate.Assembly,
                            observation.AssemblyIdentity.Name,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return (
                        null,
                        new
                            BrowserFrameworkDeclarationActivationBlock
                                .Refused(
                                    BrowserFrameworkDeclarationRefusedReason
                                        .MalformedBinding));
                }
                if (!BrowserPlatformWorkspace.IsSupportedFamily(
                        liveCoordinate.Family))
                {
                    return (
                        null,
                        new
                            BrowserFrameworkDeclarationActivationBlock
                                .Refused(
                                    BrowserFrameworkDeclarationRefusedReason
                                        .UnsupportedPlatformFamily));
                }
                participant =
                    loaded.Members[memberIndex].Participant;
                projection = new(
                    liveCoordinate.Family,
                    liveCoordinate.Version,
                    liveCoordinate.Framework);
                break;
            }
            case WorkspaceDeclarationOrigin.PlatformReference reference:
            {
                if (context.Receipt.Request
                        is not WorkspaceDeclarationRequest.PlatformReference
                            request
                    || !Equals(
                        request.Coordinate,
                        reference.Source.Coordinate)
                    || memberIndex >= group.Participants.Length)
                {
                    return (
                        null,
                        new
                            BrowserFrameworkDeclarationActivationBlock
                                .Refused(
                                    BrowserFrameworkDeclarationRefusedReason
                                        .MalformedBinding));
                }
                PlatformFamilyTarget target =
                    reference.Source.Coordinate.Target;
                string? family = target.Family switch
                {
                    PlatformFamily.DotNetRuntime =>
                        BrowserPlatformWorkspace.RuntimeFamily,
                    PlatformFamily.AspNetCore =>
                        BrowserPlatformWorkspace.AspNetCoreFamily,
                    _ => null,
                };
                if (family is null)
                {
                    return (
                        null,
                        new
                            BrowserFrameworkDeclarationActivationBlock
                                .Refused(
                                    BrowserFrameworkDeclarationRefusedReason
                                        .UnsupportedPlatformFamily));
                }
                participant = group.Participants[memberIndex];
                projection = new(
                    family,
                    target.Version.ToString(),
                    target.TargetFramework.ToString());
                break;
            }
            default:
                return (
                    null,
                    new BrowserFrameworkDeclarationActivationBlock.Refused(
                        BrowserFrameworkDeclarationRefusedReason
                            .NonPlatformObservation));
        }

        if (!Equals(library, observation.Coordinate)
            || !Equals(
                participant.Assembly.Identity,
                observation.AssemblyIdentity)
            || !Equals(
                library.LibraryIdentity.Identity,
                observation.AssemblyIdentity))
        {
            return (
                null,
                new BrowserFrameworkDeclarationActivationBlock.Refused(
                    BrowserFrameworkDeclarationRefusedReason
                        .MalformedBinding));
        }

        return (
            new ActionEntry(
                realization,
                basis,
                new WeakReference<WorkspaceDeclarationContext>(context),
                new WeakReference<AssemblyContextGroup>(group),
                new WeakReference<AssemblyContextParticipant>(participant),
                projection,
                library,
                observation,
                name),
            null);
    }

    bool IsCurrent(PublicationState state)
    {
        lock (_gate)
            return ReferenceEquals(_current, state);
    }

    bool TryGetPublication(
        BrowserFrameworkDeclarationResultAuthority authority,
        [NotNullWhen(true)] out PublicationState? state)
    {
        lock (_gate)
        {
            if (_closed
                || !ReferenceEquals(authority.Issuer, _issuer)
                || _current is not { } current
                || !ReferenceEquals(
                    current.Identity,
                    authority.Publication)
                || current.ResultGeneration
                    != authority.ResultGeneration)
            {
                state = null;
                return false;
            }
            state = current;
            return true;
        }
    }

    bool TryGetCurrentEntry(
        BrowserFrameworkDeclarationAction action,
        [NotNullWhen(true)] out ActionEntry? entry)
    {
        lock (_gate)
        {
            if (_closed
                || _current is not { } current
                || !ReferenceEquals(
                    current.Identity,
                    action.Publication))
            {
                entry = null;
                return false;
            }
            return current.Entries.TryGetValue(action.Token, out entry);
        }
    }

    static BrowserFrameworkDeclarationPublication BlockedPublication(
        long resultGeneration,
        ImmutableArray<BrowserFrameworkDeclarationDestination> destinations,
        BrowserFrameworkDeclarationActivationBlock block) =>
        new(
            resultGeneration,
            [
                .. destinations.Select(destination =>
                    (BrowserFrameworkDeclarationActionPublication)new
                        BrowserFrameworkDeclarationActionPublication.Blocked(
                            destination,
                            block)),
            ]);

    static BrowserFrameworkDeclarationActivationResult.Blocked Blocked(
        BrowserFrameworkDeclarationAction action,
        BrowserFrameworkDeclarationActivationBlock block) =>
        new(action, block);

    static BrowserFrameworkDeclarationActivationBlock FromAdmission(
        WorkspaceRealizationOperationUnavailableReason reason) =>
        new BrowserFrameworkDeclarationActivationBlock.Unavailable(
            reason switch
            {
                WorkspaceRealizationOperationUnavailableReason
                    .NoActiveRealization =>
                    BrowserFrameworkDeclarationUnavailableReason
                        .NoActiveRealization,
                WorkspaceRealizationOperationUnavailableReason
                    .CoordinatorClosed =>
                    BrowserFrameworkDeclarationUnavailableReason
                        .CoordinatorClosed,
                WorkspaceRealizationOperationUnavailableReason
                    .RuntimeUnavailable =>
                    BrowserFrameworkDeclarationUnavailableReason
                        .RuntimeUnavailable,
                _ => throw new InvalidOperationException(
                    "The Workspace realization operation returned an unknown unavailable reason."),
            });

    static BrowserFrameworkDeclarationActivationBlock FromAuthority(
        BrowserSpotlightActivationBlock block) =>
        block switch
        {
            BrowserSpotlightActivationBlock.Stale stale =>
                new BrowserFrameworkDeclarationActivationBlock.Stale(
                    stale.Reason switch
                    {
                        BrowserSpotlightActivationStaleReason
                            .WorkspaceIdentity =>
                            BrowserFrameworkDeclarationStaleReason
                                .Workspace,
                        BrowserSpotlightActivationStaleReason
                            .RegistrationRevision =>
                            BrowserFrameworkDeclarationStaleReason
                                .RegistrationRevision,
                        BrowserSpotlightActivationStaleReason
                            .ScopeRevision =>
                            BrowserFrameworkDeclarationStaleReason
                                .ScopeRevision,
                        BrowserSpotlightActivationStaleReason
                            .ScopePublicationBase =>
                            BrowserFrameworkDeclarationStaleReason
                                .ScopePublicationBase,
                        BrowserSpotlightActivationStaleReason
                            .PackageOccurrence =>
                            BrowserFrameworkDeclarationStaleReason
                                .PackageOccurrence,
                        _ => throw new InvalidOperationException(
                            "Spotlight returned an unknown stale activation reason."),
                    }),
            BrowserSpotlightActivationBlock.RegistrationUnavailable =>
                new BrowserFrameworkDeclarationActivationBlock.Unavailable(
                    BrowserFrameworkDeclarationUnavailableReason
                        .RegistrationUnavailable),
            BrowserSpotlightActivationBlock.ScopeUnavailable =>
                new BrowserFrameworkDeclarationActivationBlock.Unavailable(
                    BrowserFrameworkDeclarationUnavailableReason
                        .ScopeUnavailable),
            _ => throw new InvalidOperationException(
                "Spotlight returned an unknown activation block."),
        };

    sealed class PublicationState(long resultGeneration)
    {
        internal object Identity { get; } = new();

        internal long ResultGeneration { get; } = resultGeneration;

        internal ImmutableDictionary<object, ActionEntry> Entries { get; set; }
            = ImmutableDictionary<object, ActionEntry>.Empty
                .WithComparers(ReferenceEqualityComparer.Instance);
    }

    sealed record ActionEntry(
        InspectionWorkspaceIdentity Realization,
        BrowserSpotlightActivationBasis Basis,
        WeakReference<WorkspaceDeclarationContext> Context,
        WeakReference<AssemblyContextGroup> Group,
        WeakReference<AssemblyContextParticipant> Participant,
        FrameworkProjectionBasis Projection,
        ExactLibrarySourceCoordinate.Platform Library,
        WorkspaceDeclarationMember Observation,
        MetadataTypeDefinitionName? Name);

    sealed record FrameworkProjectionBasis(
        string Family,
        string Version,
        string Framework);
}
