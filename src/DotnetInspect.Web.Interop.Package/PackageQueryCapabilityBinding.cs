using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Package;

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
                PackageQuery.InspectionTermBindingIdentities);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [Binding]);
}
