using System.Collections.Immutable;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    int _nextDeclarationContextOrder;
    readonly List<WorkspaceDeclarationContext> _declarationContexts = [];
    WorkspaceTypeDeclarationLocator? _typeDeclarationLocator;
    bool _typeDeclarationLocatorActive;

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
                    coordinate, assembly.Identity, member.Declared,
                    member.Realized, assembly.Provenance));
            }
        }

        var context = new WorkspaceDeclarationContext(
            new(_identity, order, request,
                outcome is WorkspaceContextLoadOutcome.Loaded,
                members.ToImmutable(),
                outcome is WorkspaceContextLoadOutcome.Failed failed ? failed.Failures : []),
            outcome);
        WorkspaceTypeDeclarationLocator? locator = null;
        lock (_gate)
        {
            if (_state == InspectionWorkspaceState.Open)
            {
                _declarationContexts.Add(context);
                if (_typeDeclarationLocatorActive)
                    locator = _typeDeclarationLocator;
            }
        }
        locator?.PopulationChanged();
        return context;
    }

    /// <summary>
    /// Captures exactly the supplied loader-issued contexts, including upstream
    /// failures. It does not realize registrations or scan declarations.
    /// </summary>
    public WorkspaceDeclarationPopulationCapture CaptureDeclarationPopulation(
        ImmutableArray<WorkspaceDeclarationContext> contexts)
    {
        lock (_gate)
        {
            if (DeclarationPopulationAvailability() is { } unavailable)
                return new WorkspaceDeclarationPopulationCapture.Rejected(unavailable);
            return CaptureDeclarationPopulationCore(contexts);
        }
    }

    /// <summary>
    /// Returns this Workspace's dormant declaration locator. Declaration work
    /// begins only when its first valid query is admitted.
    /// </summary>
    public WorkspaceTypeDeclarationLocator GetTypeDeclarationLocator()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            return _typeDeclarationLocator ??= new WorkspaceTypeDeclarationLocator(this);
        }
    }

    internal WorkspaceDeclarationPopulationCapture
        ActivateAndCaptureDeclarationPopulation(
            WorkspaceTypeDeclarationLocator locator)
    {
        lock (_gate)
        {
            if (DeclarationPopulationAvailability() is { } unavailable)
                return new WorkspaceDeclarationPopulationCapture.Rejected(unavailable);
            if (!ReferenceEquals(locator, _typeDeclarationLocator))
            {
                return new WorkspaceDeclarationPopulationCapture.Rejected(
                    WorkspaceDeclarationPopulationFailure.ForeignWorkspace);
            }

            locator.ActivateFromWorkspace();
            _typeDeclarationLocatorActive = true;
            return CaptureDeclarationPopulationCore([.. _declarationContexts]);
        }
    }

    internal WorkspaceDeclarationPopulationCapture
        CaptureCurrentDeclarationPopulation(
            WorkspaceTypeDeclarationLocator locator)
    {
        lock (_gate)
        {
            if (DeclarationPopulationAvailability() is { } unavailable)
                return new WorkspaceDeclarationPopulationCapture.Rejected(unavailable);
            if (!_typeDeclarationLocatorActive
                || !ReferenceEquals(locator, _typeDeclarationLocator))
            {
                return new WorkspaceDeclarationPopulationCapture.Rejected(
                    WorkspaceDeclarationPopulationFailure.ForeignWorkspace);
            }
            return CaptureDeclarationPopulationCore([.. _declarationContexts]);
        }
    }

    WorkspaceDeclarationPopulationCapture CaptureDeclarationPopulationCore(
        ImmutableArray<WorkspaceDeclarationContext> contexts)
    {
        if (contexts.IsDefault || contexts.Any(static context => context is null))
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
            if (context.Outcome is WorkspaceContextLoadOutcome.Loaded loaded
                && !_groups.Contains(loaded.Group))
            {
                return new WorkspaceDeclarationPopulationCapture.Rejected(
                    WorkspaceDeclarationPopulationFailure.ContextUnavailable);
            }
        }

        var ordered = contexts.OrderBy(static context => context.Receipt.Order).ToArray();
        var access = new Dictionary<
            WorkspaceDeclarationOccurrence,
            (AssemblyContextGroup Group, ResolvedAssemblyReference Assembly)>();
        foreach (WorkspaceDeclarationContext context in ordered)
        {
            if (context.Outcome is not WorkspaceContextLoadOutcome.Loaded loaded)
                continue;
            for (int index = 0; index < loaded.Members.Length; index++)
            {
                access.Add(
                    context.Receipt.Members[index].Occurrence,
                    (loaded.Group, loaded.Members[index].Participant.Assembly));
            }
        }
        return new WorkspaceDeclarationPopulationCapture.Captured(
            new(
                this,
                new(
                    _identity,
                    [.. ordered.Select(static context => context.Receipt)]),
                access));
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
