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
                PackageQuery.InspectionTermBindingIdentities,
                [
                    "Supply a package ID or PREFIX* as the positional argument after package query.",
                    "Supply exposed inspection facets through --where key=value.",
                    "Use --json for structured query result output.",
                    "Supply library-target through --tfm TFM, not --where. Use one exact NuGet target framework.",
                    "library-literal and --tfm require each other; neither is valid alone.",
                ]);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [Binding]);
}
