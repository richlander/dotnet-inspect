using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

internal static class PackageQueryCapabilityBinding
{
    internal const string BindingIdentity =
        "dotnet-inspect.web/package-query";
    internal const string ModuleIdentity =
        "dotnet-inspect.web/package-query-capability";

    internal static InspectionConsumerBinding<
        PackageQueryInspectionRequest,
        PackageQueryDocument> Binding { get; } =
            new(
                new(
                    BindingIdentity,
                    InspectionConsumerKind.Browser,
                    "dotnet-inspect Browser",
                    "Package Query workspace search"),
                PackageQueryCapability.Route,
                PackageQuery.InspectionTermBindingIdentities,
                [
                    "Supply an exact package ID or PREFIX* through runPackageQuery's prefix argument.",
                    "Supply exposed inspection facets as key, operator, and value records through termsJson; use only operators exposed by the facet.",
                    "maximumCandidates bounds package-candidate work; maximumMatches bounds final matching rows and does not replace that work bound.",
                    "Supply library-target through targetFramework, not as a term. Use one exact NuGet target framework.",
                ]);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [Binding]);
}
