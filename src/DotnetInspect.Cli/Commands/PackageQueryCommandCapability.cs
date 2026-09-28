using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

internal static class PackageQueryCommandCapability
{
    internal const string BindingIdentity =
        "dotnet-inspect.cli/package-query";
    internal const string ModuleIdentity =
        "dotnet-inspect.cli/package-query-capability";

    internal static InspectionConsumerBinding<
        PackageQueryInspectionRequest,
        PackageQueryDocument> Binding { get; } =
            new(
                new(
                    BindingIdentity,
                    InspectionConsumerKind.Cli,
                    "dotnet-inspect CLI",
                    "package query"),
                PackageQueryCapability.Route,
                PackageQuery.InspectionTermBindingIdentities);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [Binding]);
}
