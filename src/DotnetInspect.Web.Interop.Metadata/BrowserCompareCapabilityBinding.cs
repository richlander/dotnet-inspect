using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Metadata;

/// <summary>The existing Browser Compare surface's executable analysis subset.</summary>
internal static class BrowserCompareCapabilityBinding
{
    internal const string BindingIdentity = "dotnet-inspect.web/diff-compare";

    internal static bool IsProducerAvailable(AnalysisDeclarationId route) =>
        route == DiffAnalysisCatalog.ApiRoute
        || route == DiffAnalysisCatalog.ApiAttributeRoute
        || route == DiffAnalysisCatalog.StringLiteralRoute;

    internal static InspectionConsumerBinding<DiffAnalysisInspectionRequest,
        DiffAnalysisDocument> Binding { get; } = new(
            new(BindingIdentity, InspectionConsumerKind.Browser,
                "dotnet-inspect Browser", "Compare inspector"),
            DiffAnalysisCatalog.Route,
            exposedQueryTerms: [],
            inputRules:
            [
                "Select an exact package Library, Type, or Member and a comparison version in the Compare inspector.",
                "API-attribute comparison selects Type targets; string-literal comparison selects a Library and requires a Literal predicate.",
                "Body-signal and retained Research comparisons are unavailable in Browser/Wasm.",
            ],
            exposedAnalyses:
            [
                .. DiffAnalysisCatalog.Registrations
                    .Where(static registration =>
                        DiffAnalysisCatalog.Operation.CanDispatch(registration.Analysis)
                        && registration.Analysis.ParticipationFor(AnalysisOperationKind.Compare)!
                            .Surfaces.Any(static surface => IsProducerAvailable(surface.ProducerRoute)))
                    .Select(static registration => registration.Analysis.Id.Value),
            ]);

    internal static InspectionCapabilityModule Module { get; } = new(
        "dotnet-inspect.web/diff-analysis-capability", bindings: [Binding]);

    internal static InspectionCapabilityCatalog Catalog { get; } =
        InspectionCapabilityCatalog.Create([DiffAnalysisCatalog.ProductModule, Module]);
}
