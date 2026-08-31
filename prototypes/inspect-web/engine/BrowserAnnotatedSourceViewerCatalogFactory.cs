using System.Collections.Frozen;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;

namespace InspectWeb.Engine;

internal static class BrowserAnnotatedSourceViewerCatalogFactory
{
    internal static readonly IReadOnlySet<AnnotationCategory> DefaultFindingCategories =
        new[]
        {
            AnnotationCategory.Allocation,
            AnnotationCategory.Unsafety,
            AnnotationCategory.Cost,
            AnnotationCategory.Semantics,
            AnnotationCategory.Lifetime,
        }.ToFrozenSet();

    private static readonly BrowserAnnotatedSourceCapabilityAvailability NotProjected =
        new(
            Available: false,
            UnavailableReason:
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected);

    // Call-shaped syntax wins hit testing independently of whether a destination is available.
    private static readonly string[] InvocationLikeNodeKinds =
    [
        "InvocationExpression",
        "IndirectInvocationExpression",
        "ObjectCreationExpression",
        "DelegateCreationExpression",
    ];

    public static BrowserAnnotatedSourceViewerCatalog Create(
        AnnotatedSourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var targetedFacts = new bool[document.Facts.Count];
        foreach (AnnotatedSourceTarget target in document.Targets)
            targetedFacts[target.FactId] = true;

        int[] defaultFindingIds =
        [
            .. document.Facts
                .Where(fact =>
                    targetedFacts[fact.Id]
                        && IsDefaultFindingCategory(fact.Category))
                .Select(fact => fact.Id),
        ];
        BrowserAnnotatedSourceMedium[] supportedMedia =
            document.Nodes.Any(node => node.Medium == SourceLineKind.Il)
                ?
                [
                    BrowserAnnotatedSourceMedium.CSharp,
                    BrowserAnnotatedSourceMedium.Il,
                ]
                : [BrowserAnnotatedSourceMedium.CSharp];
        string[] invocationLikeNodeKinds =
        [
            .. InvocationLikeNodeKinds.Where(kind =>
                document.Nodes.Any(node =>
                    node.Medium == SourceLineKind.CSharp
                        && string.Equals(node.Kind, kind, StringComparison.Ordinal))),
        ];

        return new BrowserAnnotatedSourceViewerCatalog(
            defaultFindingIds,
            supportedMedia,
            invocationLikeNodeKinds,
            NotProjected,
            NotProjected);
    }

    private static bool IsDefaultFindingCategory(string category) =>
        DefaultFindingCategories.Any(
            defaultCategory =>
                string.Equals(
                    category,
                    defaultCategory.ToString(),
                    StringComparison.Ordinal));
}
