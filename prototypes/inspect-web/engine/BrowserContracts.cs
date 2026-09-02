using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;

namespace InspectWeb.Engine;

/// <summary>
/// One package's browsable surface for one exact package/version/framework workspace, adapted
/// from <c>AssemblyContextApiSurfaceQuery</c>. Every classification, label, order, and default in
/// <see cref="Accessibility"/> comes from the product's own
/// <c>ApiAccessibilityBucket</c> values; the host restates none of them.
/// </summary>
/// <param name="InspectionErrors">
/// The whole entries rendered into <paramref name="InspectionError"/>. The browser uses these
/// product-owned boundaries when cumulative platform loads deduplicate notices. This transport
/// pairing is gated by
/// <c>BrowserEngineBoundaryTests.QueryPackage_FirstTransportTruncationReturnsTypedNotice</c>;
/// browser accumulation is gated by <c>platform inspection notices survive cumulative surface
/// loads</c>.
/// </param>
/// <param name="InspectionError">
/// The participants the workspace could not project, if any. A partial surface says so rather
/// than reading as a complete one.
/// </param>
public sealed record BrowserCompileLibraryAvailability(
    BrowserCompileLibraryStatus Status,
    string? TargetFramework,
    string? Message);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCompileLibraryStatus>))]
public enum BrowserCompileLibraryStatus
{
    Selected,
    NoCompileAssets,
    NoMatchingTargetFramework,
    EmptyCompileGroup,
    InvalidImplementationAssets,
}

public sealed record BrowserPackageSurface(
    string Package,
    string Version,
    string[] Frameworks,
    string ActiveFramework,
    BrowserPackageIcon? Icon,
    string? DefaultAssemblyId,
    BrowserCompileLibraryAvailability CompileLibrary,
    BrowserAssemblySurface[] Assemblies,
    BrowserTypeSurface[] Types,
    BrowserAccessibilityDescriptor[] Accessibility,
    int TotalMembers,
    BrowserPackageDocument[] Documents,
    string[] InspectionErrors,
    string? InspectionError);

/// <summary>
/// One bounded embedded package icon. <see cref="Base64"/> contains only bytes admitted by
/// <c>PackageIconQuery</c>; the Browser host never transports the deprecated remote icon URL.
/// </summary>
public sealed record BrowserPackageIcon(
    string MediaType,
    string Base64);

/// <summary>
/// One product-owned accessibility bucket, carried verbatim from
/// <c>DotnetInspector.Queries.ApiAccessibilityBucket</c>.
/// </summary>
public sealed record BrowserAccessibilityDescriptor(
    string Id,
    string Label,
    int Order,
    bool IsDefault,
    int Count);

public sealed record BrowserAssemblySurface(
    string Id,
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken,
    string Asset,
    int PublicTypes,
    int PublicMembers,
    string? PlatformPack);

/// <summary>
/// One type row. <see cref="Id"/> is the browser key, <see cref="DefinitionId"/> is the escaped
/// structured metadata identity, <see cref="QueryId"/> is the identity a Research projection is asked for, and
/// <see cref="MetadataId"/> is the exact metadata lookup name (nested types delimited by
/// <c>+</c>). <see cref="Assembly"/> is the selected package asset used for engine requests,
/// <see cref="AssemblyId"/> joins that asset to its descriptor, and <see cref="AssemblyName"/> is
/// its metadata identity. They are separate because none is interchangeable with another or with
/// display text.
/// </summary>
public sealed record BrowserTypeSurface(
    string Id,
    string DefinitionId,
    string QueryId,
    string MetadataId,
    string Name,
    string DisplayName,
    string Namespace,
    string Kind,
    string Accessibility,
    string AccessibilityId,
    string Assembly,
    string AssemblyId,
    string AssemblyName,
    int Members,
    string Signature,
    BrowserMemberSurface[] Api,
    string? PlatformPack);

/// <summary>
/// One member overload. <see cref="StableSelector"/>, <see cref="AnchorDigest"/>, and
/// <see cref="CanonicalSignature"/> are the product's member anchor; <see cref="GraphSelectorKey"/>
/// and <see cref="BodySelectors"/> are the product's opaque call-graph correspondence. The host
/// transports them and never parses them.
/// </summary>
public sealed record BrowserMemberSurface(
    string Name,
    string Kind,
    string Signature,
    string Accessibility,
    bool IsStatic,
    bool IsUnsafe,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsExtension,
    bool IsObsolete,
    int GenericArity,
    int? MetadataToken,
    string? ReturnType,
    BrowserParameterSurface[] Parameters,
    string? DocumentationId,
    string? Summary,
    string? Returns,
    BrowserExceptionSurface[] Exceptions,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string GraphSelectorKey,
    BrowserMemberBodySelector[] BodySelectors);

public sealed record BrowserMemberBodySelector(
    int Token,
    string MemberName,
    string SelectorKey);

/// <summary>
/// The owning API member and exact physical body selected by a graph query.
/// <c>MemberFacts_DistinguishesSurfaceAndBodyTokenResolution</c> gates this provenance.
/// </summary>
public sealed record BrowserGraphMemberSurface(
    BrowserTypeSurface Type,
    BrowserMemberBodySelector SelectedBody);

public sealed record BrowserParameterSurface(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue,
    string? Description);

/// <summary>
/// A browsable Markdown document shipped inside a package. The manifest carries presence and size
/// only; the body is fetched on demand so the surface payload stays small.
/// </summary>
public sealed record BrowserPackageDocument(
    string Kind,
    string Name,
    string Path,
    int Size);

public sealed record BrowserPackageDocumentContent(
    string Kind,
    string Name,
    string Path,
    string Text);

public sealed record BrowserExceptionSurface(
    string Type,
    string Description);

public sealed record BrowserMemberDocumentation(
    string? Summary,
    string? Returns,
    IReadOnlyDictionary<string, string> Parameters,
    BrowserExceptionSurface[] Exceptions);

public sealed record BrowserTypeCandidate(
    string Key,
    string Name,
    string Full);

public sealed record BrowserTypeSearchHit(
    string Key,
    string Kind);

public sealed record BrowserPackageCacheStats(
    int Packages,
    int Resident,
    int Workspaces,
    long ResidentBytes);

public sealed record BrowserBuildIdentity(
    string Version,
    string? Commit,
    string? BuiltAtUtc,
    string? CommitUrl);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryFacetTier>))]
public enum BrowserPackageQueryFacetTier
{
    Nuspec,
    PackageContent,
}

public sealed record BrowserPackageQueryFacetDescriptor(
    string Id,
    string Label,
    string Summary,
    int Weight,
    BrowserPackageQueryFacetTier Tier,
    string? SelectionGroupId,
    bool CombinesWithinSelectionGroup,
    string? DisplayGroupId,
    string? DisplayGroupLabel);

public sealed record BrowserPackageQueryFacetCatalog(
    BrowserPackageQueryFacetDescriptor[] Facets);

public sealed record BrowserPackageQueryEvidence(
    string Id,
    string Text);

public sealed record BrowserPackageQueryRow(
    string PackageId,
    string Version,
    BrowserPackageQueryFacetTier Tier,
    BrowserPackageQueryEvidence[] Evidence,
    long TotalDownloads,
    bool Verified,
    string Producer);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryFailureKind>))]
public enum BrowserPackageQueryFailureKind
{
    Search,
    SearchContract,
    ManifestAcquisition,
    ManifestContract,
    InvalidManifest,
    PackageContentAcquisition,
    PackageContentEvaluation,
}

public sealed record BrowserPackageQueryFailure(
    string? PackageId,
    string? Version,
    string Producer,
    BrowserPackageQueryFailureKind Kind,
    string Message);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryProgressPhase>))]
public enum BrowserPackageQueryProgressPhase
{
    Search,
    Manifest,
    PackageContent,
}

public sealed record BrowserPackageQueryProgress(
    BrowserPackageQueryProgressPhase Phase,
    int Completed,
    int Limit);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryCompletionKind>))]
public enum BrowserPackageQueryCompletionKind
{
    Exhausted,
    MatchLimitReached,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    Failed,
}

public sealed record BrowserPackageQueryCompletion(
    string Prefix,
    string Producer,
    int CandidateLimit,
    int MatchLimit,
    int Candidates,
    int Matches,
    int Failures,
    BrowserPackageQueryCompletionKind Kind);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryEventKind>))]
public enum BrowserPackageQueryEventKind
{
    Progress,
    Match,
    Failure,
    Completed,
}

public sealed record BrowserPackageQueryEvent(
    BrowserPackageQueryEventKind Kind,
    BrowserPackageQueryRow? Row,
    BrowserPackageQueryFailure? Failure,
    BrowserPackageQueryCompletion? Completion,
    BrowserPackageQueryProgress? Progress = null);

/// <summary>
/// One vocabulary field's discoverable contract, mapped verbatim from
/// <c>DotnetInspector.Vocabulary.VocabularyWireField</c>. Kept as a browser-local record (rather
/// than reusing the product's wire type directly) so the TypeScript facade's JSON-wire-contract
/// discovery — which only walks types physically defined in this assembly — can generate a real
/// TypeScript interface for it instead of collapsing to <c>unknown</c>.
/// </summary>
public sealed record BrowserVocabularyField(
    string Id,
    string Label,
    string Summary,
    string Type,
    string[] Operators);

/// <summary>One vocabulary section, mapped verbatim from <c>DotnetInspector.Vocabulary.VocabularyWireSection</c>.</summary>
public sealed record BrowserVocabularySection(
    string Id,
    string Name,
    string Summary,
    string[] Categories,
    [property: JsonPropertyName("accepted_by")]
    string[] AcceptedBy,
    BrowserVocabularyField[] Fields,
    JsonElement[] Values);

/// <summary>
/// The product-owned query vocabulary document, mapped verbatim from
/// <c>DotnetInspector.Vocabulary.VocabularyWireDocument</c>. The browser receives the same
/// section/field/value document as the CLI and retains no separate labels, ordering, defaults, or
/// query semantics.
/// </summary>
public sealed record BrowserVocabularyDocument(
    [property: JsonPropertyName("schema_version")]
    int SchemaVersion,
    BrowserVocabularySection[] Sections);

/// <summary>
/// One product home-demo catalog row from <c>ProductInspectionDemos.Entries</c>.
/// Browser-local so <c>ts-jsexport</c> emits a real TypeScript interface.
/// </summary>
public sealed record BrowserHomeDemoCatalogEntry(
    string Id,
    string Title,
    string Summary);

/// <summary>Product home-demo catalog in display order.</summary>
public sealed record BrowserHomeDemoCatalog(
    BrowserHomeDemoCatalogEntry[] Demos);

/// <summary>
/// One workspace/navigation member coordinate projected for the browser.
/// <see cref="Kind"/> is <c>package</c> or <c>platform</c>.
/// </summary>
public sealed record BrowserHomeDemoMember(
    string Kind,
    string Id,
    string? Version,
    string? Framework,
    string? Assembly);

/// <summary>One navigation tab from a resolved home demo.</summary>
public sealed record BrowserHomeDemoNavigationTab(
    string Id,
    BrowserHomeDemoMember Member);

/// <summary>View selectors from a resolved home demo.</summary>
public sealed record BrowserHomeDemoView(
    string? Library,
    string? Type,
    string? MemberAnchor,
    string? MemberKey,
    string? Section);

/// <summary>
/// Fully resolved product home demo: workspace members, navigation, and view.
/// Hosts own share encoding and any residual platform pack mapping.
/// </summary>
public sealed record BrowserHomeDemoResolved(
    string Id,
    string Title,
    string Summary,
    BrowserHomeDemoMember[] WorkspaceMembers,
    BrowserHomeDemoNavigationTab[] Tabs,
    int FocusTabIndex,
    BrowserHomeDemoView View);

/// <summary>
/// Result of resolving one home demo id. <see cref="Demo"/> is set only when
/// <see cref="Found"/> is true (avoids a bare JSON null on the JSExport surface).
/// </summary>
public sealed record BrowserHomeDemoResolveResult(
    bool Found,
    BrowserHomeDemoResolved? Demo);

/// <summary>
/// Exact browser selection produced while running one product home demo.
/// The frontend applies this identity to the package surfaces returned by the
/// same operation; it does not parse product view or navigation definitions.
/// </summary>
public sealed record BrowserHomeDemoRunActivation(
    string FocusPackage,
    string FocusVersion,
    string FocusFramework,
    string TypeId,
    string Section,
    string? MemberName,
    string? MemberKind,
    string? MemberAnchorDigest,
    string? MemberSection);

/// <summary>
/// Browser result of running one product home demo through the normal package
/// workspace and query path. Unknown ids return <see cref="Found"/> false;
/// known-demo failures remain visible exceptions.
/// </summary>
public sealed record BrowserHomeDemoRunResult(
    bool Found,
    BrowserPackageSurface[] Packages,
    BrowserHomeDemoRunActivation? Activation,
    BrowserCallGraph? CallGraph);

/// <summary>
/// One product-normalized source in a canonical workspace share packet.
/// <see cref="Kind"/> is <c>package</c> or <c>group</c>; <see cref="Source"/>
/// is the package id or leading-colon group expression.
/// </summary>
public sealed record BrowserWorkspaceShareTab(
    string Id,
    string Kind,
    string Source,
    string? Version,
    string? Framework,
    string? RuntimeIdentifier);

/// <summary>
/// One binding-consistent context expressed through stable packet-local tab ids.
/// </summary>
public sealed record BrowserWorkspaceShareContext(
    string Id,
    string[] TabIds);

/// <summary>Canonical product-owned view fields carried by share packet v1.</summary>
public sealed record BrowserWorkspaceShareView(
    string? Lens,
    string? Type,
    string? MemberAnchor,
    string? MemberSignature,
    string? Section,
    string[] Libraries);

/// <summary>
/// Long-form Browser transport for one canonical packet-local scenario.
/// TypeScript consumes these product-owned identities and never parses compact
/// packet fields or base64url.
/// </summary>
public sealed record BrowserWorkspaceShareState(
    BrowserWorkspaceShareTab[] Tabs,
    BrowserWorkspaceShareContext[] Contexts,
    string ActiveTabId,
    string SelectedContextId,
    BrowserWorkspaceShareView View);

/// <summary>Typed codec, transposition, or Browser-transport failure.</summary>
public sealed record BrowserWorkspaceShareFailure(
    string Kind,
    string Path,
    string Message);

public sealed record BrowserWorkspaceShareDecodeResult(
    bool Succeeded,
    BrowserWorkspaceShareState? State,
    BrowserWorkspaceShareFailure? Failure);

public sealed record BrowserWorkspaceShareEncodeResult(
    bool Succeeded,
    string? Packet,
    BrowserWorkspaceShareFailure? Failure);

/// <summary>
/// One type's metadata projection, adapted from <c>ResearchViews.TypeProjectionResult</c> — the
/// presentation-neutral seam the CLI consumes — so the browser never reimplements type-fact
/// composition.
/// </summary>
public sealed record BrowserTypeMetadata(
    string FullName,
    string? Namespace,
    string Name,
    string Kind,
    string[] Modifiers,
    string? Accessibility,
    string? Assembly,
    string? BaseType,
    string[] Interfaces,
    string[] DerivedTypes,
    BrowserTypeParameter[] TypeParameters,
    string[] Attributes,
    string? EnumUnderlyingType,
    BrowserTypeComposition? Composition,
    BrowserTypeGraphNode[] GraphNodes,
    BrowserTypeGraphEdge[] GraphEdges,
    string[] InspectionFailures);

public sealed record BrowserTypeParameter(string Name, string? Variance, string[] Constraints);

public sealed record BrowserTypeComposition(
    int Methods,
    int Properties,
    int Fields,
    int Events,
    int Constructors,
    int Operators,
    int ExplicitInterfaceImplementations,
    int ExtensionMethods,
    int Static,
    int Unsafe,
    int Async,
    int Virtual,
    int Abstract,
    int Override,
    int Extension,
    int Obsolete,
    int Total);

public sealed record BrowserTypeGraphNode(string Id, string DisplayName, string Role);

public sealed record BrowserTypeGraphEdge(string FromId, string ToId, string Kind);

public sealed record BrowserPackageMetadata(
    BrowserAssemblyMetadata[] Assemblies,
    string? InspectionError,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserAssemblyMetadata(
    string Assembly,
    string MetadataVersion,
    bool MetadataVersionTruncated,
    string Kind,
    bool IsAssembly,
    int MetadataSize,
    int ProjectedTableTotal,
    BrowserMetadataHeap[] Heaps,
    BrowserMetadataTable[] Tables,
    BrowserMetadataHeaders Headers);

public sealed record BrowserMetadataHeap(
    string Name,
    int SizeInBytes,
    int MaxAddress,
    string Addressing);

public sealed record BrowserMetadataTable(
    int Index,
    string Name,
    int RowCount,
    bool IsProjected);

public sealed record BrowserMetadataHeaders(
    string Machine,
    bool IsPE32Plus,
    string Subsystem,
    string? CorFlags,
    int? MajorRuntimeVersion,
    int? MinorRuntimeVersion,
    int? EntryPointToken);

public sealed record BrowserMetadataWindow(
    string Assembly,
    int Index,
    string Name,
    int RowCount,
    int StartRowId,
    BrowserMetadataColumn[] Columns,
    BrowserMetadataRow[] Rows,
    bool Truncated,
    string? Error);

public sealed record BrowserMetadataColumn(
    string Name,
    string Kind,
    int[] CandidateTargets);

public sealed record BrowserMetadataRow(
    int RowId,
    int Token,
    BrowserMetadataCell[] Cells);

public sealed record BrowserMetadataCell(
    string Kind,
    long? Raw = null,
    string? Display = null,
    string? Decoded = null,
    string? Heap = null,
    string? Text = null,
    string? Preview = null,
    int? Offset = null,
    int? Length = null,
    bool? Truncated = null,
    int? TargetTable = null,
    int? TargetRowId = null,
    int? StartRowId = null,
    int? EndRowId = null,
    int? Count = null,
    int? Token = null,
    string? Detail = null);

public sealed record BrowserHeapListing(
    string Assembly,
    string Heap,
    string StreamName,
    string Coverage,
    BrowserHeapEntry[] Entries,
    bool RowsTruncated,
    bool EntriesTruncated,
    string? Error);

public sealed record BrowserHeapEntry(
    int Offset,
    BrowserMetadataCell Value,
    int ReferenceCount);

/// <summary>
/// Declared package dependency groups and one selected assembly's direct references. Dependency
/// parsing and exact-framework selection belong to <c>PackageDependencyGroupsQuery</c>; direct
/// references belong to <c>AssemblyContextReferencesQuery</c>.
/// </summary>
public sealed record BrowserPackageDependencies(
    string Package,
    string Version,
    string ActiveFramework,
    string? Assembly,
    BrowserPackageDependencyGroup[] DependencyGroups,
    BrowserAssemblyReference[] AssemblyReferences,
    string? DependencyGroupError,
    string? AssemblyReferenceError,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserPackageDependencyGroup(
    int Index,
    string Framework,
    bool IsActive,
    BrowserPackageDependency[] Dependencies);

public sealed record BrowserPackageDependency(
    string Id,
    string VersionRange);

public sealed record BrowserAssemblyReference(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserDependencyCoordinateProvenance>))]
public enum BrowserDependencyCoordinateProvenance
{
    NuGetPackage,
    PlatformRuntime,
}

public sealed record BrowserDependencyCoordinateCandidate(
    string Key,
    BrowserDependencyCoordinateProvenance Provenance,
    string PackageId,
    string Version,
    string TargetFramework);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserDependencyCoordinateMatchOutcome>))]
public enum BrowserDependencyCoordinateMatchOutcome
{
    NoMatch,
    Unique,
    Ambiguous,
}

public sealed record BrowserDependencyCoordinateMatch(
    BrowserDependencyCoordinateMatchOutcome Outcome,
    string? CandidateKey);

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

public sealed record BrowserSource(
    string Provider,
    string Provenance,
    string? Url,
    string? PdbSourceLimitation,
    string Text);

public sealed record BrowserStyleOption(
    string Id,
    string Title,
    string Summary,
    string Tier,
    bool ByteDivergent,
    bool OracleEndorsed,
    string? ConflictGroup);

public sealed record BrowserStyleTier(
    string Id,
    string Title,
    string Summary,
    int Order,
    bool ByteDivergent);

/// <summary>
/// Ecosystem integration evidence for one workspace, carried exactly as
/// <c>AssemblyContextIntegrationsQuery</c> produced it: one group per package/version/framework,
/// one entry per participant, and each participant's own signals grouped by the integration name
/// the scanner assigned. Grouping is presentation; no signal, category, or count is composed here.
/// </summary>
public sealed record BrowserPackageIntegrations(
    string Package,
    string Version,
    string Framework,
    BrowserIntegrationCategory[] Categories,
    int TotalSignals,
    bool IsComplete,
    string? InspectionError,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserIntegrationCategory(
    string Integration,
    BrowserIntegrationSignal[] Signals);

public sealed record BrowserIntegrationSignal(string Kind, string Name, string Shape);

/// <summary>
/// Integration opportunities for one package workspace, composed by
/// <c>AssemblyContextIntegrationOpportunitiesQuery</c> from its typed Integrations prerequisite.
/// The host groups and deduplicates rows for display; it does not infer opportunity evidence.
/// </summary>
public sealed record BrowserPackageOpportunities(
    string Package,
    string Version,
    string ActiveFramework,
    BrowserOpportunityCategory[] Categories,
    int TotalOpportunities,
    bool IsComplete,
    string? InspectionError,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserOpportunityCategory(
    string Integration,
    BrowserOpportunityItem[] Items);

public sealed record BrowserOpportunityItem(
    string Api,
    string IntegrationType,
    string LookFor,
    string? SourceDefinitionId,
    string SourceAssembly,
    string SourceAssemblyVersion,
    string? SourceAssemblyCulture,
    string? SourceAssemblyPublicKeyToken);

public sealed record BrowserPackagePerformance(
    BrowserPerformanceMember[] Members,
    string? InspectionError,
    int NonPublicOpportunities,
    int TotalOpportunities,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserPerformanceMember(
    string Assembly,
    string TypeId,
    string MemberName,
    string StableSelector,
    int[] BodyTokens,
    int OpportunityCount,
    int InLoopCount,
    string[] Shapes,
    string Confidence);

public sealed record BrowserMemberFacts(
    int MetadataToken,
    BrowserMethodSignals Signals,
    BrowserAllocationFact[] Allocations,
    BrowserCallFact[] Calls,
    BrowserSafetyFact[] Safety,
    BrowserExceptionRegion[] ExceptionRegions,
    BrowserPerformanceOpportunity[] PerformanceOpportunities,
    string[] Diagnostics);

public sealed record BrowserMethodSignals(
    int Allocations,
    int Copies,
    bool Unsafe,
    int Reflection,
    int Throws,
    int Catches,
    int Finallys,
    bool AllocatesInLoop,
    string[] EvidenceOffsets,
    string[] ExceptionTypes);

public sealed record BrowserAllocationFact(
    string Kind,
    string? Type,
    string Offset,
    bool CountedAsHeap,
    string Frequency,
    string Multiplicity,
    string Path,
    string Escape,
    bool InLoop,
    int? EstimatedSizeBytes,
    string? Detail);

public sealed record BrowserCallFact(
    string Callee,
    string Offset,
    string Opcode,
    string Kind,
    string Multiplicity,
    bool InLoop);

public sealed record BrowserSafetyFact(
    string Kind,
    string? Offset,
    string Operation,
    string Requirement,
    string Evidence);

public sealed record BrowserExceptionRegion(
    int Region,
    string Clause,
    string TryRange,
    string HandlerRange,
    string? FilterRange,
    string? CaughtType);

public sealed record BrowserPerformanceOpportunity(
    string Shape,
    string Evidence,
    string Fix,
    string Confidence,
    string? Offset,
    bool InLoop,
    string? Caveat,
    string? Finding,
    string Provenance);

/// <summary>
/// One progressively acquired member call graph, projected through
/// <c>ILInspector.CallGraph.CallGraphProjection</c>. Graph identity, direction, cycles,
/// boundaries, and escaping belong to the projection; this record carries it across the Wasm
/// boundary and the browser owns rendering.
/// </summary>
public sealed record BrowserCallGraph(
    string Mermaid,
    BrowserCallGraphNode Callers,
    BrowserCallGraphNode Callees,
    BrowserCallGraphScope Scope,
    BrowserCallGraphTarget[] Targets,
    BrowserCallGraphDiagnostics Diagnostics,
    bool NoBody = false);

public sealed record BrowserCallGraphDiagnostics(
    int IncompleteNodes,
    int IncompleteEdges,
    int BindingIdentityConflicts,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary)
{
    public bool IsIncomplete =>
        IncompleteNodes > 0
        || IncompleteEdges > 0
        || BindingIdentityConflicts > 0
        || HasUnexploredTraversalBoundary
        || HasAnalysisFailureBoundary;
}

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

public sealed record BrowserCallGraphNode(
    string Label,
    string Status,
    bool InLoop,
    string? Source,
    BrowserCallGraphNode[] Children,
    string Assembly,
    string TypeFullName,
    string MemberName);

public sealed record BrowserCallGraphScope(
    int Packages,
    int Assemblies,
    int CallerAssemblies,
    string CalleeScope);

public sealed record BrowserWorkspacePackage(
    string Package,
    string Version,
    string Framework);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageSurface))]
[JsonSerializable(typeof(BrowserMemberSurface))]
[JsonSerializable(typeof(BrowserGraphMemberSurface))]
[JsonSerializable(typeof(BrowserPackageDocumentContent))]
[JsonSerializable(typeof(BrowserMemberDocumentation))]
[JsonSerializable(typeof(BrowserPackageCacheStats))]
[JsonSerializable(typeof(BrowserBuildIdentity))]
[JsonSerializable(typeof(BrowserPackageQueryFacetCatalog))]
[JsonSerializable(typeof(BrowserPackageQueryEvent))]
[JsonSerializable(typeof(BrowserPackageDependencies))]
[JsonSerializable(typeof(BrowserDependencyCoordinateCandidate[]))]
[JsonSerializable(typeof(BrowserDependencyCoordinateMatch))]
[JsonSerializable(typeof(BrowserPackageIntegrations))]
[JsonSerializable(typeof(BrowserPackageOpportunities))]
[JsonSerializable(typeof(BrowserPackagePerformance))]
[JsonSerializable(typeof(BrowserMemberFacts))]
[JsonSerializable(typeof(BrowserTypeMetadata))]
[JsonSerializable(typeof(BrowserPackageMetadata))]
[JsonSerializable(typeof(BrowserMetadataWindow))]
[JsonSerializable(typeof(BrowserHeapListing))]
[JsonSerializable(typeof(BrowserAnnotatedSource))]
[JsonSerializable(typeof(BrowserSource))]
[JsonSerializable(typeof(BrowserCallGraph))]
[JsonSerializable(typeof(BrowserWorkspacePackage[]))]
[JsonSerializable(typeof(BrowserTypeCandidate[]))]
[JsonSerializable(typeof(BrowserTypeSearchHit[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(BrowserVocabularyDocument))]
[JsonSerializable(typeof(BrowserHomeDemoCatalog))]
[JsonSerializable(typeof(BrowserHomeDemoResolved))]
[JsonSerializable(typeof(BrowserHomeDemoResolveResult))]
[JsonSerializable(typeof(BrowserHomeDemoRunResult))]
[JsonSerializable(typeof(BrowserWorkspaceShareState))]
[JsonSerializable(typeof(BrowserWorkspaceShareDecodeResult))]
[JsonSerializable(typeof(BrowserWorkspaceShareEncodeResult))]
internal sealed partial class BrowserJsonContext : JsonSerializerContext;
