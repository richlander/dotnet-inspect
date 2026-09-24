using System.Collections.Immutable;
using DotnetInspector.Queries;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class PackageQueryCapabilityResourcePaths
{
    public static readonly ResourcePath Document =
        new("package-query");

    public static readonly ResourcePath Route =
        Document.Append("routes", "default");

    public static readonly ResourcePath QuerySpace =
        Document.Append("query");

    public static ResourcePath QueryFacet(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return QuerySpace.Append("facets", key);
    }

    public static ImmutableArray<
        InspectionCapabilityResourcePathRegistration> Create(
        InspectionCapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var registrations =
            ImmutableArray.CreateBuilder<
                InspectionCapabilityResourcePathRegistration>();

        foreach (InspectionDocumentRegistration document
                 in catalog.Documents)
        {
            EnsurePackageDocument(document);
            registrations.Add(
                new(
                    ResourceExplanationCatalog.DocumentIdentity(
                        document),
                    Document));
        }

        foreach (InspectionRouteRegistration route in catalog.Routes)
        {
            EnsurePackageRoute(route);
            registrations.Add(
                new(
                    ResourceExplanationCatalog.RouteIdentity(route),
                    Route));
        }

        foreach (QuerySpaceBinding querySpace in catalog.Routes
                     .Select(static route => route.QuerySpace)
                     .DistinctBy(
                         static querySpace =>
                             querySpace.Descriptor.Identity,
                         StringComparer.Ordinal))
        {
            if (!ReferenceEquals(querySpace, PackageQuery.QuerySpace))
            {
                throw new ArgumentException(
                    "Package Query Resource Explanation paths cannot "
                    + "register another Query Space.",
                    nameof(catalog));
            }
            registrations.Add(
                new(
                    ResourceExplanationCatalog.QuerySpaceIdentity(
                        querySpace),
                    QuerySpace));
            foreach (QuerySpaceOperationTermDescriptor term
                     in querySpace.Descriptor.Operation.Terms)
            {
                registrations.Add(
                    new(
                        ResourceExplanationCatalog.QueryFacetIdentity(
                            querySpace,
                            term),
                        QueryFacet(term.Key)));
            }
        }

        foreach (InspectionConsumerBinding binding in catalog.Bindings)
        {
            EnsurePackageRoute(binding.Route);
            string segment = binding.Descriptor.Kind switch
            {
                InspectionConsumerKind.Cli => "cli",
                InspectionConsumerKind.Browser => "browser",
                InspectionConsumerKind.OperationBackedSection =>
                    "operation-backed-section",
                _ => throw new InvalidOperationException(
                    "Unknown Package Query consumer kind."),
            };
            registrations.Add(
                new(
                    ResourceExplanationCatalog.ConsumerBindingIdentity(
                        binding),
                    Document.Append("bindings", segment)));
        }

        return registrations.ToImmutable();
    }

    private static void EnsurePackageDocument(
        InspectionDocumentRegistration document)
    {
        if (!ReferenceEquals(
                document,
                PackageQueryCapability.Document))
        {
            throw new ArgumentException(
                "Package Query Resource Explanation paths cannot register "
                + "another inspection document.",
                nameof(document));
        }
    }

    private static void EnsurePackageRoute(
        InspectionRouteRegistration route)
    {
        if (!ReferenceEquals(route, PackageQueryCapability.Route))
        {
            throw new ArgumentException(
                "Package Query Resource Explanation paths cannot register "
                + "another inspection route.",
                nameof(route));
        }
    }
}
