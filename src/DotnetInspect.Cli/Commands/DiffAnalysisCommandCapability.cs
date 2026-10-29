using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using QuerySpace;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The CLI consumer of Diff's Compare route and its analyses. Help,
/// completion, <c>-D</c>, <c>explain</c>, and <c>diff --analysis</c>
/// dispatch all read <see cref="Catalog"/> and <see cref="CompareBinding"/>.
/// </summary>
internal static class DiffAnalysisCommandCapability
{
    internal const string ModuleIdentity =
        "dotnet-inspect.cli/diff-analysis-capability";

    internal const string CompareBindingIdentity =
        "dotnet-inspect.cli/diff-compare";

    /// <summary>
    /// The <c>diff --analysis</c> command's binding to Diff's Compare
    /// inspection route. It selects every analysis the route's operation can
    /// dispatch; every analysis-set run executes that route through
    /// <c>DiffOperation.ExecuteSurfaceSetAsync</c>.
    /// </summary>
    internal static InspectionConsumerBinding<
        DiffAnalysisInspectionRequest,
        DiffAnalysisDocument> CompareBinding { get; } =
        new(
            new(
                CompareBindingIdentity,
                InspectionConsumerKind.Cli,
                "dotnet-inspect CLI",
                "diff --analysis"),
            DiffAnalysisCatalog.Route,
            exposedQueryTerms: [],
            inputRules:
            [
                "Selecting --analysis string-literals requires exactly one --where predicate.",
                $"Use \"{StringLiteralComparisonQuery.LiteralKey} {RowPredicateSyntaxParser.Comparison(PortableQueryOperator.Contains)} value\""
                    + $" or \"{StringLiteralComparisonQuery.LiteralKey} {RowPredicateSyntaxParser.Comparison(PortableQueryOperator.StartsWith)} value\".",
                $"Supply a non-empty operand of at most {StringLiteralUsePredicate.MaximumLength} UTF-16 characters.",
                "Pass the complete predicate as one shell argument; for example "
                    + $"--where \"{StringLiteralComparisonQuery.LiteralKey} {RowPredicateSyntaxParser.Comparison(PortableQueryOperator.Contains)} https://\".",
            ],
            exposedAnalyses:
            [
                .. DiffAnalysisCatalog.Registrations
                    .Where(static registration =>
                        DiffAnalysisCatalog.Operation.CanDispatch(
                            registration.Analysis))
                    .Select(static registration =>
                        registration.Analysis.Id.Value),
            ]);

    internal static InspectionCapabilityModule Module { get; } =
        new(
            ModuleIdentity,
            bindings: [CompareBinding]);

    /// <summary>The composed catalog Diff validates and dispatches against.</summary>
    internal static InspectionCapabilityCatalog Catalog { get; } =
        InspectionCapabilityCatalog.Create(
            [DiffAnalysisCatalog.ProductModule, Module]);

    /// <summary>The analysis identities <c>diff --analysis</c> selects, in product order.</summary>
    internal static ImmutableArray<string> Identities => CompareBinding.ExposedAnalyses;
}
