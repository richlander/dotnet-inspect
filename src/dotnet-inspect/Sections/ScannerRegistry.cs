using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Sections;

/// <summary>
/// Context passed to each scanner during data collection.
/// </summary>
public sealed class ScannerContext
{
    public required string AssemblyPath { get; init; }
    public required LibraryInspection Model { get; init; }
    public required VerboseLogger Logger { get; init; }
    public PdbContext? MetadataContext { get; init; }

    /// <summary>
    /// Analysis features required by the complete scanner set. The shared body session computes
    /// their union once, so Array Pool Escapes can share acquisition with leverage/performance scans
    /// without making a resource-only request pay for unrelated body evidence.
    /// </summary>
    public Analysis.LibraryBodyAnalysisFeatures BodyAnalysisFeatures { get; init; }
        = Analysis.LibraryBodyAnalysisFeatures.Default;

    private MethodBodyInspectionSession? _bodySession;
    private Dictionary<int, (string? Stable, string Visibility, string Selector)>?
        _drillMap;

    /// <summary>
    /// Shared method-body analysis index for <see cref="AssemblyPath"/>, built once on first use.
    /// The body-index scanners (unsafe members, top leverage, optimization opportunities) share it
    /// instead of each rebuilding the full <c>LibraryBodyIndex</c>. Scanners run sequentially
    /// (<see cref="ScannerRegistry.RunScanners"/>), so no synchronization is required. The build is
    /// narrowed to the phases the requested scanners consume (see
    /// <see cref="BodyAnalysisFeatures"/>).
    /// </summary>
    public Analysis.LibraryBodyIndex BodyIndex() =>
        (_bodySession ??= MethodBodyInspectionSession.OpenWithPrefetchedImage(
            AssemblyPath,
            GetMetadataContext(),
            BodyAnalysisFeatures)).BodyIndex;

    /// <summary>
    /// Stable member drill coordinates, derived once from the command's shared
    /// metadata reader.
    /// </summary>
    public IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>
        DrillMap()
        => _drillMap ??=
            LibraryMetadataService.BuildLibraryDrillMap(
                GetMetadataContext(),
                Logger);

    PdbContext GetMetadataContext()
        => MetadataContext
            ?? throw new InvalidOperationException(
                "A shared metadata context is required by this scanner.");
}

/// <summary>
/// Registry of named scanners for the library command.
/// Each scanner is a function that populates part of a <see cref="LibraryInspection"/>
/// model. Scanners are registered by key and invoked only when needed.
/// </summary>
public sealed class ScannerRegistry
{
    private readonly Dictionary<string, Action<ScannerContext>?> _scanners = [];
    private readonly Dictionary<string, string[]> _requires = [];

    /// <summary>
    /// The keys of every registered scanner. This is the supply side of the section-to-scanner
    /// binding; the demand side is <see cref="SectionPipeline{TModel}.DeclaredScannerKeys"/>.
    /// Exposed so a test can hold the two sets equal: <see cref="RunScanners"/> skips a requested
    /// key it has no registration for, so an unregistered key is otherwise silent. Gate for the
    /// library pipeline:
    /// <c>SectionPipelineTests.LibraryScannerRegistry_RegistrationMatchesDeclaration</c>.
    /// </summary>
    public IReadOnlyCollection<string> RegisteredKeys => _scanners.Keys;

    /// <summary>
    /// Registers a scanner by key. The action populates the model with data.
    /// </summary>
    /// <param name="requires">
    /// Keys of scanners whose output this scanner reads. Declaring a prerequisite is the only
    /// supported way to depend on another scanner's work: <see cref="RunScanners"/> runs
    /// prerequisites first and runs every scanner at most once per context. A scanner that calls
    /// another scanner directly instead hides the dependency from
    /// <see cref="ExpandRequired"/>, so cost and ordering stop being computable from the
    /// registry. Gate: <c>ScannerRegistryTests.LibraryScanners_DeclareTheirPrerequisites</c>.
    /// </param>
    public ScannerRegistry Add(string key, Action<ScannerContext> scan, params string[] requires)
    {
        _scanners[key] = scan;
        _requires[key] = requires;
        return this;
    }

    /// <summary>
    /// Registers a key that performs no work of its own and exists only to pull in
    /// <paramref name="requires"/>. A section whose data comes from several scanners binds to a
    /// bundle, because <see cref="ISectionDescriptor{TModel}.ScannerKey"/> names a single key.
    /// </summary>
    public ScannerRegistry AddBundle(string key, params string[] requires)
    {
        _scanners[key] = null;
        _requires[key] = requires;
        return this;
    }

    /// <summary>
    /// The prerequisite keys declared by <paramref name="key"/>, or an empty span when it has
    /// none. Exposed so a test can assert the declared graph rather than infer it from behavior.
    /// </summary>
    public IReadOnlyList<string> RequirementsOf(string key)
        => _requires.TryGetValue(key, out var r) ? r : [];

    /// <summary>
    /// Expands <paramref name="requested"/> to include every transitively required scanner.
    /// Callers that reason about the work a run will do — notably the body-analysis feature
    /// selection, which must see a prerequisite that opens the body index — must expand first, or
    /// they will under-count the run.
    /// </summary>
    public HashSet<string> ExpandRequired(IEnumerable<string> requested)
    {
        HashSet<string> closure = new(StringComparer.Ordinal);
        foreach (var key in requested)
            AddWithRequirements(key, closure);
        return closure;
    }

    private void AddWithRequirements(string key, HashSet<string> closure)
    {
        if (!closure.Add(key))
            return;
        foreach (var required in RequirementsOf(key))
            AddWithRequirements(required, closure);
    }

    /// <summary>
    /// Runs the scanners in <paramref name="requiredScanners"/> and everything they require,
    /// prerequisites first, each at most once. Registration order breaks ties so a run is
    /// deterministic.
    /// </summary>
    public void RunScanners(HashSet<string> requiredScanners, ScannerContext context)
    {
        HashSet<string> ran = new(StringComparer.Ordinal);
        HashSet<string> running = new(StringComparer.Ordinal);
        foreach (var key in _scanners.Keys)
        {
            if (requiredScanners.Contains(key))
                RunWithRequirements(key, context, ran, running);
        }
    }

    private void RunWithRequirements(
        string key,
        ScannerContext context,
        HashSet<string> ran,
        HashSet<string> running)
    {
        if (!_scanners.TryGetValue(key, out var scan) || ran.Contains(key))
            return;

        if (!running.Add(key))
            throw new InvalidOperationException(
                $"Scanner prerequisite cycle detected at '{key}'.");

        foreach (var required in RequirementsOf(key))
            RunWithRequirements(required, context, ran, running);

        running.Remove(key);
        ran.Add(key);
        scan?.Invoke(context);
    }
}
