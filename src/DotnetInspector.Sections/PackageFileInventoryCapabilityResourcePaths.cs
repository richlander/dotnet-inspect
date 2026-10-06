using System.Collections.Immutable;
using DotnetInspector.Queries;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class PackageFileInventoryCapabilityResourcePaths
{
    public static readonly ResourcePath Document =
        new("package-files");

    public static readonly ResourcePath Route =
        Document.Append("routes", "default");

    public static readonly ResourcePath QuerySpace =
        Document.Append("query");

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
            EnsureDocument(document);
            registrations.Add(
                new(
                    ResourceExplanationCatalog.DocumentIdentity(document),
                    Document));
        }

        foreach (InspectionRouteRegistration route in catalog.Routes)
        {
            EnsureRoute(route);
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
            if (!ReferenceEquals(
                    querySpace,
                    PackageFileInventoryQuery.QuerySpace))
            {
                throw new ArgumentException(
                    "Package-file Resource Explanation paths cannot "
                    + "register another Query Space.",
                    nameof(catalog));
            }
            registrations.Add(
                new(
                    ResourceExplanationCatalog.QuerySpaceIdentity(
                        querySpace),
                    QuerySpace));
        }

        foreach (InspectionConsumerBinding binding in catalog.Bindings)
        {
            EnsureRoute(binding.Route);
            string segment = binding.Descriptor.Kind switch
            {
                InspectionConsumerKind.Cli => "cli",
                InspectionConsumerKind.Browser => "browser",
                InspectionConsumerKind.OperationBackedSection =>
                    "operation-backed-section",
                _ => throw new InvalidOperationException(
                    "Unknown package-file consumer kind."),
            };
            registrations.Add(
                new(
                    ResourceExplanationCatalog.ConsumerBindingIdentity(
                        binding),
                    Document.Append("bindings", segment)));
        }

        return registrations.ToImmutable();
    }

    private static void EnsureDocument(
        InspectionDocumentRegistration document)
    {
        if (!ReferenceEquals(
                document,
                PackageFileInventoryCapability.Document))
        {
            throw new ArgumentException(
                "Package-file Resource Explanation paths cannot register "
                    + "another inspection document.",
                nameof(document));
        }
    }

    private static void EnsureRoute(InspectionRouteRegistration route)
    {
        if (!ReferenceEquals(
                route,
                PackageFileInventoryCapability.Route))
        {
            throw new ArgumentException(
                "Package-file Resource Explanation paths cannot register "
                    + "another inspection route.",
                nameof(route));
        }
    }
}
