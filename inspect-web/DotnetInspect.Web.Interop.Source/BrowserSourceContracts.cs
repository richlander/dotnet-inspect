using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using InertText;
using Inspector.Findings;
using ILInspector.Research;

namespace DotnetInspect.Web.Interop.Source;

/// <summary>
/// The source facade's browser wire contract.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>DotnetInspect.Web.Interop.Source</c>. The annotated-source document embeds a call-graph
/// target, and this facade declares its own transport for it rather than importing the call-graph
/// facade's; <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </remarks>
public sealed record BrowserSource(
    string Provider,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Provenance,
    string? Url,
    string? PdbSourceLimitation,
    string Text);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserAnnotatedSourceMedium>))]
public enum BrowserAnnotatedSourceMedium
{
    CSharp,
    Il,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserAnnotatedSourceCapabilityUnavailableReason>))]
public enum BrowserAnnotatedSourceCapabilityUnavailableReason
{
    NotProjected,
    ContextUnavailable,
}

public sealed record BrowserAnnotatedSourceCapabilityAvailability
{
    public BrowserAnnotatedSourceCapabilityAvailability(
        bool Available,
        BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason)
    {
        if (Available == (UnavailableReason is not null))
        {
            throw new ArgumentException(
                Available
                    ? "An available capability cannot carry an unavailable reason."
                    : "An unavailable capability must carry an unavailable reason.",
                nameof(UnavailableReason));
        }

        this.Available = Available;
        this.UnavailableReason = UnavailableReason;
    }

    public bool Available { get; }
    public BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason { get; }
}

public sealed record BrowserAnnotatedSourceViewerCatalog
{
    private readonly int[] _defaultFindingIds;
    private readonly BrowserAnnotatedSourceMedium[] _supportedMedia;
    private readonly string[] _invocationLikeNodeKinds;
    private readonly BrowserAnnotatedSourceInvocationDestination[]
        _invocationDestinations;

    public BrowserAnnotatedSourceViewerCatalog(
        int[] DefaultFindingIds,
        BrowserAnnotatedSourceMedium[] SupportedMedia,
        string[] InvocationLikeNodeKinds,
        BrowserAnnotatedSourceCapabilityAvailability FindingEvidence,
        BrowserAnnotatedSourceCapabilityAvailability Destinations,
        BrowserAnnotatedSourceInvocationDestination[] InvocationDestinations)
    {
        ArgumentNullException.ThrowIfNull(DefaultFindingIds);
        ArgumentNullException.ThrowIfNull(SupportedMedia);
        ArgumentNullException.ThrowIfNull(InvocationLikeNodeKinds);
        ArgumentNullException.ThrowIfNull(FindingEvidence);
        ArgumentNullException.ThrowIfNull(Destinations);
        ArgumentNullException.ThrowIfNull(InvocationDestinations);
        if (!Destinations.Available && InvocationDestinations.Length > 0)
        {
            throw new ArgumentException(
                "Unavailable destinations cannot carry projected rows.",
                nameof(InvocationDestinations));
        }

        _defaultFindingIds = [.. DefaultFindingIds];
        _supportedMedia = [.. SupportedMedia];
        _invocationLikeNodeKinds = [.. InvocationLikeNodeKinds];
        _invocationDestinations = [.. InvocationDestinations];
        this.FindingEvidence = FindingEvidence;
        this.Destinations = Destinations;
    }

    public int[] DefaultFindingIds => [.. _defaultFindingIds];
    public BrowserAnnotatedSourceMedium[] SupportedMedia => [.. _supportedMedia];
    public string[] InvocationLikeNodeKinds => [.. _invocationLikeNodeKinds];
    public BrowserAnnotatedSourceInvocationDestination[] InvocationDestinations =>
        [.. _invocationDestinations];
    public BrowserAnnotatedSourceCapabilityAvailability FindingEvidence { get; }
    public BrowserAnnotatedSourceCapabilityAvailability Destinations { get; }
}

public sealed record BrowserAnnotatedSourceInvocationDestination(
    int NodeId,
    BrowserCallGraphTarget Target);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCalleeEvidenceKind>))]
public enum BrowserCalleeEvidenceKind
{
    ExceptionConstruction,
    Localloc,
    Calli,
}

public sealed record BrowserAnnotatedSourceFindingEvidenceCoordinate(
    int IlOffset,
    BrowserCalleeEvidenceKind Kind);

/// <summary>
/// One exact caller Finding joined to method-qualified evidence in its physical callee.
/// </summary>
public sealed record BrowserAnnotatedSourceFindingEvidence(
    int FactId,
    int InstanceKey,
    string Member,
    BrowserCallGraphTarget Target,
    BrowserAnnotatedSourceFindingEvidenceCoordinate[] Coordinates,
    JsonElement? Document,
    int[] NodeIds,
    string? UnavailableReason);

public sealed record BrowserMemberFindingFact(
    string Member,
    int? IlOffset,
    int? CSharpLine,
    string Anchor,
    string Category,
    string Id,
    string? Detail,
    string Conditionality,
    int? InstanceKey);

public sealed record BrowserSourceFactInstance(
    int FactId,
    int InstanceKey);

/// <summary>
/// One Research-issued member Finding census transported across its Facts and Annotated Source
/// projections. The receipt scopes every non-null fact-row key and every source fact instance.
/// </summary>
public sealed record BrowserMemberFindingCensus
{
    private BrowserMemberFindingCensus(
        string FactCensusReceipt,
        BrowserMemberFindingFact[] Facts,
        BrowserAnnotatedSource AnnotatedSource,
        BrowserSourceFactInstance[] SourceFactInstances)
    {
        this.FactCensusReceipt = FactCensusReceipt;
        this.Facts = Facts;
        this.AnnotatedSource = AnnotatedSource;
        this.SourceFactInstances = SourceFactInstances;
    }

    public string FactCensusReceipt { get; }
    public BrowserMemberFindingFact[] Facts { get; }
    public BrowserAnnotatedSource AnnotatedSource { get; }
    public BrowserSourceFactInstance[] SourceFactInstances { get; }

    internal static BrowserMemberFindingCensus Create(
        FindingCensusReceipt? receipt,
        IReadOnlyList<ResearchViews.FactRow>? facts,
        AnnotatedSourceDocument document,
        IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>? sourceFactIdentities,
        InertString provenance,
        string? contextLimitation,
        BrowserAnnotatedSourceInvocationDestination[]?
            invocationDestinations = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            destinationUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceFindingEvidence[]?
            findingEvidence = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            findingEvidenceUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        if (receipt is not { IsDefault: false } censusReceipt)
            throw new InvalidOperationException(
                "Member Finding census produced no non-default receipt.");
        if (facts is null)
            throw new InvalidOperationException(
                "Member Finding census produced no Facts projection.");
        ArgumentNullException.ThrowIfNull(document);
        if (sourceFactIdentities is null)
        {
            throw new InvalidOperationException(
                "Member Finding census produced no Annotated Source identity sidecar.");
        }

        var factKeys = new HashSet<int>();
        var projectedFacts = new BrowserMemberFindingFact[facts.Count];
        for (int index = 0; index < facts.Count; index++)
        {
            ResearchViews.FactRow fact = facts[index];
            bool hasReceipt = fact.CensusReceipt is not null;
            bool hasKey = fact.InstanceKey is not null;
            if (hasReceipt != hasKey)
            {
                throw new InvalidOperationException(
                    $"Member Finding census Facts row {index} carries an incomplete identity.");
            }

            int? keyValue = null;
            if (fact.CensusReceipt is { } factReceipt
                && fact.InstanceKey is { } factKey)
            {
                if (factReceipt != censusReceipt)
                {
                    throw new InvalidOperationException(
                        $"Member Finding census Facts row {index} carries a different receipt.");
                }
                if (factKey.IsDefault || !factKeys.Add(factKey.Value))
                {
                    throw new InvalidOperationException(
                        $"Member Finding census Facts row {index} carries an invalid or duplicate instance key.");
                }
                keyValue = factKey.Value;
            }

            projectedFacts[index] = new BrowserMemberFindingFact(
                fact.Member,
                fact.ILOffset,
                fact.CSharpLine,
                fact.Anchor,
                fact.Category,
                fact.Id,
                fact.Detail,
                fact.Conditionality,
                keyValue);
        }

        var bodyFactIds = document.Facts
            .Where(static fact => fact.Origin == AnnotatedSourceFactOrigin.Body)
            .Select(static fact => fact.Id)
            .ToHashSet();
        var sourceFactIds = new HashSet<int>();
        var sourceKeys = new HashSet<int>();
        var projectedIdentities =
            new BrowserSourceFactInstance[sourceFactIdentities.Count];
        for (int index = 0; index < sourceFactIdentities.Count; index++)
        {
            ResearchViews.AnnotatedSourceFactIdentity identity =
                sourceFactIdentities[index];
            if (identity.CensusReceipt != censusReceipt)
            {
                throw new InvalidOperationException(
                    $"Member Finding census source identity {index} carries a different receipt.");
            }
            if (identity.InstanceKey.IsDefault
                || !sourceKeys.Add(identity.InstanceKey.Value))
            {
                throw new InvalidOperationException(
                    $"Member Finding census source identity {index} carries an invalid or duplicate instance key.");
            }
            if (!sourceFactIds.Add(identity.FactId)
                || !bodyFactIds.Contains(identity.FactId))
            {
                throw new InvalidOperationException(
                    $"Member Finding census source identity {index} carries an invalid or duplicate fact id.");
            }

            projectedIdentities[index] = new BrowserSourceFactInstance(
                identity.FactId,
                identity.InstanceKey.Value);
        }

        if (!bodyFactIds.SetEquals(sourceFactIds))
        {
            throw new InvalidOperationException(
                "Member Finding census source identities do not cover the document body facts.");
        }
        if (!factKeys.SetEquals(sourceKeys))
        {
            throw new InvalidOperationException(
                "Member Finding census Facts and Annotated Source identities do not describe the same instances.");
        }
        ValidateFindingEvidence(
            document,
            projectedIdentities,
            findingEvidence);

        return new BrowserMemberFindingCensus(
            censusReceipt.ToString(),
            projectedFacts,
            BrowserAnnotatedSource.Create(
                document,
                provenance,
                contextLimitation,
                invocationDestinations,
                destinationUnavailableReason,
                findingEvidence,
                findingEvidenceUnavailableReason),
            projectedIdentities);
    }

    static void ValidateFindingEvidence(
        AnnotatedSourceDocument document,
        IReadOnlyList<BrowserSourceFactInstance> sourceFactInstances,
        IReadOnlyList<BrowserAnnotatedSourceFindingEvidence>? findingEvidence)
    {
        if (findingEvidence is null)
            return;

        Dictionary<int, int> instanceKeyByFactId =
            sourceFactInstances.ToDictionary(
                identity => identity.FactId,
                identity => identity.InstanceKey);
        HashSet<int> eligibleFactIds =
        [
            .. document.Facts
                .Where(fact =>
                    fact.Origin == AnnotatedSourceFactOrigin.Body
                    && fact.Descriptor is
                        "semantics.callee" or "safety.callee")
                .Select(fact => fact.Id),
        ];
        var evidenceFactIds = new HashSet<int>();
        var evidenceKeys = new HashSet<int>();
        for (int index = 0; index < findingEvidence.Count; index++)
        {
            BrowserAnnotatedSourceFindingEvidence evidence =
                findingEvidence[index]
                    ?? throw new InvalidOperationException(
                        $"Member Finding census evidence row {index} is null.");
            if (!eligibleFactIds.Contains(evidence.FactId)
                || !instanceKeyByFactId.TryGetValue(
                    evidence.FactId,
                    out int expectedKey)
                || expectedKey != evidence.InstanceKey
                || !evidenceFactIds.Add(evidence.FactId)
                || !evidenceKeys.Add(evidence.InstanceKey))
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} carries an invalid or duplicate fact identity.");
            }
            if (string.IsNullOrWhiteSpace(evidence.Member)
                || evidence.Target is not { } target
                || string.IsNullOrWhiteSpace(target.Assembly)
                || string.IsNullOrWhiteSpace(target.TypeFullName)
                || string.IsNullOrWhiteSpace(target.TypeDefinitionId)
                || string.IsNullOrWhiteSpace(target.MemberName)
                || string.IsNullOrWhiteSpace(target.ReturnType)
                || string.IsNullOrWhiteSpace(target.SelectorKey)
                || target.ParameterTypes is null
                || target.GenericArity < 0
                || target.MetadataToken is not int token
                || (token & 0xFF000000) != 0x06000000
                || !string.Equals(
                    target.Kind,
                    "method",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} carries no callee member target.");
            }
            if (evidence.Coordinates is null
                || evidence.NodeIds is null
                || evidence.Coordinates.Any(coordinate =>
                    coordinate is null || coordinate.IlOffset < 0)
                || evidence.NodeIds.Any(nodeId => nodeId < 0)
                || evidence.NodeIds.Distinct().Count()
                    != evidence.NodeIds.Length)
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} carries invalid coordinates or node ids.");
            }

            bool unavailable =
                !string.IsNullOrWhiteSpace(evidence.UnavailableReason);
            AnnotatedSourceDocument? evidenceDocument =
                evidence.Document is { } serializedDocument
                    ? DeserializeDocument(serializedDocument, index)
                    : null;
            if (unavailable)
            {
                if (evidence.NodeIds.Length != 0)
                {
                    throw new InvalidOperationException(
                        $"Unavailable member Finding evidence row {index} cannot carry node ids.");
                }
                if (evidenceDocument is not null
                    && evidence.Coordinates.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Unavailable member Finding evidence row {index} cannot carry a document without coordinates.");
                }
                if (evidenceDocument is not null
                    && FindEvidenceNodeIds(
                        evidenceDocument,
                        evidence,
                        index,
                        out _) is not null)
                {
                    throw new InvalidOperationException(
                        $"Member Finding census evidence row {index} is unavailable despite exact serialized correspondence.");
                }
            }
            else if (evidenceDocument is null
                || evidence.Coordinates.Length == 0
                || evidence.NodeIds.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Available member Finding evidence row {index} requires a document, coordinates, and node ids.");
            }
            else
            {
                int[] expectedNodeIds =
                    FindEvidenceNodeIds(
                    evidenceDocument,
                    evidence,
                    index,
                    out string? failure)
                    ?? throw new InvalidOperationException(failure);
                if (!evidence.NodeIds.SequenceEqual(expectedNodeIds))
                {
                    throw new InvalidOperationException(
                        $"Member Finding census evidence row {index} node ids "
                            + "do not equal its exact coordinate matches.");
                }
            }
        }

        if (!eligibleFactIds.SetEquals(evidenceFactIds))
        {
            throw new InvalidOperationException(
                "Member Finding census evidence does not cover every instruction-level callee Finding.");
        }
    }

    static AnnotatedSourceDocument DeserializeDocument(
        JsonElement document,
        int evidenceIndex) =>
        document.Deserialize(
            AnnotatedSourceDocumentCompactJsonContext.Default
                .AnnotatedSourceDocument)
        ?? throw new InvalidOperationException(
            $"Member Finding census evidence row {evidenceIndex} carries no callee document.");

    static int[]? FindEvidenceNodeIds(
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceFindingEvidence evidence,
        int evidenceIndex,
        out string? failure)
    {
        var matchedNodeIds = new List<int>();
        foreach (
            BrowserAnnotatedSourceFindingEvidenceCoordinate coordinate
            in evidence.Coordinates)
        {
            string expectedKind = coordinate.Kind switch
            {
                BrowserCalleeEvidenceKind.ExceptionConstruction =>
                    "ObjectCreationExpression",
                BrowserCalleeEvidenceKind.Localloc =>
                    "StackAllocationExpression",
                BrowserCalleeEvidenceKind.Calli =>
                    "IndirectInvocationExpression",
                _ => throw new InvalidOperationException(
                    $"Member Finding census evidence row {evidenceIndex} carries an unknown evidence kind."),
            };
            AnnotatedSourceNode[] matches =
            [
                .. document.Nodes.Where(node =>
                    node.Medium == SourceLineKind.CSharp
                    && string.Equals(
                        node.Kind,
                        expectedKind,
                        StringComparison.Ordinal)
                    && node.Provenance?.IlOffsets.Contains(
                        coordinate.IlOffset) == true),
            ];
            if (matches.Length != 1)
            {
                failure =
                    $"Member Finding census evidence row {evidenceIndex} coordinate "
                        + $"IL_{coordinate.IlOffset:X4} matches {matches.Length} "
                        + $"{expectedKind} nodes.";
                return null;
            }
            matchedNodeIds.Add(matches[0].Id);
        }

        failure = null;
        return
        [
            .. matchedNodeIds.Distinct().Order(),
        ];
    }
}

/// <summary>
/// One call-graph target reached from an annotated-source invocation node. The identity is
/// produced by the product's call-graph projection and carried verbatim; this facade owns only
/// the transport record.
/// </summary>
public sealed record BrowserCallGraphTarget(
    string Id,
    string Assembly,
    string? AssemblyVersion,
    string? AssemblyCulture,
    string? AssemblyPublicKeyToken,
    string TypeFullName,
    string? TypeMetadataId,
    string? TypeDefinitionId,
    string MemberName,
    string[] ParameterTypes,
    string ReturnType,
    int GenericArity,
    int? MetadataToken,
    string SelectorKey,
    string Kind,
    string? PlatformPack,
    string? SurfaceAssemblyId);

/// <summary>
/// The annotated-source envelope: the product's portable <c>AnnotatedSourceDocument</c> serialized
/// by its owning <c>AnnotatedSourceDocumentJsonContext</c>, the product-issued viewer catalog, and
/// the provenance of the artifact it was raised from. The document travels as a
/// <see cref="JsonElement"/> so the wire shape stays exactly the one the viewer's model validates —
/// the host neither reshapes nor renames a field.
/// </summary>
/// <param name="ContextLimitation">
/// Set when the projection's whole-assembly fact context was narrower than a complete one, so a
/// short fact list is never mistaken for an honest absence of facts.
/// </param>
public sealed record BrowserAnnotatedSource
{
    private readonly BrowserAnnotatedSourceFindingEvidence[]
        _findingEvidence;

    private BrowserAnnotatedSource(
        JsonElement Document,
        BrowserAnnotatedSourceViewerCatalog ViewerCatalog,
        InertString Provenance,
        string? ContextLimitation,
        BrowserAnnotatedSourceFindingEvidence[] FindingEvidence)
    {
        this.Document = Document;
        this.ViewerCatalog = ViewerCatalog;
        this.Provenance = Provenance;
        this.ContextLimitation = ContextLimitation;
        _findingEvidence = [.. FindingEvidence];
    }

    public JsonElement Document { get; }
    public BrowserAnnotatedSourceViewerCatalog ViewerCatalog { get; }
    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Provenance { get; }
    public string? ContextLimitation { get; }
    public BrowserAnnotatedSourceFindingEvidence[] FindingEvidence =>
        [.. _findingEvidence];

    internal static BrowserAnnotatedSource Create(
        AnnotatedSourceDocument document,
        InertString provenance,
        string? contextLimitation,
        BrowserAnnotatedSourceInvocationDestination[]?
            invocationDestinations = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            destinationUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceFindingEvidence[]?
            findingEvidence = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            findingEvidenceUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            provenance.ToString(),
            nameof(provenance));

        JsonElement serialized = SerializeDocument(document)!.Value;
        return new BrowserAnnotatedSource(
            serialized,
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                invocationDestinations,
                destinationUnavailableReason,
                findingEvidence,
                findingEvidenceUnavailableReason),
            provenance,
            contextLimitation,
            findingEvidence ?? []);
    }

    internal static JsonElement? SerializeDocument(
        AnnotatedSourceDocument? document)
    {
        if (document is null)
            return null;
        using JsonDocument serialized = JsonDocument.Parse(
            JsonSerializer.Serialize(
                document,
                AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument));
        return serialized.RootElement.Clone();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserSource))]
[JsonSerializable(typeof(BrowserTypeSourceResult))]
[JsonSerializable(typeof(BrowserTypeSourceCancellation))]
[JsonSerializable(typeof(BrowserMethodBodyTargetsResult))]
[JsonSerializable(typeof(BrowserMethodBodyComparisonResult))]
[JsonSerializable(typeof(BrowserMethodBodyComparisonRequest))]
[JsonSerializable(typeof(BrowserSourceComparisonRequest))]
[JsonSerializable(typeof(BrowserSourceComparisonResult))]
[JsonSerializable(typeof(BrowserAnnotatedSource))]
[JsonSerializable(typeof(BrowserMemberFindingCensus))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserSourceJsonContext : JsonSerializerContext;
