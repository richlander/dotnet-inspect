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
