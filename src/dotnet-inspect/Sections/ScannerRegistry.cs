using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
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

    /// <summary>
    /// Whether the shared body session must compute optimization opportunities (and therefore the
    /// allocation occurrences they build on). True only when the Performance Triage scanner is in
    /// the requested set; the other body scanners (unsafe members, top leverage) need neither, so
    /// the index build skips both expensive phases. Defaults to true (compute everything) so an
    /// unset context never silently drops opportunity data.
    /// </summary>
    public bool IncludeOpportunities { get; init; } = true;

    private MethodBodyInspectionSession? _bodySession;

    /// <summary>
    /// Shared method-body analysis index for <see cref="AssemblyPath"/>, built once on first use.
    /// The body-index scanners (unsafe members, top leverage, optimization opportunities) share it
    /// instead of each rebuilding the full <c>LibraryBodyIndex</c>. Scanners run sequentially
    /// (<see cref="ScannerRegistry.RunScanners"/>), so no synchronization is required. The build is
    /// narrowed to the phases the requested scanners consume (see <see cref="IncludeOpportunities"/>).
    /// </summary>
    public Analysis.LibraryBodyIndex BodyIndex() =>
        (_bodySession ??= MethodBodyInspectionSession.Open(
            AssemblyPath, includeAllocations: IncludeOpportunities, includeOpportunities: IncludeOpportunities)).BodyIndex;
}

/// <summary>
/// Registry of named scanners for the library command.
/// Each scanner is a function that populates part of a <see cref="LibraryInspection"/>
/// model. Scanners are registered by key and invoked only when needed.
/// </summary>
public sealed class ScannerRegistry
{
    private readonly Dictionary<string, Action<ScannerContext>> _scanners = [];

    /// <summary>
    /// Registers a scanner by key. The action populates the model with data.
    /// </summary>
    public ScannerRegistry Add(string key, Action<ScannerContext> scan)
    {
        _scanners[key] = scan;
        return this;
    }

    /// <summary>
    /// Runs all scanners whose keys are in the <paramref name="requiredScanners"/> set.
    /// </summary>
    public void RunScanners(HashSet<string> requiredScanners, ScannerContext context)
    {
        foreach (var (key, scan) in _scanners)
        {
            if (requiredScanners.Contains(key))
                scan(context);
        }
    }
}
