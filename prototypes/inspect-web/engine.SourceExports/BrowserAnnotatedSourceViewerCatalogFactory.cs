using System.Collections.Frozen;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;

namespace InspectWeb.Engine.SourceFacade;

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
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceInvocationDestination[]?
            invocationDestinations = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            destinationUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        ArgumentNullException.ThrowIfNull(document);
        BrowserAnnotatedSourceInvocationDestination[] projectedDestinations =
            invocationDestinations is null
                ? []
                : ValidateInvocationDestinations(document, invocationDestinations);

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
            invocationDestinations is null
                ? destinationUnavailableReason
                    == BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    ? NotProjected
                    : new BrowserAnnotatedSourceCapabilityAvailability(
                        Available: false,
                        destinationUnavailableReason)
                : new BrowserAnnotatedSourceCapabilityAvailability(
                    Available: true,
                    UnavailableReason: null),
            projectedDestinations);
    }

    private static BrowserAnnotatedSourceInvocationDestination[]
        ValidateInvocationDestinations(
            AnnotatedSourceDocument document,
            BrowserAnnotatedSourceInvocationDestination[] destinations)
    {
        var nodeIds = new HashSet<int>();
        var rows =
            new BrowserAnnotatedSourceInvocationDestination[destinations.Length];
        for (int index = 0; index < destinations.Length; index++)
        {
            BrowserAnnotatedSourceInvocationDestination destination =
                destinations[index]
                ?? throw new ArgumentException(
                    "Invocation destination rows cannot be null.",
                    nameof(destinations));
            if (destination.NodeId < 0
                || destination.NodeId >= document.Nodes.Count)
            {
                throw new ArgumentException(
                    $"Invocation destination node {destination.NodeId} does not exist.",
                    nameof(destinations));
            }
            AnnotatedSourceNode node = document.Nodes[destination.NodeId];
            if (node.Medium != SourceLineKind.CSharp
                || !string.Equals(
                    node.Kind,
                    "InvocationExpression",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Invocation destination node {destination.NodeId} is not a C# invocation.",
                    nameof(destinations));
            }
            if (!nodeIds.Add(destination.NodeId))
            {
                throw new ArgumentException(
                    $"Invocation destination node {destination.NodeId} is duplicated.",
                    nameof(destinations));
            }
            ArgumentNullException.ThrowIfNull(destination.Target);
            rows[index] = destination;
        }
        return rows;
    }

    private static bool IsDefaultFindingCategory(string category) =>
        DefaultFindingCategories.Any(
            defaultCategory =>
                string.Equals(
                    category,
                    defaultCategory.ToString(),
                    StringComparison.Ordinal));
}
