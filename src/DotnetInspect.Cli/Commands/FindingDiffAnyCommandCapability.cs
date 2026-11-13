using DotnetInspector.Diff;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The CLI consumer of Finding Diff Any's Library route: the exact-name
/// <c>diff -S "Changed Types"</c> section runs the route over the
/// request's one Library pair.
/// </summary>
internal static class FindingDiffAnyCommandCapability
{
    internal const string ModuleIdentity =
        "dotnet-inspect.cli/finding-diff-any-capability";

    internal const string BindingIdentity =
        "dotnet-inspect.cli/finding-diff-any";

    internal static InspectionConsumerBinding<
        FindingDiffAnyLibraryRequest,
        FindingDiffAnyLibraryDocument> Binding { get; } =
        new(
            new(
                BindingIdentity,
                InspectionConsumerKind.Cli,
                "dotnet-inspect CLI",
                "diff -S \"Changed Types\""),
            FindingDiffAnyLibraryCapability.Route,
            exposedQueryTerms: [],
            inputRules:
            [
                "Select -S \"Changed Types\" by itself, with one --package, --platform, or --library pair.",
                "Each endpoint must resolve exactly one Library; a package endpoint compares its implementation assembly.",
                "Both the API and the Body axis are compared.",
            ]);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [Binding]);

    /// <summary>The composed catalog the CLI explains and searches.</summary>
    internal static InspectionCapabilityCatalog Catalog { get; } =
        InspectionCapabilityCatalog.Create(
            [FindingDiffAnyLibraryCapability.ProductModule, Module]);
}
