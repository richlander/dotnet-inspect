using DotnetInspector.Diff;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The CLI consumers of Finding Diff Any's routes: the exact-name
/// <c>diff -S "Changed Types"</c> section runs the Library route over the
/// request's one Library pair, and <c>diff -S "Changed Members"</c> runs the
/// Type route for its one <c>--type</c>.
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

    internal const string TypeBindingIdentity =
        "dotnet-inspect.cli/finding-diff-any-members";

    /// <summary>
    /// The CLI consumer of the Type route: the exact-name
    /// <c>diff -S "Changed Members"</c> section with one <c>--type</c>.
    /// </summary>
    internal static InspectionConsumerBinding<
        FindingDiffAnyTypeRequest,
        FindingDiffAnyTypeDocument> TypeBinding { get; } =
        new(
            new(
                TypeBindingIdentity,
                InspectionConsumerKind.Cli,
                "dotnet-inspect CLI",
                "diff -S \"Changed Members\" --type"),
            FindingDiffAnyTypeCapability.Route,
            exposedQueryTerms: [],
            inputRules:
            [
                "Select -S \"Changed Members\" by itself, with exactly one --type and one --package, --platform, or --library pair.",
                "--type is the exact escaped definition name, with nested Types spelled Outer+Inner.",
                "Each endpoint must resolve exactly one Library; a package endpoint compares its implementation assembly.",
                "Both the API and the Body axis are compared.",
            ]);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [Binding, TypeBinding]);

    /// <summary>The composed catalog the CLI explains and searches.</summary>
    internal static InspectionCapabilityCatalog Catalog { get; } =
        InspectionCapabilityCatalog.Create(
            [FindingDiffAnyLibraryCapability.ProductModule, Module]);
}
