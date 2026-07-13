using DotnetInspector.Options;
using DotnetInspector.Sections;
using SectionRegistrySpike.Sections;

namespace SectionRegistrySpike.CurrentBaseline;

/// <summary>
/// Representative analog of <c>ScannerContext</c>: a per-run context with a lazily built, shared
/// body-index session. Scanners that need it call <see cref="BodyIndex"/>; the underlying build
/// happens at most once per context, exactly like <c>ScannerContext.BodyIndex()</c>.
/// </summary>
public sealed class CurrentScannerContext
{
    public required SpikeModel Model { get; init; }
    public required bool NetworkAuthorized { get; init; }

    public List<string> Trace { get; } = [];
    public int BodyIndexBuilds { get; private set; }

    private int? _bodyIndex;

    public int BodyIndex()
    {
        if (_bodyIndex is null)
        {
            Trace.Add("create BodyIndex");
            _bodyIndex = 42; // representative shared computed index value
            BodyIndexBuilds++;
            Trace.Add("execute BodyIndex");
        }
        return _bodyIndex.Value;
    }
}

/// <summary>
/// Representative analog of <c>ScannerRegistry</c>: a string-keyed dictionary of scan actions,
/// run only for the scanner keys the pipeline says are required.
/// </summary>
public sealed class CurrentScannerRegistry
{
    private readonly List<(string Key, Action<CurrentScannerContext> Scan)> _scanners = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public CurrentScannerRegistry Add(string key, Action<CurrentScannerContext> scan)
    {
        if (!_keys.Add(key))
            throw new InvalidOperationException($"Scanner '{key}' is already registered.");
        _scanners.Add((key, scan));
        return this;
    }

    public void RunScanners(HashSet<string> requiredScanners, CurrentScannerContext context)
    {
        foreach (var (key, scan) in _scanners)
        {
            if (requiredScanners.Contains(key))
                scan(context);
        }
    }
}

/// <summary>
/// Builds the "current" baseline: the real <see cref="SectionPipeline{TModel}"/> reused verbatim
/// for selection, plus the representative scanner registry/context above for execution. This is
/// the honest today-shape: scanner dependencies live implicitly inside the context (BodyIndex),
/// and network dependencies are decided separately via <c>GetAuthorizedSections</c> — the same
/// split as <c>LibraryMetadataService</c>.
/// </summary>
public static class CurrentBaselinePipelines
{
    public static SectionPipeline<SpikeModel> CreatePipeline() => new SectionPipeline<SpikeModel>()
        .Add<SpikeSections.MetadataSection>(m => m.IsManagedAssembly)
        .Add<SpikeSections.DecompiledSourceSection>(m => m.IsManagedAssembly)
        .Add<SpikeSections.OriginalSourceSection>(m => m.HasSourceLink)
        .Add<SpikeSections.CallsSection>(m => m.HasMethodBodies)
        .Add<SpikeSections.FactsSection>(m => m.HasMethodBodies)
        .AddCategory("@Projections", "Calls", "Facts")
        .AddCategory("@Source", "Decompiled Source", "Original Source");

    public static CurrentScannerRegistry CreateScannerRegistry() => new CurrentScannerRegistry()
        .Add(SpikeSections.ScannerMetadata, ctx =>
        {
            ctx.Trace.Add("create Metadata");
            ctx.Model.MetadataLoaded = true;
            ctx.Trace.Add("execute Metadata");
        })
        .Add(SpikeSections.ScannerDecompile, ctx =>
        {
            ctx.Trace.Add("create Decompile");
            ctx.Model.DecompiledSource = "// decompiled source (representative)";
            ctx.Trace.Add("execute Decompile");
        })
        .Add(SpikeSections.ScannerCalls, ctx =>
        {
            var body = ctx.BodyIndex();
            ctx.Trace.Add("create Calls");
            ctx.Model.Calls = body;
            ctx.Trace.Add("execute Calls");
        })
        .Add(SpikeSections.ScannerFacts, ctx =>
        {
            var body = ctx.BodyIndex();
            ctx.Trace.Add("create Facts");
            ctx.Model.Facts = body;
            ctx.Trace.Add("execute Facts");
        });

    /// <summary>
    /// Original Source has a null <c>ScannerKey</c> — its network work runs through this manual
    /// branch, mirroring <c>LibraryMetadataService.InspectAsync</c>'s
    /// <c>GetAuthorizedSections(MayDownloadPdb/MayFetchSources, ...)</c> + bool-branch pattern
    /// rather than through <see cref="CurrentScannerRegistry"/>.
    /// </summary>
    public static async Task RunNetworkWorkAsync(
        SectionPipeline<SpikeModel> pipeline, HashSet<string> include, Verbosity verbosity, CurrentScannerContext context)
    {
        var pdbSections = pipeline.GetAuthorizedSections(SectionCapabilities.MayDownloadPdb, verbosity, include);
        var fetchSections = pipeline.GetAuthorizedSections(SectionCapabilities.MayFetchSources, verbosity, include);
        bool allowPdbDownload = pdbSections.Count > 0;
        bool allowFetchSource = fetchSections.Count > 0;

        if (!allowPdbDownload && !allowFetchSource)
            return;

        if (!context.NetworkAuthorized)
            throw new InvalidOperationException(
                "Original Source network work requires authorization; the section was not explicitly selected.");

        context.Trace.Add("create AcquirePdb");
        context.Model.PdbAcquired = true;
        context.Trace.Add("execute AcquirePdb");

        if (allowFetchSource)
        {
            context.Trace.Add("create FetchSource");
            await Task.Yield();
            context.Model.OriginalSource = "// original source text (representative)";
            context.Trace.Add("execute FetchSource");
        }
    }
}
