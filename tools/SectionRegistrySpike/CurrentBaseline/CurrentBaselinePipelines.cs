using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace SectionRegistrySpike.CurrentBaseline;

public sealed class CurrentScannerContext
{
    public required SpikeModel Model { get; init; }
    public Action<string>? Trace { get; init; }
    public int WorkCount { get; private set; }
    public int BodyIndexBuilds { get; private set; }

    private int? _bodyIndex;

    public int BodyIndex()
    {
        if (_bodyIndex is null)
        {
            _bodyIndex = 42;
            BodyIndexBuilds++;
            Record("BodyIndex");
        }

        return _bodyIndex.Value;
    }

    public void Record(string name)
    {
        WorkCount++;
        Trace?.Invoke($"execute {name}");
    }

    public void Reset()
    {
        Model.Reset();
        WorkCount = 0;
        BodyIndexBuilds = 0;
        _bodyIndex = null;
    }
}

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
/// Fair current-shape baseline: the real SectionPipeline plus string-keyed scanner dispatch,
/// context-owned body-index memoization, and separate ordered network branches.
/// </summary>
public static class CurrentBaselinePipelines
{
    public const string ScannerMetadata = "Metadata";
    public const string ScannerDecompile = "Decompile";
    public const string ScannerCalls = "Calls";
    public const string ScannerFacts = "Facts";

    public static SectionPipeline<SpikeModel> CreatePipeline() => new SectionPipeline<SpikeModel>()
        .Add<MetadataSection>(m => m.IsManagedAssembly)
        .Add<DecompiledSourceSection>(m => m.IsManagedAssembly)
        .Add<OriginalSourceSection>(m => m.HasSourceLink)
        .Add<CallsSection>(m => m.HasMethodBodies)
        .Add<FactsSection>(m => m.HasMethodBodies)
        .AddCategory("@Projections", "Calls", "Facts")
        .AddCategory("@Source", "Decompiled Source", "Original Source");

    public static CurrentScannerRegistry CreateScannerRegistry() => new CurrentScannerRegistry()
        .Add(ScannerMetadata, ctx =>
        {
            ctx.Model.MetadataLoaded = true;
            ctx.Record("Metadata");
        })
        .Add(ScannerDecompile, ctx =>
        {
            ctx.Model.DecompiledSource = "// decompiled source (representative)";
            ctx.Record("Decompile");
        })
        .Add(ScannerCalls, ctx =>
        {
            ctx.Model.Calls = ctx.BodyIndex();
            ctx.Record("Calls");
        })
        .Add(ScannerFacts, ctx =>
        {
            ctx.Model.Facts = ctx.BodyIndex();
            ctx.Record("Facts");
        });

    public static async Task RunNetworkWorkAsync(
        SectionPipeline<SpikeModel> pipeline,
        HashSet<string> include,
        Verbosity verbosity,
        CurrentScannerContext context)
    {
        bool allowPdbDownload =
            pipeline.GetAuthorizedSections(SectionCapabilities.MayDownloadPdb, verbosity, include).Count > 0;
        bool allowFetchSource =
            pipeline.GetAuthorizedSections(SectionCapabilities.MayFetchSources, verbosity, include).Count > 0;

        if (!allowPdbDownload)
            return;

        context.Model.PdbAcquired = true;
        context.Record("AcquirePdb");

        if (!allowFetchSource)
            return;

        await Task.Yield();
        context.Model.OriginalSource = "// original source text (representative)";
        context.Record("FetchSource");
    }

    private readonly struct MetadataSection : ISectionDescriptor<SpikeModel>
    {
        public static string Name => "Metadata";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static string? ScannerKey => ScannerMetadata;
        public static bool CanRender(SpikeModel model) => model.MetadataLoaded;
    }

    private readonly struct DecompiledSourceSection : ISectionDescriptor<SpikeModel>
    {
        public static string Name => "Decompiled Source";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => ScannerDecompile;
        public static bool CanRender(SpikeModel model) => model.DecompiledSource != null;
    }

    private readonly struct OriginalSourceSection : ISectionDescriptor<SpikeModel>
    {
        public static string Name => "Original Source";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static SectionCapabilities Capabilities =>
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources;
        public static string? ScannerKey => null;
        public static bool CanRender(SpikeModel model) => model.OriginalSource != null;
    }

    private readonly struct CallsSection : ISectionDescriptor<SpikeModel>
    {
        public static string Name => "Calls";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => ScannerCalls;
        public static bool CanRender(SpikeModel model) => model.Calls > 0;
    }

    private readonly struct FactsSection : ISectionDescriptor<SpikeModel>
    {
        public static string Name => "Facts";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => ScannerFacts;
        public static bool CanRender(SpikeModel model) => model.Facts > 0;
    }
}
