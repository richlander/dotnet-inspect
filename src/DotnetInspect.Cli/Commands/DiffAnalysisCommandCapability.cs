using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The CLI consumer of Diff's analysis participation registrations. Help,
/// completion, <c>-D</c>, <c>explain</c>, and <c>diff --analysis</c>
/// dispatch all read <see cref="Catalog"/>.
/// </summary>
internal static class DiffAnalysisCommandCapability
{
    internal const string BindingIdentity =
        "dotnet-inspect.cli/diff-analysis";
    internal const string ModuleIdentity =
        "dotnet-inspect.cli/diff-analysis-capability";

    internal static InspectionAnalysisConsumerBinding Binding { get; } =
        new(
            new(
                BindingIdentity,
                InspectionConsumerKind.Cli,
                "dotnet-inspect CLI",
                "diff --analysis"),
            DiffAnalysisCatalog.Operation);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            analysisBindings: [Binding]);

    /// <summary>The composed catalog Diff validates and dispatches against.</summary>
    internal static InspectionCapabilityCatalog Catalog { get; } =
        InspectionCapabilityCatalog.Create(
            [DiffAnalysisCatalog.ProductModule, Module]);

    /// <summary>Compare-participating analysis identities, in product order.</summary>
    internal static ImmutableArray<string> Identities { get; } =
    [
        .. Catalog.Analyses
            .Where(static registration =>
                registration.Analysis.ParticipationFor(
                    AnalysisOperationKind.Compare) is not null)
            .Select(static registration => registration.Analysis.Id.Value),
    ];
}
