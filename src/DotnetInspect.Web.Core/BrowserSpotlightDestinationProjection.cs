using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web;

internal enum BrowserSpotlightWorkspaceDisposition
{
    PreserveCurrent,
    CreateFresh,
    Unavailable,
}

internal enum BrowserSpotlightWorkspaceRelationship
{
    Admitted,
    RegistrationCovered,
    External,
    Unavailable,
}

internal enum BrowserSpotlightDestinationHome
{
    Workspace,
    Package,
    Platform,
}

internal enum BrowserSpotlightDestinationUnavailableReason
{
    UncoveredLibrary,
}

internal abstract record BrowserSpotlightDestinationAvailability
{
    private protected BrowserSpotlightDestinationAvailability()
    {
    }

    internal sealed record Available :
        BrowserSpotlightDestinationAvailability
    {
        internal static Available Instance { get; } = new();

        private Available()
        {
        }
    }

    internal sealed record Unavailable(
        BrowserSpotlightDestinationUnavailableReason Reason) :
        BrowserSpotlightDestinationAvailability;
}

internal sealed class BrowserSpotlightDestinationPresentation
{
    internal BrowserSpotlightDestinationPresentation(
        BrowserSpotlightDestinationHome sourceHome,
        BrowserSpotlightWorkspaceRelationship workspaceRelationship,
        BrowserSpotlightWorkspaceDisposition workspaceDisposition,
        BrowserSpotlightDestinationAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        SourceHome = sourceHome;
        WorkspaceRelationship = workspaceRelationship;
        WorkspaceDisposition = workspaceDisposition;
        Availability = availability;
    }

    internal BrowserSpotlightDestinationHome SourceHome { get; }

    internal BrowserSpotlightWorkspaceRelationship WorkspaceRelationship
    {
        get;
    }

    internal BrowserSpotlightWorkspaceDisposition WorkspaceDisposition
    {
        get;
    }

    internal BrowserSpotlightDestinationAvailability Availability { get; }
}

internal enum BrowserSpotlightProjectionStaleReason
{
    RegistrationWorkspaceMismatch,
    NavigationSubjectWorkspaceMismatch,
    NavigationOccurrenceNotCurrent,
    PackageOccurrenceWorkspaceMismatch,
    PackageOccurrenceNotCurrent,
    PackageOccurrenceCoordinateMismatch,
    PlatformActionWorkspaceMismatch,
}

internal sealed class BrowserSpotlightActivationBasis
{
    internal BrowserSpotlightActivationBasis(
        long resultGeneration,
        WorkspaceScopeSnapshot scope,
        WorkspaceRegistrationRevision registrations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resultGeneration);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(registrations);

        ResultGeneration = resultGeneration;
        Scope = scope;
        Registrations = registrations;
    }

    internal long ResultGeneration { get; }

    internal WorkspaceScopeSnapshot Scope { get; }

    internal WorkspaceRegistrationRevision Registrations { get; }
}

internal abstract record BrowserSpotlightPackageRequest
{
    private protected BrowserSpotlightPackageRequest(
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        Coordinate = coordinate;
    }

    internal PackageSourceCoordinate Coordinate { get; }

    internal abstract RealizedMemberCoordinate.Package? WorkspaceCoordinate
    {
        get;
    }
}

internal sealed record BrowserSpotlightPackageRequest<TRequest> :
    BrowserSpotlightPackageRequest
    where TRequest : class
{
    internal BrowserSpotlightPackageRequest(
        PackageSourceCoordinate coordinate,
        TRequest request,
        RealizedMemberCoordinate.Package? workspaceCoordinate = null)
        : base(coordinate)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (workspaceCoordinate is not null
            && (!string.Equals(
                    workspaceCoordinate.PackageId,
                    coordinate.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    workspaceCoordinate.Version,
                    coordinate.Version,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The Workspace coordinate must identify the requested package.",
                nameof(workspaceCoordinate));
        }
        Request = request;
        WorkspaceCoordinate = workspaceCoordinate;
    }

    internal TRequest Request { get; }

    internal override RealizedMemberCoordinate.Package? WorkspaceCoordinate
    {
        get;
    }
}

internal sealed class BrowserSpotlightPlatformAction<TAction>
    where TAction : class
{
    internal BrowserSpotlightPlatformAction(
        InspectionWorkspaceIdentity workspace,
        TAction action)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(action);
        Workspace = workspace;
        Action = action;
    }

    internal InspectionWorkspaceIdentity Workspace { get; }

    internal TAction Action { get; }
}

internal abstract record BrowserSpotlightDestination<
    TPackageRequest,
    TNavigationAction,
    TPlatformAction,
    TLibraryIntent>
    where TPackageRequest : class
    where TNavigationAction : class
    where TPlatformAction : class
    where TLibraryIntent : class
{
    private protected BrowserSpotlightDestination()
    {
    }

    internal abstract BrowserSpotlightDestinationHome SourceHome { get; }

    internal sealed record Current : BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>
    {
        internal Current(
            StructuralSubjectIdentity subject,
            TNavigationAction action)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(action);
            Subject = subject;
            Action = action;
        }

        internal StructuralSubjectIdentity Subject { get; }

        internal TNavigationAction Action { get; }

        internal override BrowserSpotlightDestinationHome SourceHome =>
            Subject is StructuralSubjectIdentity.WorkspaceSubject
                ? BrowserSpotlightDestinationHome.Workspace
                : BrowserSpotlightDestinationHome.Package;
    }

    internal sealed record Package : BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>
    {
        internal Package(
            BrowserSpotlightPackageRequest<TPackageRequest> request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Request = request;
        }

        internal BrowserSpotlightPackageRequest<TPackageRequest> Request
        {
            get;
        }

        internal override BrowserSpotlightDestinationHome SourceHome =>
            BrowserSpotlightDestinationHome.Package;
    }

    internal sealed record CurrentPackageLibrary : BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>
    {
        internal CurrentPackageLibrary(
            ExactLibrarySourceCoordinate.Package coordinate,
            BrowserSpotlightPackageRequest<TPackageRequest> packageRequest,
            TLibraryIntent intent,
            WorkspacePackageOccurrence package)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(packageRequest);
            ArgumentNullException.ThrowIfNull(intent);
            ArgumentNullException.ThrowIfNull(package);
            if (coordinate.PackageCoordinate != packageRequest.Coordinate)
            {
                throw new ArgumentException(
                    "The package request must identify the exact Library package.",
                    nameof(packageRequest));
            }
            Coordinate = coordinate;
            PackageRequest = packageRequest;
            Intent = intent;
            Occurrence = package;
        }

        internal ExactLibrarySourceCoordinate.Package Coordinate { get; }

        internal BrowserSpotlightPackageRequest<TPackageRequest> PackageRequest
        {
            get;
        }

        internal TLibraryIntent Intent { get; }

        internal WorkspacePackageOccurrence Occurrence { get; }

        internal override BrowserSpotlightDestinationHome SourceHome =>
            BrowserSpotlightDestinationHome.Package;
    }

    internal sealed record PackageLibrary : BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>
    {
        internal PackageLibrary(
            ExactLibrarySourceCoordinate.Package coordinate,
            BrowserSpotlightPackageRequest<TPackageRequest> packageRequest,
            TLibraryIntent intent)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(packageRequest);
            ArgumentNullException.ThrowIfNull(intent);
            if (coordinate.PackageCoordinate != packageRequest.Coordinate)
            {
                throw new ArgumentException(
                    "The package request must identify the exact Library package.",
                    nameof(packageRequest));
            }
            Coordinate = coordinate;
            PackageRequest = packageRequest;
            Intent = intent;
        }

        internal ExactLibrarySourceCoordinate.Package Coordinate { get; }

        internal BrowserSpotlightPackageRequest<TPackageRequest> PackageRequest
        {
            get;
        }

        internal TLibraryIntent Intent { get; }

        internal override BrowserSpotlightDestinationHome SourceHome =>
            BrowserSpotlightDestinationHome.Package;
    }

    internal sealed record CurrentPlatformLibrary : BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>
    {
        internal CurrentPlatformLibrary(
            ExactLibrarySourceCoordinate.Platform coordinate,
            BrowserSpotlightPlatformAction<TPlatformAction> action)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(action);
            Coordinate = coordinate;
            Action = action;
        }

        internal ExactLibrarySourceCoordinate.Platform Coordinate { get; }

        internal BrowserSpotlightPlatformAction<TPlatformAction> Action
        {
            get;
        }

        internal override BrowserSpotlightDestinationHome SourceHome =>
            BrowserSpotlightDestinationHome.Platform;
    }

    internal sealed record PlatformLibrary : BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>
    {
        internal PlatformLibrary(
            ExactLibrarySourceCoordinate.Platform coordinate,
            BrowserSpotlightPlatformAction<TPlatformAction> action)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(action);
            Coordinate = coordinate;
            Action = action;
        }

        internal ExactLibrarySourceCoordinate.Platform Coordinate { get; }

        internal BrowserSpotlightPlatformAction<TPlatformAction> Action
        {
            get;
        }

        internal override BrowserSpotlightDestinationHome SourceHome =>
            BrowserSpotlightDestinationHome.Platform;
    }

    internal sealed record PlatformDescendant : BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>
    {
        internal PlatformDescendant(
            BrowserSpotlightPlatformAction<TPlatformAction> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            Action = action;
        }

        internal BrowserSpotlightPlatformAction<TPlatformAction> Action
        {
            get;
        }

        internal override BrowserSpotlightDestinationHome SourceHome =>
            BrowserSpotlightDestinationHome.Platform;
    }
}

internal sealed class BrowserSpotlightCoverageWitness<TPackageRequest>
    where TPackageRequest : class
{
    internal BrowserSpotlightCoverageWitness(
        BrowserSpotlightActivationBasis basis,
        BrowserSpotlightCoverageTarget<TPackageRequest> target,
        int registrationIndex,
        WorkspaceRegistration registration,
        int? populationIndex = null,
        WorkspaceEcosystemPopulationDeclaration? population = null)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(registrationIndex);
        ArgumentNullException.ThrowIfNull(registration);
        if (populationIndex.HasValue != (population is not null))
        {
            throw new ArgumentException(
                "An ecosystem population witness requires both its index and declaration.",
                nameof(population));
        }
        if (populationIndex is < 0)
            throw new ArgumentOutOfRangeException(nameof(populationIndex));

        Basis = basis;
        Target = target;
        RegistrationIndex = registrationIndex;
        Registration = registration;
        PopulationIndex = populationIndex;
        Population = population;
    }

    internal BrowserSpotlightActivationBasis Basis { get; }

    internal BrowserSpotlightCoverageTarget<TPackageRequest> Target { get; }

    internal int RegistrationIndex { get; }

    internal WorkspaceRegistration Registration { get; }

    internal int? PopulationIndex { get; }

    internal WorkspaceEcosystemPopulationDeclaration? Population { get; }
}

internal abstract record BrowserSpotlightCoverageTarget<TPackageRequest>
    where TPackageRequest : class
{
    private protected BrowserSpotlightCoverageTarget()
    {
    }

    internal sealed record Package(
        BrowserSpotlightPackageRequest<TPackageRequest> Request) :
        BrowserSpotlightCoverageTarget<TPackageRequest>;

    internal sealed record PackageLibrary(
        ExactLibrarySourceCoordinate.Package Coordinate,
        BrowserSpotlightPackageRequest<TPackageRequest> Request) :
        BrowserSpotlightCoverageTarget<TPackageRequest>;

    internal sealed record PlatformLibrary(
        ExactLibrarySourceCoordinate.Platform Coordinate) :
        BrowserSpotlightCoverageTarget<TPackageRequest>;

    internal sealed record Admitted(
        StructuralSubjectIdentity Subject) :
        BrowserSpotlightCoverageTarget<TPackageRequest>;
}

internal abstract record BrowserSpotlightDestinationActivationPlan<
    TPackageRequest,
    TNavigationAction,
    TPlatformAction,
    TLibraryIntent>
    where TPackageRequest : class
    where TNavigationAction : class
    where TPlatformAction : class
    where TLibraryIntent : class
{
    private protected BrowserSpotlightDestinationActivationPlan()
    {
    }

    internal abstract BrowserSpotlightWorkspaceDisposition WorkspaceDisposition
    {
        get;
    }

    internal sealed record NavigateCurrent(
        StructuralSubjectIdentity Subject,
        TNavigationAction Action) :
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>
    {
        internal override BrowserSpotlightWorkspaceDisposition
            WorkspaceDisposition =>
            BrowserSpotlightWorkspaceDisposition.PreserveCurrent;
    }

    internal sealed record ActivateCurrentPackageLibrary(
        WorkspacePackageOccurrence Package,
        TLibraryIntent Intent) :
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>
    {
        internal override BrowserSpotlightWorkspaceDisposition
            WorkspaceDisposition =>
            BrowserSpotlightWorkspaceDisposition.PreserveCurrent;
    }

    internal sealed record AddCurrentPackage(
        BrowserSpotlightPackageRequest<TPackageRequest> Package,
        TLibraryIntent? LibraryIntent) :
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>
    {
        internal override BrowserSpotlightWorkspaceDisposition
            WorkspaceDisposition =>
            BrowserSpotlightWorkspaceDisposition.PreserveCurrent;
    }

    internal sealed record ActivateCurrentPlatformDestination(
        BrowserSpotlightPlatformAction<TPlatformAction> Action) :
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>
    {
        internal override BrowserSpotlightWorkspaceDisposition
            WorkspaceDisposition =>
            BrowserSpotlightWorkspaceDisposition.PreserveCurrent;
    }

    internal sealed record RestoreExternalPackageWorkspace(
        BrowserSpotlightPackageRequest<TPackageRequest> Package) :
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>
    {
        internal override BrowserSpotlightWorkspaceDisposition
            WorkspaceDisposition =>
            BrowserSpotlightWorkspaceDisposition.CreateFresh;
    }

    internal sealed record Unavailable(
        BrowserSpotlightDestinationUnavailableReason Reason) :
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>
    {
        internal override BrowserSpotlightWorkspaceDisposition
            WorkspaceDisposition =>
            BrowserSpotlightWorkspaceDisposition.Unavailable;
    }
}

internal sealed class BrowserSpotlightDestinationDescriptor<
    TPackageRequest,
    TNavigationAction,
    TPlatformAction,
    TLibraryIntent>
    where TPackageRequest : class
    where TNavigationAction : class
    where TPlatformAction : class
    where TLibraryIntent : class
{
    internal BrowserSpotlightDestinationDescriptor(
        BrowserSpotlightActivationBasis basis,
        BrowserSpotlightDestination<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> destination,
        ImmutableArray<BrowserSpotlightCoverageWitness<TPackageRequest>>
            coverage,
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> plan)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(plan);
        if (coverage.IsDefault)
        {
            throw new ArgumentException(
                "Spotlight coverage must be an initialized immutable array.",
                nameof(coverage));
        }

        Basis = basis;
        Destination = destination;
        Coverage = coverage;
        Plan = plan;
        Presentation = new(
            destination.SourceHome,
            Relationship(destination, plan),
            plan.WorkspaceDisposition,
            plan is BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.Unavailable unavailable
                ? new BrowserSpotlightDestinationAvailability.Unavailable(
                    unavailable.Reason)
                : BrowserSpotlightDestinationAvailability.Available.Instance);
    }

    internal BrowserSpotlightActivationBasis Basis { get; }

    internal BrowserSpotlightDestination<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent> Destination { get; }

    internal ImmutableArray<BrowserSpotlightCoverageWitness<TPackageRequest>>
        Coverage { get; }

    internal BrowserSpotlightDestinationActivationPlan<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent> Plan { get; }

    internal BrowserSpotlightDestinationPresentation Presentation { get; }

    internal BrowserSpotlightDestinationHome SourceHome =>
        Presentation.SourceHome;

    internal BrowserSpotlightWorkspaceDisposition WorkspaceDisposition =>
        Presentation.WorkspaceDisposition;

    internal BrowserSpotlightWorkspaceRelationship WorkspaceRelationship =>
        Presentation.WorkspaceRelationship;

    internal BrowserSpotlightDestinationAvailability Availability =>
        Presentation.Availability;

    private static BrowserSpotlightWorkspaceRelationship Relationship(
        BrowserSpotlightDestination<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> destination,
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> plan) =>
        plan switch
        {
            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.Unavailable =>
                BrowserSpotlightWorkspaceRelationship.Unavailable,
            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.RestoreExternalPackageWorkspace =>
                BrowserSpotlightWorkspaceRelationship.External,
            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.AddCurrentPackage =>
                BrowserSpotlightWorkspaceRelationship.RegistrationCovered,
            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.ActivateCurrentPlatformDestination
                when destination is BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.PlatformLibrary =>
                BrowserSpotlightWorkspaceRelationship.RegistrationCovered,
            _ => BrowserSpotlightWorkspaceRelationship.Admitted,
        };
}

internal abstract record BrowserSpotlightDestinationProjectionResult<
    TPackageRequest,
    TNavigationAction,
    TPlatformAction,
    TLibraryIntent>
    where TPackageRequest : class
    where TNavigationAction : class
    where TPlatformAction : class
    where TLibraryIntent : class
{
    private protected BrowserSpotlightDestinationProjectionResult()
    {
    }

    internal sealed record Projected(
        BrowserSpotlightDestinationDescriptor<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> Descriptor) :
        BrowserSpotlightDestinationProjectionResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>;

    internal sealed record StaleBasis(
        BrowserSpotlightActivationBasis Basis,
        BrowserSpotlightDestination<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> Destination,
        BrowserSpotlightProjectionStaleReason Reason) :
        BrowserSpotlightDestinationProjectionResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>;
}

internal static class BrowserSpotlightDestinationProjection
{
    internal static BrowserSpotlightDestinationProjectionResult<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent> Project<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>(
        BrowserSpotlightActivationBasis basis,
        BrowserSpotlightDestination<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> destination)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(destination);

        if (!ReferenceEquals(
                basis.Scope.Revision.Workspace,
                basis.Registrations.Workspace))
        {
            return Stale(
                basis,
                destination,
                BrowserSpotlightProjectionStaleReason
                    .RegistrationWorkspaceMismatch);
        }

        ImmutableArray<BrowserSpotlightCoverageWitness<TPackageRequest>>
            coverage =
            CoverageFor(basis, destination);
        BrowserSpotlightDestinationActivationPlan<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> plan;

        switch (destination)
        {
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.Current current:
            {
                if (!ReferenceEquals(
                        current.Subject.Workspace.Identity,
                        basis.Scope.Revision.Workspace))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .NavigationSubjectWorkspaceMismatch);
                }

                if (OccurrenceFor(current.Subject) is { } occurrence
                    && !ContainsOccurrence(basis.Scope, occurrence))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .NavigationOccurrenceNotCurrent);
                }

                plan = new BrowserSpotlightDestinationActivationPlan<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.NavigateCurrent(
                        current.Subject,
                        current.Action);
                break;
            }
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.Package package:
                plan = coverage.IsEmpty
                    ? new BrowserSpotlightDestinationActivationPlan<
                        TPackageRequest,
                        TNavigationAction,
                        TPlatformAction,
                        TLibraryIntent>.RestoreExternalPackageWorkspace(
                            package.Request)
                    : new BrowserSpotlightDestinationActivationPlan<
                        TPackageRequest,
                        TNavigationAction,
                        TPlatformAction,
                        TLibraryIntent>.AddCurrentPackage(
                            package.Request,
                            LibraryIntent: null);
                break;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.CurrentPackageLibrary library:
            {
                WorkspacePackageOccurrence currentPackage = library.Occurrence;
                if (!ReferenceEquals(
                        currentPackage.Identity.WorkspaceIdentity,
                        basis.Scope.Revision.Workspace))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .PackageOccurrenceWorkspaceMismatch);
                }
                if (!ContainsOccurrence(basis.Scope, currentPackage))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .PackageOccurrenceNotCurrent);
                }
                if (!MatchesPackage(
                        currentPackage,
                        library.PackageRequest))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .PackageOccurrenceCoordinateMismatch);
                }

                plan = new BrowserSpotlightDestinationActivationPlan<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.ActivateCurrentPackageLibrary(
                        currentPackage,
                        library.Intent);
                break;
            }
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.PackageLibrary library:
            {
                plan = coverage.IsEmpty
                    ? new BrowserSpotlightDestinationActivationPlan<
                        TPackageRequest,
                        TNavigationAction,
                        TPlatformAction,
                        TLibraryIntent>.Unavailable(
                            BrowserSpotlightDestinationUnavailableReason
                                .UncoveredLibrary)
                    : new BrowserSpotlightDestinationActivationPlan<
                        TPackageRequest,
                        TNavigationAction,
                        TPlatformAction,
                        TLibraryIntent>.AddCurrentPackage(
                            library.PackageRequest,
                            library.Intent);
                break;
            }
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.CurrentPlatformLibrary currentPlatform:
                if (!ReferenceEquals(
                        currentPlatform.Action.Workspace,
                        basis.Scope.Revision.Workspace))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .PlatformActionWorkspaceMismatch);
                }
                plan = new BrowserSpotlightDestinationActivationPlan<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.ActivateCurrentPlatformDestination(
                        currentPlatform.Action);
                break;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.PlatformLibrary platformLibrary:
                if (!ReferenceEquals(
                        platformLibrary.Action.Workspace,
                        basis.Scope.Revision.Workspace))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .PlatformActionWorkspaceMismatch);
                }
                plan = !coverage.IsEmpty
                    ? new BrowserSpotlightDestinationActivationPlan<
                        TPackageRequest,
                        TNavigationAction,
                        TPlatformAction,
                        TLibraryIntent>.ActivateCurrentPlatformDestination(
                            platformLibrary.Action)
                    : new BrowserSpotlightDestinationActivationPlan<
                        TPackageRequest,
                        TNavigationAction,
                        TPlatformAction,
                        TLibraryIntent>.Unavailable(
                            BrowserSpotlightDestinationUnavailableReason
                                .UncoveredLibrary);
                break;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.PlatformDescendant platformDescendant:
                if (!ReferenceEquals(
                        platformDescendant.Action.Workspace,
                        basis.Scope.Revision.Workspace))
                {
                    return Stale(
                        basis,
                        destination,
                        BrowserSpotlightProjectionStaleReason
                            .PlatformActionWorkspaceMismatch);
                }
                plan = new BrowserSpotlightDestinationActivationPlan<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.ActivateCurrentPlatformDestination(
                        platformDescendant.Action);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Spotlight destination arm.");
        }

        return new BrowserSpotlightDestinationProjectionResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent>.Projected(
                new(
                    basis,
                    destination,
                    coverage,
                    plan));
    }

    private static BrowserSpotlightDestinationProjectionResult<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>.StaleBasis Stale<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>(
        BrowserSpotlightActivationBasis basis,
        BrowserSpotlightDestination<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> destination,
        BrowserSpotlightProjectionStaleReason reason)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class =>
        new(basis, destination, reason);

    private static ImmutableArray<
        BrowserSpotlightCoverageWitness<TPackageRequest>> CoverageFor<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>(
        BrowserSpotlightActivationBasis basis,
        BrowserSpotlightDestination<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> destination)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
    {
        if (!TryGetCoverageCoordinate(
                destination,
                out BrowserSpotlightCoverageTarget<TPackageRequest> target,
                out string? packageId,
                out ExactLibrarySourceCoordinate? library,
                out PlatformLibraryPopulationDeclaration? platform))
        {
            return [];
        }

        var witnesses = ImmutableArray.CreateBuilder<
            BrowserSpotlightCoverageWitness<TPackageRequest>>();
        for (int registrationIndex = 0;
            registrationIndex < basis.Registrations.Registrations.Length;
            registrationIndex++)
        {
            WorkspaceRegistration registration =
                basis.Registrations.Registrations[registrationIndex];
            switch (registration)
            {
                case WorkspaceRegistration.ExactLibrary exact
                    when library is not null
                        && exact.Coordinate == library:
                    witnesses.Add(
                        new(
                            basis,
                            target,
                            registrationIndex,
                            registration));
                    break;
                case WorkspaceRegistration.PackagePrefix prefix
                    when packageId is not null
                        && prefix.Prefix.MatchesPackageId(packageId):
                    witnesses.Add(
                        new(
                            basis,
                            target,
                            registrationIndex,
                            registration));
                    break;
                case WorkspaceRegistration.Ecosystem ecosystem:
                    AddEcosystemWitnesses(
                        basis,
                        target,
                        registrationIndex,
                        ecosystem,
                        packageId,
                        library,
                        platform,
                        witnesses);
                    break;
            }
        }
        return witnesses.ToImmutable();
    }

    private static void AddEcosystemWitnesses<TPackageRequest>(
        BrowserSpotlightActivationBasis basis,
        BrowserSpotlightCoverageTarget<TPackageRequest> target,
        int registrationIndex,
        WorkspaceRegistration.Ecosystem registration,
        string? packageId,
        ExactLibrarySourceCoordinate? library,
        PlatformLibraryPopulationDeclaration? platform,
        ImmutableArray<BrowserSpotlightCoverageWitness<TPackageRequest>>.Builder
            witnesses)
        where TPackageRequest : class
    {
        ImmutableArray<WorkspaceEcosystemPopulationDeclaration> populations =
            registration.Declaration.Populations;
        for (int populationIndex = 0;
            populationIndex < populations.Length;
            populationIndex++)
        {
            WorkspaceEcosystemPopulationDeclaration population =
                populations[populationIndex];
            bool matches = population switch
            {
                WorkspaceEcosystemPopulationDeclaration.ExactLibrary exact =>
                    library is not null && exact.Coordinate == library,
                WorkspaceEcosystemPopulationDeclaration.PackagePrefix prefix =>
                    packageId is not null
                    && prefix.Prefix.MatchesPackageId(packageId),
                WorkspaceEcosystemPopulationDeclaration.Platform declared =>
                    platform is not null
                    && declared.Population == platform,
                _ => throw new InvalidOperationException(
                    "Unknown Workspace ecosystem population arm."),
            };
            if (matches)
            {
                witnesses.Add(
                    new(
                        basis,
                        target,
                        registrationIndex,
                        registration,
                        populationIndex,
                        population));
            }
        }
    }

    private static bool TryGetCoverageCoordinate<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent>(
        BrowserSpotlightDestination<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> destination,
        out BrowserSpotlightCoverageTarget<TPackageRequest> target,
        out string? packageId,
        out ExactLibrarySourceCoordinate? library,
        out PlatformLibraryPopulationDeclaration? platform)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
    {
        switch (destination)
        {
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.Current current
                when TryGetCurrentCoverageCoordinate(
                    current.Subject,
                    out packageId,
                    out library):
                target = new BrowserSpotlightCoverageTarget<
                    TPackageRequest>.Admitted(current.Subject);
                platform = null;
                return true;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.Package package:
                target = new BrowserSpotlightCoverageTarget<
                    TPackageRequest>.Package(
                    package.Request);
                packageId = package.Request.Coordinate.PackageId;
                library = null;
                platform = null;
                return true;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.CurrentPackageLibrary packageLibrary:
                target = new BrowserSpotlightCoverageTarget<
                    TPackageRequest>.PackageLibrary(
                    packageLibrary.Coordinate,
                    packageLibrary.PackageRequest);
                packageId =
                    packageLibrary.PackageRequest.Coordinate.PackageId;
                library = packageLibrary.Coordinate;
                platform = null;
                return true;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.PackageLibrary packageLibrary:
                target = new BrowserSpotlightCoverageTarget<
                    TPackageRequest>.PackageLibrary(
                    packageLibrary.Coordinate,
                    packageLibrary.PackageRequest);
                packageId =
                    packageLibrary.PackageRequest.Coordinate.PackageId;
                library = packageLibrary.Coordinate;
                platform = null;
                return true;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.CurrentPlatformLibrary platformLibrary:
                target = new BrowserSpotlightCoverageTarget<
                    TPackageRequest>.PlatformLibrary(
                    platformLibrary.Coordinate);
                packageId = null;
                library = platformLibrary.Coordinate;
                platform = platformLibrary.Coordinate.Population;
                return true;
            case BrowserSpotlightDestination<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.PlatformLibrary platformLibrary:
                target = new BrowserSpotlightCoverageTarget<
                    TPackageRequest>.PlatformLibrary(
                    platformLibrary.Coordinate);
                packageId = null;
                library = platformLibrary.Coordinate;
                platform = platformLibrary.Coordinate.Population;
                return true;
            default:
                target = null!;
                packageId = null;
                library = null;
                platform = null;
                return false;
        }
    }

    private static bool TryGetCurrentCoverageCoordinate(
        StructuralSubjectIdentity subject,
        out string? packageId,
        out ExactLibrarySourceCoordinate? library)
    {
        switch (subject)
        {
            case StructuralSubjectIdentity.PackageSubject package:
                packageId = package.Coordinate.PackageId;
                library = null;
                return true;
            case StructuralSubjectIdentity.LibrarySubject packageLibrary:
                packageId = packageLibrary.Coordinate.PackageId;
                library = new ExactLibrarySourceCoordinate.Package(
                    PackageSourceCoordinate.Create(
                        packageLibrary.Coordinate.PackageId,
                        packageLibrary.Coordinate.Version),
                    new ManagedMetadataIdentity.Assembly(
                        packageLibrary.Identity.Assembly));
                return true;
            default:
                packageId = null;
                library = null;
                return false;
        }
    }

    private static WorkspacePackageOccurrence? OccurrenceFor(
        StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.WorkspaceSubject => null,
            StructuralSubjectIdentity.PackageSubject package =>
                package.Occurrence,
            StructuralSubjectIdentity.AllLibrariesSubject libraries =>
                libraries.Package.Occurrence,
            StructuralSubjectIdentity.LibrarySubject library =>
                library.Package.Occurrence,
            StructuralSubjectIdentity.TypeSubject type =>
                type.Library.Package.Occurrence,
            StructuralSubjectIdentity.MemberSubject member =>
                member.DeclaringType.Library.Package.Occurrence,
            _ => throw new InvalidOperationException(
                "Unknown structural subject arm."),
        };

    private static bool ContainsOccurrence(
        WorkspaceScopeSnapshot scope,
        WorkspacePackageOccurrence occurrence) =>
        scope.Revision.Packages.Any(
            candidate => ReferenceEquals(candidate, occurrence));

    private static bool MatchesPackage(
        WorkspacePackageOccurrence occurrence,
        BrowserSpotlightPackageRequest request) =>
        request.WorkspaceCoordinate is { } coordinate
        && occurrence.Package.Coordinate == coordinate;
}
