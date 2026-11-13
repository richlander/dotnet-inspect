using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspector.ResearchSections;

/// <summary>
/// Canonical Resource Explanation paths for Diff's Compare document, its
/// inspection route, and the consumer bindings that execute it.
/// </summary>
public static class DiffAnalysisCapabilityResourcePaths
{
    public static readonly ResourcePath Document = new("diff-analysis");

    public static readonly ResourcePath Route =
        Document.Append("routes", "compare");

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
            if (!ReferenceEquals(document, DiffAnalysisCatalog.Document))
            {
                throw new ArgumentException(
                    "Diff analysis Resource Explanation paths cannot "
                    + "register another inspection document.",
                    nameof(catalog));
            }
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
                    "Unknown Diff analysis consumer kind."),
            };
            registrations.Add(
                new(
                    ResourceExplanationCatalog.ConsumerBindingIdentity(
                        binding),
                    Document.Append("bindings", segment)));
        }

        return registrations.ToImmutable();
    }

    private static void EnsureRoute(InspectionRouteRegistration route)
    {
        if (!ReferenceEquals(route, DiffAnalysisCatalog.Route))
        {
            throw new ArgumentException(
                "Diff analysis Resource Explanation paths cannot register "
                + "another inspection route.",
                nameof(route));
        }
    }
}
