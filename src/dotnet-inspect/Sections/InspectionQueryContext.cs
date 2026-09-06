using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Sections;

/// <summary>
/// A typed query requested unbounded work without a matching declaration.
/// This is a programming error, not an inspected-artifact failure.
/// </summary>
internal abstract class CostDeclarationException(string message) : Exception(message);

internal sealed class QueryCostDeclarationException(string message)
    : CostDeclarationException(message);

/// <summary>
/// Shared resource context passed to typed-query adapters during data collection.
/// </summary>
public sealed class InspectionQueryContext : IDisposable
{
    public required string AssemblyPath { get; init; }
    public ResolvedAssemblyReference? AssemblyReference { get; init; }
    public IAssemblyReferenceResolver? BodyReferenceResolver { get; init; }
    public required LibraryInspection Model { get; init; }
    public required VerboseLogger Logger { get; init; }
    public PdbContext? MetadataContext { get; init; }
    public SourceLinkQueryContext? SourceLinkContext { get; init; }
    public MetadataRootKind MetadataRoot { get; init; } = MetadataRootKind.Cli;

    /// <summary>
    /// When supplied, records query execution and expensive resource acquisition. Null for an
    /// untraced run, which is every run that did not pass <c>--trace</c>.
    /// </summary>
    public InspectionTrace? Trace { get; init; }

    /// <summary>
    /// Analysis features required by the complete query set. The shared body session
    /// computes their union once, so Array Pool Escapes can share acquisition with
    /// leverage/performance producers without making a resource-only request pay for unrelated
    /// body evidence.
    /// </summary>
    public Analysis.LibraryBodyAnalysisFeatures BodyAnalysisFeatures { get; init; }
        = Analysis.LibraryBodyAnalysisFeatures.Default;

    private MethodBodyInspectionSession? _bodySession;
    private AssemblyInspectionSession? _session;
    private Exception? _sessionOpenFailure;
    private bool _sessionOpenAttempted;
    private Dictionary<int, (string? Stable, string Visibility, string Selector)>?
        _drillMap;
    private (string Key, SectionCost Cost)? _runningWork;

    /// <summary>
    /// One metadata session over the assembly, opened on first use and shared by typed
    /// queries that ask for it through <see cref="Scan{TScan}"/> or <see cref="Query{TResult}"/>.
    ///
    /// This exists for atomicity, not speed. Query adapters share one open image so a single run
    /// cannot mix two assemblies. Reopening the path per query would reintroduce that window:
    /// retargeting the
    /// path between opens (a symlink swap, or a build replacing the file) yields an incoherent
    /// result with a zero exit code.
    ///
    /// When <see cref="MetadataContext"/> is present — which is every path the library and package
    /// commands take — the session <em>borrows</em> that already-open image rather than opening
    /// <see cref="AssemblyPath"/> again. So queries are coherent not only with each other but
    /// with the assembly identity, presence flags, and debug-directory facts the command read from
    /// the same image.
    ///
    /// Returns <see langword="null"/> when the session cannot be acquired and retains the
    /// acquisition exception. Adapters with compatibility fallback behavior use their path
    /// overload; other typed-query adapters map the retained exception directly because reopening
    /// <see cref="AssemblyPath"/> could substitute different content for a supplied
    /// <see cref="MetadataContext"/>.
    ///
    /// Queries run sequentially, so no synchronization is required.
    ///
    /// Gated by <c>ClassifiedAndAuditQueries_ObserveOneSession</c>,
    /// <c>SharedSessionQueries_DoNotObserveAPathRetargetedMidRun</c>,
    /// <c>SharedSessionQueries_ObserveTheImageTheCommandAlreadyOpened</c>, and
    /// <c>ResourcesQuery_OpenFailureRemainsTyped</c>,
    /// <c>ResourcesQuery_RetainedImageFailureDoesNotReopenPath</c>, and
    /// <c>TypeForwardersQuery_OpenFailureRemainsTyped</c>, and
    /// <c>TypeForwardersQuery_RetainedImageFailureDoesNotReopenPath</c>.
    /// </summary>
    public AssemblyInspectionSession? Session()
    {
        if (_sessionOpenAttempted)
            return _session;

        _sessionOpenAttempted = true;
        try
        {
            if (MetadataContext is { } context)
            {
                _session = AssemblyInspectionSession.Borrow(context);
                Trace?.RecordResource(
                    "metadata session",
                    new InertString(TextPolicy.Field, "borrowed from the command's open image"));
            }
            else
            {
                _session = AssemblyInspectionSession.Open(AssemblyPath);
                Trace?.RecordResource(
                    "metadata session",
                    new InertString(TextPolicy.Field, "opened (no shared image available)"));
            }
        }
        catch (Exception ex)
        {
            _session = null;
            _sessionOpenFailure = ex;
            Trace?.RecordResource(
                "metadata session",
                new InertString(
                    TextPolicy.Field,
                    "failed to acquire; work maps the acquisition failure"));
        }

        return _session;
    }

    /// <summary>
    /// Runs <paramref name="shared"/> against the shared session when the assembly opened, and
    /// <paramref name="reopen"/> otherwise, so an adapter keeps its compatibility failure mapping.
    /// </summary>
    public TScan Scan<TScan>(
        Func<AssemblyInspectionSession, TScan> shared,
        Func<TScan> reopen)
    {
        if (Session() is not { } session)
            return reopen();

        SharedQueryCount++;
        return shared(session);
    }

    /// <inheritdoc cref="Scan{TScan}"/>
    public void Scan(Action<AssemblyInspectionSession> shared, Action reopen)
    {
        if (Session() is not { } session)
        {
            reopen();
            return;
        }

        SharedQueryCount++;
        shared(session);
    }

    /// <summary>
    /// Runs a typed query against the shared session, or maps the original acquisition failure
    /// without reopening <see cref="AssemblyPath"/>.
    /// </summary>
    public TResult Query<TResult>(
        Func<AssemblyInspectionSession, TResult> execute,
        Func<Exception, TResult> failed)
    {
        if (Session() is { } session)
        {
            SharedQueryCount++;
            return execute(session);
        }

        Exception error = _sessionOpenFailure
            ?? throw new InvalidOperationException(
                "Metadata session acquisition failed without recording an exception.");
        return failed(error);
    }

    /// <summary>
    /// How many typed query adapters have taken the shared-session branch of
    /// <see cref="Scan{TScan}"/> or <see cref="Query{TResult}"/>.
    ///
    /// This exists so the atomicity property above can be gated rather than asserted. An adapter
    /// that reverts to opening <see cref="AssemblyPath"/> itself still produces correct-looking
    /// output, so nothing else in the suite would notice; the count drops, and
    /// <c>SharedSessionQueries_AllObserveOneSession</c> fails.
    /// </summary>
    public int SharedQueryCount { get; private set; }

    /// <summary>
    /// Enters a typed query's resource declaration for the duration of its executor.
    /// </summary>
    internal IDisposable EnterQuery(string key, SectionCost cost)
        => EnterWork(key, cost);

    private IDisposable EnterWork(string key, SectionCost cost)
    {
        var outer = _runningWork;
        _runningWork = (key, cost);
        return new WorkScope(this, outer);
    }

    /// <summary>
    /// Refuses a shared resource to a query that did not declare it could afford one.
    ///
    /// The body index is a whole-assembly IL build. The registry cannot see that an executor
    /// touches it because <see cref="BodyIndex"/> is handed over as a lazily-invoked method group
    /// so the declaration is enforced at acquisition rather than inferred from delegate shape.
    ///
    /// So the declaration is enforced where the cost is actually incurred. Adding a body-index
    /// call to a producer that still claims to be cheap fails loudly instead of quietly restoring
    /// the defect. Gates:
    /// <c>SectionPipelineTests.Query_CannotTakeTheBodyIndexWithoutDeclaringItsCost</c>.
    /// </summary>
    private void RequireUnboundedDeclaration(string resource)
    {
        if (_runningWork is not { } running || running.Cost == SectionCost.Unbounded)
            return;

        string message =
            $"Query '{running.Key}' declares Cost={running.Cost} but asked for the " +
            $"{resource}, which is unbounded whole-assembly work. Register it with " +
            $"SectionCost.Unbounded, or stop taking the {resource}.";
        throw new QueryCostDeclarationException(message);
    }

    private sealed class WorkScope(
        InspectionQueryContext context,
        (string Key, SectionCost Cost)? outer) : IDisposable
    {
        private InspectionQueryContext? _context = context;

        public void Dispose()
        {
            if (_context is not { } current)
                return;

            current._runningWork = outer;
            _context = null;
        }
    }

    public void Dispose() => _session?.Dispose();

    /// <summary>
    /// Shared method-body analysis index for <see cref="AssemblyPath"/>, built once on first use.
    /// Body-index queries share it instead of each rebuilding the full
    /// <c>LibraryBodyIndex</c>. Work runs sequentially, so no synchronization is required. The
    /// build is narrowed to the phases the requested work consumes (see
    /// <see cref="BodyAnalysisFeatures"/>).
    /// </summary>
    public Analysis.LibraryBodyIndex BodyIndex()
    {
        RequireUnboundedDeclaration("body index");
        if (_bodySession is not null)
            return _bodySession.BodyIndex;

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            _bodySession = MethodBodyInspectionSession.OpenWithPrefetchedImage(
                AssemblyPath,
                GetMetadataContext(),
                BodyAnalysisFeatures,
                BodyReferenceResolver,
                assembly: AssemblyReference);
        }
        catch (Exception ex)
        {
            // The trace must distinguish attempted acquisition from a run that correctly never
            // needed the index.
            Trace?.RecordResource(
                "body index",
                InertString.Format(
                    TextPolicy.Field,
                    $"FAILED after {Elapsed(start)}: {ex.GetType().Name}"));
            throw;
        }

        var index = _bodySession.BodyIndex;
        Trace?.RecordResource(
            "body index",
            InertString.Format(
                TextPolicy.Field,
                $"built in {Elapsed(start)} (features: {BodyAnalysisFeatures})"));
        return index;
    }

    private static string Elapsed(long start)
        => $"{System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms";

    /// <summary>
    /// Stable member drill coordinates, derived once from the command's shared
    /// metadata reader.
    /// </summary>
    public IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>
        DrillMap()
    {
        RequireUnboundedDeclaration("drill map");
        if (_drillMap is not null)
            return _drillMap;

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _drillMap = LibraryMetadataService.BuildLibraryDrillMap(GetMetadataContext(), Logger);
        Trace?.RecordResource(
            "drill map",
            InertString.Format(
                TextPolicy.Field,
                $"built in {Elapsed(start)} ({_drillMap.Count} members)"));
        return _drillMap;
    }

    PdbContext GetMetadataContext()
        => MetadataContext
            ?? throw new InvalidOperationException(
                "A shared metadata context is required by this inspection producer.");
}
