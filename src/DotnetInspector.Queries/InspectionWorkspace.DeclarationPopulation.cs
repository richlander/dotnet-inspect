using System.Collections.Immutable;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    int _nextDeclarationContextOrder;
    readonly List<WorkspaceDeclarationContext> _declarationContexts = [];
    WorkspaceDeclarationPopulation? _declarationPopulation;
    WorkspaceDeclarationLocator? _declarationLocator;
    WorkspaceDeclarationLocator? _declarationObserver;

    internal int BeginDeclarationContext()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open, this);
            return checked(_nextDeclarationContextOrder++);
        }
    }

    internal WorkspaceDeclarationContext CompleteDeclarationContext(
        int order,
        WorkspaceContextInput request,
        WorkspaceContextLoadOutcome outcome)
    {
        var members = ImmutableArray.CreateBuilder<WorkspaceDeclarationMember>();
        if (outcome is WorkspaceContextLoadOutcome.Loaded loaded)
        {
            foreach (WorkspaceContextMember member in loaded.Members)
            {
                ResolvedAssemblyReference assembly = member.Participant.Assembly;
                var identity = new ManagedMetadataIdentity.Assembly(assembly.Identity);
                ExactLibrarySourceCoordinate? coordinate = member.Realized switch
                {
                    RealizedMemberCoordinate.Package package =>
                        new ExactLibrarySourceCoordinate.Package(
                            PackageSourceCoordinate.Create(package.PackageId, package.Version), identity),
                    RealizedMemberCoordinate.Platform { Family: "runtime" } =>
                        new ExactLibrarySourceCoordinate.Platform(
                            new(PlatformFamily.DotNetRuntime), identity),
                    RealizedMemberCoordinate.Platform { Family: "aspnetcore" } =>
                        new ExactLibrarySourceCoordinate.Platform(
                            new(PlatformFamily.AspNetCore), identity),
                    _ => null,
                };
                members.Add(new(
                    new(_identity, order, members.Count),
                    coordinate, assembly.Identity,
                    new WorkspaceDeclarationOrigin.ContextLoad(member.Declared, member.Realized),
                    assembly.Provenance,
                    coordinate is ExactLibrarySourceCoordinate.Package
                        ? new(
                            request.Framework,
                            request.RuntimeIdentifier)
                        : null));
            }
        }

        var context = new WorkspaceDeclarationContext(
            new(_identity, order, new WorkspaceDeclarationRequest.ContextLoad(request),
                outcome is WorkspaceContextLoadOutcome.Loaded,
                members.ToImmutable(),
                outcome is WorkspaceContextLoadOutcome.Failed failed
                    ? [.. failed.Failures.Select(static failure =>
                        new WorkspaceDeclarationFailure.ContextLoad(failure))]
                    : []),
            outcome);
        return PublishDeclarationContext(context);
    }

    internal WorkspaceDeclarationContext PublishDeclarationContext(WorkspaceDeclarationContext context)
    {
        WorkspaceDeclarationLocator? observer;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state != InspectionWorkspaceState.Open, this);
            _declarationContexts.Add(context);
            _declarationPopulation = null;
            observer = _declarationObserver;
        }
        observer?.PopulationChanged();
        return context;
    }

    /// <summary>
    /// Captures exactly the supplied admitted contexts, including upstream
    /// failures. It does not realize registrations or scan declarations.
    /// </summary>
    public WorkspaceDeclarationPopulationCapture CaptureDeclarationPopulation(
        ImmutableArray<WorkspaceDeclarationContext> contexts) =>
        CaptureDeclarationPopulation(contexts, []);

    /// <summary>
    /// Captures the supplied admitted contexts while omitting explicitly
    /// selected exact member occurrences.
    /// </summary>
    public WorkspaceDeclarationPopulationCapture CaptureDeclarationPopulation(
        ImmutableArray<WorkspaceDeclarationContext> contexts,
        ImmutableArray<WorkspaceDeclarationOccurrence>
            excludedOccurrences)
    {
        lock (_gate)
        {
            if (DeclarationPopulationAvailability() is { } unavailable)
                return new WorkspaceDeclarationPopulationCapture.Rejected(unavailable);
            if (contexts.IsDefault
                || contexts.Any(static context => context is null)
                || excludedOccurrences.IsDefault
                || excludedOccurrences.Any(
                    static occurrence => occurrence is null))
            {
                return new WorkspaceDeclarationPopulationCapture.Rejected(
                    WorkspaceDeclarationPopulationFailure.MalformedSelection);
            }
            var seen = new HashSet<WorkspaceDeclarationContext>();
            foreach (WorkspaceDeclarationContext context in contexts)
            {
                if (!ReferenceEquals(context.Receipt.Workspace, _identity))
                {
                    return new WorkspaceDeclarationPopulationCapture.Rejected(
                        WorkspaceDeclarationPopulationFailure.ForeignWorkspace);
                }
                if (!seen.Add(context))
                {
                    return new WorkspaceDeclarationPopulationCapture.Rejected(
                        WorkspaceDeclarationPopulationFailure.DuplicateContext);
                }
                if (context.Group is { } group
                    && !_groups.Contains(group)
                    && !IsPackageDeclarationContext(context))
                {
                    return new WorkspaceDeclarationPopulationCapture.Rejected(
                        WorkspaceDeclarationPopulationFailure.ContextUnavailable);
                }
            }

            var exclusions =
                new HashSet<WorkspaceDeclarationOccurrence>();
            foreach (WorkspaceDeclarationOccurrence occurrence
                in excludedOccurrences)
            {
                if (!ReferenceEquals(occurrence.Workspace, _identity)
                    || !exclusions.Add(occurrence)
                    || !contexts.Any(context =>
                        context.Receipt.Members.Any(member =>
                            ReferenceEquals(
                                member.Occurrence,
                                occurrence))))
                {
                    return new WorkspaceDeclarationPopulationCapture.Rejected(
                        WorkspaceDeclarationPopulationFailure
                            .OccurrenceNotSelected);
                }
            }

            return new WorkspaceDeclarationPopulationCapture.Captured(
                CreateDeclarationPopulation(
                    contexts,
                    exclusions));
        }
    }

    /// <summary>
    /// Captures the current admitted declaration contexts in publication order.
    /// The returned contexts remain valid only while this Workspace remains open.
    /// </summary>
    public ImmutableArray<WorkspaceDeclarationContext>
        GetDeclarationContextsSnapshot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            return [.. _declarationContexts];
        }
    }

    /// <summary>
    /// Gets this Workspace's lazy locator over its admitted declaration contexts.
    /// The first call fixes its limits; getting it does not activate inspection.
    /// </summary>
    public WorkspaceDeclarationLocator GetDeclarationLocator(
        WorkspaceDeclarationLocatorOptions? options = null) =>
        GetDeclarationLocator(options, WorkspaceDeclarationLocator.YieldAsync);

    internal WorkspaceDeclarationLocator GetDeclarationLocator(
        WorkspaceDeclarationLocatorOptions? options,
        Func<ValueTask> yieldAsync)
    {
        options?.Validate();
        ArgumentNullException.ThrowIfNull(yieldAsync);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state != InspectionWorkspaceState.Open, this);
            if (_declarationLocator is { } existing)
            {
                if (options is not null && existing.Options != options)
                    throw new InvalidOperationException("The Workspace declaration locator's limits are already fixed.");
                return existing;
            }
            return _declarationLocator = new(this, options ?? new(), yieldAsync);
        }
    }

    internal WorkspaceDeclarationPopulationCapture ObserveDeclarationPopulation(
        WorkspaceDeclarationLocator observer)
    {
        lock (_gate)
        {
            if (DeclarationPopulationAvailability() is { } unavailable)
                return new WorkspaceDeclarationPopulationCapture.Rejected(unavailable);
            _declarationObserver = observer;
            return new WorkspaceDeclarationPopulationCapture.Captured(
                _declarationPopulation ??= CreateDeclarationPopulation(_declarationContexts));
        }
    }

    internal WorkspaceDeclarationPopulationCapture CaptureObservedDeclarationPopulation()
    {
        lock (_gate)
        {
            if (DeclarationPopulationAvailability() is { } unavailable)
                return new WorkspaceDeclarationPopulationCapture.Rejected(unavailable);
            return new WorkspaceDeclarationPopulationCapture.Captured(
                _declarationPopulation ??= CreateDeclarationPopulation(_declarationContexts));
        }
    }

    WorkspaceDeclarationPopulation CreateDeclarationPopulation(
        IEnumerable<WorkspaceDeclarationContext> contexts) =>
        CreateDeclarationPopulation(
            contexts,
            excludedOccurrences: null);

    WorkspaceDeclarationPopulation CreateDeclarationPopulation(
        IEnumerable<WorkspaceDeclarationContext> contexts,
        IReadOnlySet<WorkspaceDeclarationOccurrence>?
            excludedOccurrences)
    {
        var ordered = contexts.OrderBy(static context => context.Receipt.Order).ToArray();
        var access = new Dictionary<
            WorkspaceDeclarationOccurrence,
            WorkspaceDeclarationMemberAccess>();
        var receipts =
            ImmutableArray.CreateBuilder<
                WorkspaceDeclarationContextReceipt>(
                    ordered.Length);
        foreach (WorkspaceDeclarationContext context in ordered)
        {
            WorkspaceDeclarationContextReceipt original =
                context.Receipt;
            ImmutableArray<WorkspaceDeclarationMember> members =
                excludedOccurrences is null
                    ? original.Members
                    : [
                        .. original.Members.Where(member =>
                            !excludedOccurrences.Contains(
                                member.Occurrence)),
                    ];
            WorkspaceDeclarationContextReceipt receipt =
                members.Length == original.Members.Length
                    ? original
                    : new(
                        original.Workspace,
                        original.Order,
                        original.Request,
                        original.IsRealized,
                        members,
                        original.Failures);
            receipts.Add(receipt);

            if (context.Group is { } group)
            {
                for (int index = 0;
                    index < group.Participants.Length;
                    index++)
                {
                    WorkspaceDeclarationMember member =
                        original.Members[index];
                    if (excludedOccurrences?.Contains(
                            member.Occurrence)
                        == true)
                    {
                        continue;
                    }
                    access.Add(
                        member.Occurrence,
                        new WorkspaceDeclarationMemberAccess.AssemblyContext(
                            member,
                            group,
                            group.Participants[index].Assembly));
                }
            }
            else if (!context.LibraryOccurrences.IsDefaultOrEmpty)
            {
                LibraryTypeDeclarationInventoryInspectionBounds bounds =
                    context.LibraryInspectionBounds
                    ?? throw new InvalidOperationException(
                        "A Library-backed declaration context requires "
                            + "inspection bounds.");
                for (int index = 0;
                    index < context.LibraryOccurrences.Length;
                    index++)
                {
                    WorkspaceDeclarationMember member =
                        original.Members[index];
                    if (excludedOccurrences?.Contains(
                            member.Occurrence)
                        == true)
                    {
                        continue;
                    }
                    access.Add(
                        member.Occurrence,
                        new WorkspaceDeclarationMemberAccess
                            .LibraryOccurrence(
                                member,
                                context.LibraryOccurrences[index],
                                bounds));
                }
            }
        }
        return new(
            this,
            new(_identity, receipts.MoveToImmutable()),
            access);
    }

    internal WorkspaceDeclarationPopulationFailure? DeclarationPopulationAvailability()
    {
        lock (_gate)
        {
            return _state switch
            {
                InspectionWorkspaceState.Open => null,
                InspectionWorkspaceState.Closing => WorkspaceDeclarationPopulationFailure.WorkspaceClosing,
                _ => WorkspaceDeclarationPopulationFailure.WorkspaceClosed,
            };
        }
    }
}
