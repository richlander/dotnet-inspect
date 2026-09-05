using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;

namespace InspectWeb.Engine.SourceFacade;

/// <summary>
/// The source facade's browser wire contract.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>InspectWeb.Engine.SourceExports</c>. The annotated-source document embeds a call-graph
/// target, and this facade declares its own transport for it rather than importing the call-graph
/// facade's; <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </remarks>
public sealed record BrowserSource(
    string Provider,
    string Provenance,
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
    private BrowserAnnotatedSource(
        JsonElement Document,
        BrowserAnnotatedSourceViewerCatalog ViewerCatalog,
        string Provenance,
        string? ContextLimitation)
    {
        this.Document = Document;
        this.ViewerCatalog = ViewerCatalog;
        this.Provenance = Provenance;
        this.ContextLimitation = ContextLimitation;
    }

    public JsonElement Document { get; }
    public BrowserAnnotatedSourceViewerCatalog ViewerCatalog { get; }
    public string Provenance { get; }
    public string? ContextLimitation { get; }

    internal static BrowserAnnotatedSource Create(
        AnnotatedSourceDocument document,
        string provenance,
        string? contextLimitation,
        BrowserAnnotatedSourceInvocationDestination[]?
            invocationDestinations = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            destinationUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);

        using JsonDocument serialized = JsonDocument.Parse(
            JsonSerializer.Serialize(
                document,
                AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument));
        return new BrowserAnnotatedSource(
            serialized.RootElement.Clone(),
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                invocationDestinations,
                destinationUnavailableReason),
            provenance,
            contextLimitation);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserSource))]
[JsonSerializable(typeof(BrowserTypeSourceResult))]
[JsonSerializable(typeof(BrowserTypeSourceCancellation))]
[JsonSerializable(typeof(BrowserAnnotatedSource))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserSourceJsonContext : JsonSerializerContext;
