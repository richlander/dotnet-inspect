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

    AssemblyInspectionSession(AssemblyImage image) => _image = image;

    /// <summary>Opens a session from a file path.</summary>
    public static AssemblyInspectionSession Open(string path) => new(AssemblyImage.Open(path));

    /// <summary>Opens a session from a resolved assembly reference (path or stream opener).</summary>
    public static AssemblyInspectionSession Open(ResolvedAssemblyReference reference) => new(AssemblyImage.Open(reference));

    /// <summary>Whether the image contains managed metadata (false for a native binary).</summary>
    public bool HasMetadata => _image.HasMetadata;

    // The method-body / IL seam (the CLI's MemberCodeProvider + ILInstructionPrinter) needs low-level
    // reader access that the public facet API deliberately does not expose. These internal
    // accessors let that composition read through this owned image instead of opening its own raw
    // PEReader, so the single PE open stays owned here. The public contract ("callers never touch a
    // PEReader") is unchanged for external consumers.
    internal System.Reflection.PortableExecutable.PEReader PeReader => _image.PEReader;
    internal System.Reflection.Metadata.MetadataReader MetadataReader => _image.GetMetadataReader();

    // --- assembly facets: each produced once over the single shared reader ---

    /// <summary>Assembly identity and, optionally, its assembly references.</summary>
    public AssemblyInfo AssemblyInfo(bool includeReferences = false)
        => AssemblyInspector.ExtractAssemblyInfo(_image.PEReader, includeReferences);

    /// <summary>The public (or, with <paramref name="includeAll"/>, full) API surface.</summary>
    public ApiSurface ApiSurface(bool includeAll = false, bool typesOnly = false)
        => ApiSurfaceExtractor.Extract(_image.PEReader, includeAll, typesOnly);

    /// <summary>Manifest resources.</summary>
    public List<ManifestResourceInfo> Resources()
        => ResourceScanner.Scan(_image.PEReader);

    /// <summary>Feature-switch metadata.</summary>
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

    public void Dispose() => _image.Dispose();
}
