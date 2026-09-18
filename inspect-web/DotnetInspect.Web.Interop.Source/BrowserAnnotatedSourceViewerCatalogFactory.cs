using System.Collections.Frozen;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Research;

namespace DotnetInspect.Web.Interop.Source;

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
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceFindingEvidence[]?
            findingEvidence = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            findingEvidenceUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceCallRelationship[]?
            callRelationships = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            callRelationshipsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        ArgumentNullException.ThrowIfNull(document);
        BrowserAnnotatedSourceInvocationDestination[] projectedDestinations =
            invocationDestinations is null
                ? []
                : ValidateInvocationDestinations(document, invocationDestinations);
        if (callRelationships is not null)
            ValidateCallRelationships(document, callRelationships);

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
            findingEvidence is null
                ? findingEvidenceUnavailableReason
                    == BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    ? NotProjected
                    : new BrowserAnnotatedSourceCapabilityAvailability(
                        Available: false,
                        findingEvidenceUnavailableReason)
                : new BrowserAnnotatedSourceCapabilityAvailability(
                    Available: true,
                    UnavailableReason: null),
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
            callRelationships is null
                ? callRelationshipsUnavailableReason
                    == BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    ? NotProjected
                    : new BrowserAnnotatedSourceCapabilityAvailability(
                        Available: false,
                        callRelationshipsUnavailableReason)
                : new BrowserAnnotatedSourceCapabilityAvailability(
                    Available: true,
                    UnavailableReason: null),
            projectedDestinations);
    }

    private static void ValidateCallRelationships(
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceCallRelationship[] relationships)
    {
        var factIds = new HashSet<int>();
        var physicalOccurrences =
            new HashSet<(Guid ModuleVersionId, int CallerToken, int IlOffset, int OperandToken)>();
        foreach ((BrowserAnnotatedSourceCallRelationship relationship, int index)
            in relationships.Select((relationship, index) =>
                (relationship, index)))
        {
            if (relationship is null)
            {
                throw new ArgumentException(
                    $"Call relationship row {index} is null.",
                    nameof(relationships));
            }
            if (relationship.EdgeRow < 1
                || relationship.FactId < 0
                || relationship.FactId >= document.Facts.Count
                || relationship.ModuleVersionId == Guid.Empty
                || (relationship.CallerToken & 0xFF000000) != 0x06000000
                || relationship.IlOffset < 0
                || relationship.OperandToken <= 0
                || !Enum.IsDefined(relationship.Kind)
                || !physicalOccurrences.Add((
                    relationship.ModuleVersionId,
                    relationship.CallerToken,
                    relationship.IlOffset,
                    relationship.OperandToken))
                || !factIds.Add(relationship.FactId))
            {
                throw new ArgumentException(
                    $"Call relationship row {index} has invalid or duplicate identity.",
                    nameof(relationships));
            }

            AnnotatedSourceFact fact = document.Facts[relationship.FactId];
            if (fact.Descriptor
                    != ResearchFactRegistry.CallRelationshipDescriptorId
                || fact.Origin != AnnotatedSourceFactOrigin.Body
                || fact.SourceOffset != relationship.IlOffset
                || !document.Targets.Any(target =>
                    target.FactId == relationship.FactId))
            {
                throw new ArgumentException(
                    $"Call relationship row {index} does not name one targeted call.edge fact.",
                    nameof(relationships));
            }
            ArgumentNullException.ThrowIfNull(relationship.Target);
        }
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
