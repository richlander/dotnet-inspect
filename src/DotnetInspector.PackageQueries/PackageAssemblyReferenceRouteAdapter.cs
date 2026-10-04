using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One request-specific ordinary Package route over an immutable reachable
/// Package supplier association.
/// </summary>
public sealed class PackageAssemblyReferenceExternalRoute :
    AssemblyReferenceExternalRoute
{
    public PackageAssemblyReferenceExternalRoute(
        AssemblyBindingRequest request,
        PackageAssemblyReferenceSupplierAssociation association,
        PackageAssemblyReferenceRouteEligibilityReceipt packageRoutes)
        : base(
            request,
            (packageRoutes
                ?? throw new ArgumentNullException(nameof(packageRoutes)))
                .Generation)
    {
        ArgumentNullException.ThrowIfNull(association);
        if (request.Target
                is not AssemblyBindingTarget.AssemblyReference
            || request.Origin
                is not AssemblyBindingOrigin.RequestingAssembly)
        {
            throw new ArgumentException(
                "A Package route requires an ordinary assembly-reference request from one requesting assembly.",
                nameof(request));
        }
        if (association.RouteProjection
                is not PackageAssemblyReferenceRouteProjectionOutcome
                    .Completed completed
            || !ReferenceEquals(completed.Receipt, packageRoutes))
        {
            throw new ArgumentException(
                "The Package route must retain the supplier association's exact completed eligibility receipt.",
                nameof(packageRoutes));
        }

        Association = association;
        PackageRoutes = packageRoutes;
    }

    public PackageAssemblyReferenceSupplierAssociation Association
    { get; }

    public PackageAssemblyReferenceRouteEligibilityReceipt PackageRoutes
    { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope =>
        PackageRoutes.FocalScope;
}

/// <summary>
/// Executes one exact Package route through the reusable supplier association.
/// </summary>
public static class PackageAssemblyReferenceRouteAdapter
{
    public static async ValueTask<
        PackageAssemblyReferenceSupplierOutcome> ExecuteAsync(
            PackageAssemblyReferenceExternalRoute route,
            AssemblyBindingSelection referencingContextSelection,
            PackageHouse packageHouse,
            PackageSourceOperationLease packageSourceOperation,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(referencingContextSelection);
        ArgumentNullException.ThrowIfNull(packageHouse);
        ArgumentNullException.ThrowIfNull(packageSourceOperation);
        cancellationToken.ThrowIfCancellationRequested();

        PackageAssemblyReferenceSupplierOutcome outcome =
            await route.Association.ResolveAsync(
                    route.Request,
                    referencingContextSelection,
                    packageHouse,
                    packageSourceOperation)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(outcome.Request, route.Request))
        {
            throw new InvalidOperationException(
                "The Package supplier outcome must retain the exact route request.");
        }
        if (outcome
                is PackageAssemblyReferenceSupplierOutcome.Missing missing
            && !ReferenceEquals(missing.Route, route.PackageRoutes))
        {
            throw new InvalidOperationException(
                "A complete Package miss must retain the exact route eligibility receipt.");
        }

        return outcome;
    }
}
