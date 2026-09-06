using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

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
    readonly Lazy<MetadataTypeDeclarationProbe.Index>
        _declarationIndex;
    MethodBodySource? _methodBodies;

    AssemblyInspectionSession(AssemblyImage image)
    {
        _image = image;
        _declarationIndex =
            new Lazy<MetadataTypeDeclarationProbe.Index>(
                () => MetadataTypeDeclarationProbe.CreateIndex(
                    _image.GetMetadataReader()),
                LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Opens a session from a file path.</summary>
    public static AssemblyInspectionSession Open(string path) => new(AssemblyImage.Open(path));

    /// <summary>Opens a session from a resolved assembly reference (path or stream opener).</summary>
    public static AssemblyInspectionSession Open(ResolvedAssemblyReference reference) => new(AssemblyImage.Open(reference));

    /// <summary>
    /// Opens a session over an immutable image snapshot without reopening its acquisition source.
    /// </summary>
    /// <remarks>
    /// Gated by
    /// <c>RegistryRun_ScansEveryParticipantInOrderAndReusesSnapshots</c>.
    /// </remarks>
    public static AssemblyInspectionSession Open(AssemblyImageSnapshot snapshot) =>
        new(AssemblyImage.Open(snapshot));

    internal static AssemblyInspectionSession OpenPrefetched(Stream stream) =>
        new(AssemblyImage.OpenPrefetched(stream));

    // Only the synchronous artifact query scope uses this borrow. It disposes
    // the session before disposing the reader and releasing the image pin.
    internal static AssemblyInspectionSession Borrow(PEReader reader) =>
        new(AssemblyImage.Borrow(reader, () => _ = reader.GetEntireImage()));

    /// <summary>
    /// A session over an image a <see cref="PdbContext"/> already opened, so a caller that holds
    /// one can reach the facets without opening the path a second time.
    ///
    /// Two opens of one path are only the same assembly by assumption. Anything that replaces the
    /// file between them — a build, a package restore, a retargeted symlink — makes one inspection
    /// report facts from two different assemblies with a zero exit code. Borrowing removes the
    /// assumption rather than narrowing the window.
    ///
    /// The session does not own the image: disposing it leaves <paramref name="context"/> open.
    /// Using it after <paramref name="context"/> is disposed throws
    /// <see cref="ObjectDisposedException"/> — including from a <see cref="MethodBodySource"/>
    /// obtained while the context was still alive, which would otherwise read unmapped memory and
    /// take the process down with an <see cref="AccessViolationException"/>.
    ///
    /// Gated by <c>SharedSessionScanners_ObserveTheImageTheCommandAlreadyOpened</c>,
    /// <c>BorrowedSession_DoesNotDisposeTheOwningContext</c>, and
    /// <c>BorrowedSession_FailsLoudlyAfterTheLenderIsDisposed</c>.
    /// </summary>
    public static AssemblyInspectionSession Borrow(PdbContext context)
        => new(AssemblyImage.Borrow(context.BorrowedPEReader, context.EnsureAliveForBorrower));

    /// <summary>Whether the image contains managed metadata (false for a native binary).</summary>
    public bool HasMetadata
    {
        get
        {
            _image.EnsureAlive();
            return _image.HasMetadata;
        }
    }

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

    /// <summary>Direct assembly references without decoding unrelated assembly facts.</summary>
    public List<AssemblyReference> AssemblyReferences()
        => AssemblyInspector.ExtractReferences(_image.PEReader);

    /// <summary>Direct typed assembly-reference identities without presentation projection.</summary>
    public List<AssemblyReferenceIdentity> AssemblyReferenceIdentities()
        => AssemblyInspector.ExtractReferenceIdentities(_image.PEReader);

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

    internal ApiSurface ApiSurface(
        ResolvedAssemblyReference source,
        TypeResolutionCatalog catalog,
        IAssemblyBindingPolicy bindingPolicy,
        bool includeAll,
        bool typesOnly) =>
        ApiSurfaceExtractor.Extract(
            _image.PEReader,
            source,
            catalog,
            bindingPolicy,
            includeAll,
            typesOnly);

    /// <summary>
    /// Reads a TypeDef's instance-field primitive after the durable address
    /// matches this image. Used by TypeResolution so enum-width consumers never
    /// borrow a <c>MetadataReader</c>.
    /// </summary>
    internal bool TryGetEnumUnderlyingType(
        MetadataTypeDefinitionAddress address,
        out System.Reflection.Metadata.PrimitiveTypeCode code)
    {
        _image.EnsureAlive();
        code = default;
        System.Reflection.Metadata.MetadataReader reader =
            _image.GetMetadataReader();
        if (!address.TryResolve(
                reader,
                out System.Reflection.Metadata.TypeDefinitionHandle handle))
        {
            return false;
        }

        return EnumUnderlyingPrimitive.TryFromEnumDefinition(
            reader,
            handle,
            out code);
    }

    /// <summary>The API surface at one explicit extraction scope.</summary>
    public ApiSurface ApiSurface(ApiSurfaceExtractionScope scope, bool typesOnly = false)
        => ApiSurfaceExtractor.Extract(_image.PEReader, scope, typesOnly);

    /// <summary>
    /// The API surface at one explicit extraction scope under hard retention bounds. An image
    /// that does not fit is abandoned before it is materialized, and reported as
    /// <see cref="ApiSurfaceExtractionResult.Exceeded"/> rather than returned shortened.
    /// </summary>
    public ApiSurfaceExtractionResult BoundedApiSurface(
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly = false)
        => ApiSurfaceExtractor.ExtractBounded(_image.PEReader, scope, bounds, typesOnly);

    /// <summary>Manifest resources.</summary>
    public List<ManifestResourceInfo> Resources()
    {
        _image.EnsureAlive();
        return ResourceScanner.Scan(_image.PEReader);
    }

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
    {
        _image.EnsureAlive();
        return SwitchScanner.Scan(_image.PEReader);
    }

    /// <summary>Classified methods (unsafe / P-Invoke / async).</summary>
    public List<ClassifiedMethodInfo> ClassifiedMethods()
    {
        _image.EnsureAlive();
        return MethodClassificationScanner.Scan(_image.PEReader);
    }

    /// <summary>OpenTelemetry integration signals.</summary>
    public List<OpenTelemetrySignalInfo> OpenTelemetrySignals()
        => OpenTelemetryScanner.Scan(_image.PEReader);

    /// <summary>Ecosystem integration signals.</summary>
    public List<EcosystemIntegrationSignalInfo> EcosystemIntegrations()
        => EcosystemIntegrationScanner.Scan(_image.PEReader);

    /// <summary>Decodes immutable Integration observations from this retained image.</summary>
    public EcosystemIntegrationObservationContext EcosystemIntegrationObservations()
    {
        _image.EnsureAlive();
        return _image.HasMetadata
            ? EcosystemIntegrationObservationReader.Read(_image.GetMetadataReader())
            : new EcosystemIntegrationObservationContext([], []);
    }

    /// <summary>Runs one selected scanner without computing full-library presence.</summary>
    public List<EcosystemIntegrationSignalInfo> EcosystemIntegrations(
        EcosystemIntegrationScannerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return EcosystemIntegrationScanner.Scan(
            EcosystemIntegrationObservations(),
            binding);
    }

    /// <summary>Presence flags summarized from grouped integration evidence.</summary>
    public EcosystemIntegrationPresence EcosystemIntegrationPresence(
        IEnumerable<EcosystemIntegrationSignalInfo> ecosystemSignals)
        => EcosystemIntegrationScanner.SummarizePresence(
            _image.PEReader,
            ecosystemSignals,
            OpenTelemetryScanner.HasSupport(_image.PEReader));

    /// <summary>Integration opportunities, excluding already-present integrations.</summary>
    public List<IntegrationOpportunityInfo> IntegrationOpportunities(IReadOnlySet<string> existingIntegrations)
        => IntegrationOpportunityScanner.Scan(_image.PEReader, existingIntegrations);

    /// <summary>Integration opportunities, excluding exact configured concepts.</summary>
    public List<IntegrationOpportunityInfo> IntegrationOpportunities(
        IReadOnlySet<IntegrationConceptDescriptor> existingIntegrations)
        => IntegrationOpportunityScanner.Scan(
            _image.PEReader,
            existingIntegrations);

    /// <summary>Discriminated-union types.</summary>
    public List<UnionTypeInfo> UnionTypes()
    {
        _image.EnsureAlive();
        return UnionTypeScanner.Scan(_image.PEReader);
    }

    /// <summary>Extension methods.</summary>
    public IEnumerable<ExtensionMethodInfo> ExtensionMethods(bool includeAll = false)
        => ExtensionMethodScanner.FindAllExtensions(_image.PEReader, includeAll);

    /// <summary>Image-local type addresses for lazy extension reachability.</summary>
    public IReadOnlyList<ExtensionReachabilityType> ExtensionReachabilityTypes()
        => ExtensionMethodScanner.IndexReachableTypes(_image.PEReader);

    /// <summary>Reachable public-member edges for one image-local type address.</summary>
    public IReadOnlyList<ExtensionReachabilityEdge> ExtensionReachabilityEdges(
        int metadataToken)
        => ExtensionMethodScanner.FindReachableEdges(
            _image.PEReader,
            metadataToken);

    /// <summary>Types that directly implement or extend the requested type.</summary>
    public IEnumerable<TypeRelationship> Implementers(
        string targetType,
        bool includeHidden = false)
        => TypeHierarchyScanner.FindImplementers(
            _image.PEReader,
            targetType,
            includeHidden);

    /// <summary>Assembly-level custom attributes.</summary>
    public List<AssemblyAttributeInfo> CustomAttributes()
        => AssemblyDetailScanner.ScanCustomAttributes(_image.PEReader);

    /// <summary>Type forwarders.</summary>
    public List<TypeForwarderInfo> TypeForwarders()
    {
        _image.EnsureAlive();
        return AssemblyDetailScanner.ScanTypeForwarders(_image.PEReader);
    }

    /// <summary>Assembly audit metadata (P-Invoke counts, flags, …).</summary>
    public AssemblyAuditMetadata AuditMetadata()
    {
        _image.EnsureAlive();
        return AssemblyDetailScanner.ScanAuditMetadata(_image.PEReader);
    }

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
    /// Captures an explicitly selected metadata root for scoped table and heap
    /// navigation. The captured root remains readable after this session closes.
    /// </summary>
    public MetadataRootInspection? MetadataRoot(MetadataRootKind root = MetadataRootKind.Cli)
        => MetadataRootInspection.Open(_image.PEReader, root);

    /// <summary>The ReadyToRun envelope, or null when the image does not advertise one.</summary>
    public ReadyToRunImageOverview? ReadyToRunImage()
        => ReadyToRunImageInspector.Describe(_image.PEReader);

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
    /// <param name="untrustedText">
    /// What to do with the metadata root's version stamp, which is artifact-controlled text.
    /// Defaults to containment, matching the projection.
    /// </param>
    public MetadataImageOverview? MetadataImage(
        UntrustedTextMode untrustedText = UntrustedTextMode.Contain)
        => MetadataImageInspector.Describe(_image.PEReader, untrustedText);

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

    /// <summary>
    /// The listable entries of one heap, with the limits of that listing attached — complete for
    /// the GUID heap, the values projected rows reference for the string and blob heaps, and
    /// nothing at all for the user-string heap, which no table column points into. The result
    /// carries its own <see cref="MetadataHeapEntrySet.Coverage"/> so a bounded or partial listing
    /// is never read as a whole heap. Null when the image carries no metadata.
    /// </summary>
    public MetadataHeapEntrySet? MetadataHeapEntries(
        HeapKind heap,
        MetadataProjectionOptions? options = null)
        => MetadataTableProjector.ReadHeapEntries(_image.PEReader, heap, options);

    internal AssemblyReferenceIdentity AssemblyIdentity() =>
        AssemblyReferenceIdentity.FromAssemblyDefinition(_image.GetMetadataReader());

    internal Guid ModuleVersionId()
    {
        var reader = _image.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    /// <summary>
    /// Probes one exact structured type name in this immutable assembly image.
    /// </summary>
    public TypeDeclarationResult ProbeDeclaration(
        MetadataTypeDefinitionName name)
    {
        _image.EnsureAlive();
        return _declarationIndex.Value.Probe(name);
    }

    /// <summary>
    /// Reports whether this image declares one exact structured extension
    /// member identity.
    /// </summary>
    public bool DeclaresExtensionMember(
        MetadataTypeDefinitionName declaringType,
        MemberAnchor member)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(member);
        _image.EnsureAlive();
        return ExtensionMethodScanner.FindAllExtensions(
                _image.PEReader,
                includeAll: true)
            .Any(extension =>
                extension.GetDeclaringTypeDefinition()
                    == declaringType
                && extension.Anchor == member);
    }

    public void Dispose() => _image.Dispose();
}
