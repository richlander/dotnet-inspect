using DotnetInspector.PlatformQueries;
using DotnetInspector.Queries;

namespace DotnetInspector.ResearchQueries;

/// <summary>
/// Binds one closed Package/Platform supplier selection to the exact deferred
/// route that authorized Workspace replacement.
/// </summary>
public static class
    ExternalAssemblyReferenceWorkspaceContinuationDemandAdapter
{
    public static AssemblyReferenceWorkspaceContinuationDemand Create(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionOutcome.AcquisitionRequired
            acquisition)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!ReferenceEquals(
                request.BindingRequest,
                acquisition.Request)
            || !ReferenceEquals(
                request.BindingRequest,
                acquisition.FinalRequest)
            || !ReferenceEquals(
                request.Generation,
                acquisition.Generation))
        {
            throw new ArgumentException(
                "The acquisition result must retain the resolution request's exact predecessor generation.",
                nameof(acquisition));
        }

        return new(
            request,
            acquisition.RouteSet,
            acquisition.SelectedRoute,
            acquisition.OwnerEvidence);
    }

    public static AssemblyReferenceWorkspaceContinuationDemand Create(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceExternalRouteSet routeSet,
        ExternalAssemblyReferenceSupplierOutcome supplier)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(routeSet);
        ArgumentNullException.ThrowIfNull(supplier);
        if (!ReferenceEquals(request.BindingRequest, supplier.Request))
        {
            throw new ArgumentException(
                "The supplier must retain the resolution request.",
                nameof(supplier));
        }

        AssemblyReferenceExternalRoute selectedRoute = supplier switch
        {
            ExternalAssemblyReferenceSupplierOutcome.PackageOwned package =>
                package.Route,
            ExternalAssemblyReferenceSupplierOutcome.PlatformOwned platform =>
                platform.Platform.Route,
            _ => throw new ArgumentException(
                "Workspace continuation requires a selected Package or Platform supplier.",
                nameof(supplier)),
        };
        return new(request, routeSet, selectedRoute, supplier);
    }
}
