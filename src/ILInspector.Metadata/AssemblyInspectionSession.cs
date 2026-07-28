namespace ILInspector.Metadata;

/// <summary>
/// The assembly-level inspection hub. Opens a PE image once (via <see cref="AssemblyImage"/>) and
/// produces assembly <em>facets</em> by delegating to the metadata scanners over the single shared
/// reader. Callers never touch a <c>PEReader</c>; each facet is produced on request.
///
/// This is the assembly seam of the inspection query model
/// (<c>docs/design/assembly-inspection-query.md</c>): a facet registry that delegates to
/// per-facet producers, not a god-object. The method-body seam is a sibling session opened over
/// the same image.
/// </summary>
public sealed class AssemblyInspectionSession : IDisposable
{
    readonly AssemblyImage _image;
    MethodBodySource? _methodBodies;

    AssemblyInspectionSession(AssemblyImage image) => _image = image;

    /// <summary>Opens a session from a file path.</summary>
    public static AssemblyInspectionSession Open(string path) => new(AssemblyImage.Open(path));

    /// <summary>Opens a session from a resolved assembly reference (path or stream opener).</summary>
    public static AssemblyInspectionSession Open(ResolvedAssemblyReference reference) => new(AssemblyImage.Open(reference));

    /// <summary>Whether the image contains managed metadata (false for a native binary).</summary>
    public bool HasMetadata => _image.HasMetadata;

    /// <summary>
    /// Session-bound method-body and operand access without exposing raw readers.
    /// </summary>
    public MethodBodySource MethodBodies
    {
        get
        {
            _image.EnsureAlive();
            return _methodBodies ??= new MethodBodySource(_image.PEReader, _image.EnsureAlive);
        }
    }

    // --- assembly facets: each produced once over the single shared reader ---

    /// <summary>Assembly identity and, optionally, its assembly references.</summary>
    public AssemblyInfo AssemblyInfo(bool includeReferences = false)
        => AssemblyInspector.ExtractAssemblyInfo(_image.PEReader, includeReferences);

    /// <summary>
    /// The image's own simple assembly name and the simple names of its assembly references,
    /// read from the <c>Assembly</c> and <c>AssemblyRef</c> tables alone. Use this in preference to
    /// <see cref="AssemblyInfo"/> when only reachability by name is needed: it decodes no
    /// signatures and derives no public-key tokens.
    /// </summary>
    public AssemblyIdentityNames IdentityNames()
        => AssemblyIdentityScanner.Scan(_image.PEReader);

    /// <summary>The public (or, with <paramref name="includeAll"/>, full) API surface.</summary>
    public ApiSurface ApiSurface(bool includeAll = false, bool typesOnly = false)
        => ApiSurfaceExtractor.Extract(_image.PEReader, includeAll, typesOnly);

    /// <summary>Manifest resources.</summary>
    public List<ManifestResourceInfo> Resources()
        => ResourceScanner.Scan(_image.PEReader);

    /// <summary>
    /// Extracts embedded manifest resources beneath a directory without allowing
    /// path escape or overwriting existing files.
    /// </summary>
    public List<string> ExtractResources(string outputDirectory)
        => ResourceScanner.ExtractAll(_image.PEReader, outputDirectory);

    /// <summary>
    /// Attribute-declared feature-switch metadata. IL call-site discovery is
    /// outside this metadata-only facet.
    /// </summary>
    public List<SwitchInfo> Switches()
        => SwitchScanner.Scan(_image.PEReader);

    /// <summary>Classified methods (unsafe / P-Invoke / async).</summary>
    public List<ClassifiedMethodInfo> ClassifiedMethods()
        => MethodClassificationScanner.Scan(_image.PEReader);

    /// <summary>OpenTelemetry integration signals.</summary>
    public List<OpenTelemetrySignalInfo> OpenTelemetrySignals()
        => OpenTelemetryScanner.Scan(_image.PEReader);

    /// <summary>Ecosystem integration signals.</summary>
    public List<EcosystemIntegrationSignalInfo> EcosystemIntegrations()
        => EcosystemIntegrationScanner.Scan(_image.PEReader);

    /// <summary>Integration opportunities, excluding already-present integrations.</summary>
    public List<IntegrationOpportunityInfo> IntegrationOpportunities(IReadOnlySet<string> existingIntegrations)
        => IntegrationOpportunityScanner.Scan(_image.PEReader, existingIntegrations);

    /// <summary>Discriminated-union types.</summary>
    public List<UnionTypeInfo> UnionTypes()
        => UnionTypeScanner.Scan(_image.PEReader);

    /// <summary>Extension methods.</summary>
    public IEnumerable<ExtensionMethodInfo> ExtensionMethods(bool includeAll = false)
        => ExtensionMethodScanner.FindAllExtensions(_image.PEReader, includeAll);

    /// <summary>Assembly-level custom attributes.</summary>
    public List<AssemblyAttributeInfo> CustomAttributes()
        => AssemblyDetailScanner.ScanCustomAttributes(_image.PEReader);

    /// <summary>Type forwarders.</summary>
    public List<TypeForwarderInfo> TypeForwarders()
        => AssemblyDetailScanner.ScanTypeForwarders(_image.PEReader);

    /// <summary>Assembly audit metadata (P-Invoke counts, flags, …).</summary>
    public AssemblyAuditMetadata AuditMetadata()
        => AssemblyDetailScanner.ScanAuditMetadata(_image.PEReader);

    /// <summary>Presence flags for assembly-level features.</summary>
    public PresenceFlags PresenceFlags()
        => AssemblyDetailScanner.ScanPresenceFlags(_image.PEReader);

    /// <summary>
    /// A raw ECMA-335 metadata-table projection (see
    /// <c>docs/design/metadata-table-projection.md</c>). Structurally lossless
    /// over SRM's logical table/heap graph and a sibling of the typed facets
    /// above, never derived from them.
    /// </summary>
    public MetadataTableProjection MetadataTables(MetadataProjectionOptions? options = null)
        => MetadataTableProjector.Project(_image.PEReader, options);

    /// <summary>
    /// A single row of one metadata table, read on demand and independent of any
    /// row window. This is the handle click-through primitive: it reaches a
    /// target row that a windowed <see cref="MetadataTables"/> call did not
    /// include. Null when the table is unsupported or the row id is past its end.
    /// </summary>
    public MetadataTableView? MetadataTableRow(
        System.Reflection.Metadata.Ecma335.TableIndex table,
        int rowId,
        MetadataProjectionOptions? options = null)
        => MetadataTableProjector.ProjectRow(_image.PEReader, table, rowId, options);

    /// <summary>
    /// The reverse of the projection's handle edges: every row pointing at the
    /// given row, including through list-column runs so ownership resolves. The
    /// result reports its own blind spots rather than folding them into an empty
    /// answer.
    /// </summary>
    public MetadataRowReferenceSet MetadataReferences(
        System.Reflection.Metadata.Ecma335.TableIndex targetTable,
        int targetRowId,
        int maxReferences = MetadataRowReferenceSet.DefaultMaxReferences)
        => MetadataTableProjector.FindReferences(_image.PEReader, targetTable, targetRowId, maxReferences);

    /// <summary>
    /// Image-level facts outside the table projection: metadata root identity,
    /// heap sizes and addressing, physical row counts for every ECMA-335 table
    /// (including tables the projection does not model), and PE/CLI header
    /// facts. Null when the image carries no metadata.
    /// </summary>
    public MetadataImageOverview? MetadataImage()
        => MetadataImageInspector.Describe(_image.PEReader);

    /// <summary>
    /// One heap value read by address, independent of any row that references
    /// it. The address follows
    /// <see cref="MetadataValue.HeapReference.Offset"/>, so a projected cell's
    /// offset round-trips. Null when the image carries no metadata.
    /// </summary>
    public MetadataValue? MetadataHeapValue(
        HeapKind heap,
        int address,
        MetadataProjectionOptions? options = null)
        => MetadataTableProjector.ReadHeapValue(_image.PEReader, heap, address, options);

    public void Dispose() => _image.Dispose();
}
