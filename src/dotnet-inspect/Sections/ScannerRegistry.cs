using System.Collections.Immutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Sections;

/// <summary>
/// A scanner or typed query requested unbounded work without a matching registry declaration.
/// This is a programming error, not an inspected-artifact failure.
/// </summary>
internal abstract class CostDeclarationException(string message) : Exception(message);

internal sealed class ScannerCostDeclarationException(string message)
    : CostDeclarationException(message);

internal sealed class QueryCostDeclarationException(string message)
    : CostDeclarationException(message);

/// <summary>
/// Shared resource context passed to scanners and typed-query adapters during data collection.
/// </summary>
public sealed class ScannerContext : IDisposable
{
    public required string AssemblyPath { get; init; }
    public ResolvedAssemblyReference? AssemblyReference { get; init; }
    public required LibraryInspection Model { get; init; }
    public required VerboseLogger Logger { get; init; }
    public PdbContext? MetadataContext { get; init; }
    public SourceLinkQueryContext? SourceLinkContext { get; init; }

    /// <summary>
    /// When supplied, records scanner execution and expensive resource acquisition. Null for an
    /// untraced run, which is every run that did not pass <c>--trace</c>.
    /// </summary>
    public InspectionTrace? Trace { get; init; }

    /// <summary>
    /// Analysis features required by the complete scanner and query set. The shared body session
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
    private (WorkKind Kind, string Key, SectionCost Cost)? _runningWork;

    /// <summary>
    /// One metadata session over the assembly, opened on first use and shared by scanners and typed
    /// queries that ask for it through <see cref="Scan{TScan}"/> or <see cref="Query{TResult}"/>.
    ///
    /// This exists for atomicity, not speed. Each of the three scanner fan-out sites that declared
    /// prerequisites replaced held its callees inside one open, so a single run could not mix two
    /// assemblies. Reopening the path per scanner would reintroduce that window: retargeting the
    /// path between opens (a symlink swap, or a build replacing the file) yields an incoherent
    /// result with a zero exit code.
    ///
    /// When <see cref="MetadataContext"/> is present — which is every path the library and package
    /// commands take — the session <em>borrows</em> that already-open image rather than opening
    /// <see cref="AssemblyPath"/> again. So the scanners are coherent not only with each other but
    /// with the assembly identity, presence flags, and debug-directory facts the command read from
    /// the same image. Reopening here would leave that wider window open even though every scanner
    /// shared one session.
    ///
    /// Returns <see langword="null"/> when the session cannot be acquired and retains the
    /// acquisition exception. Scanner adapters use their path overload to preserve their existing
    /// failure mapping. Typed-query adapters map the retained exception directly because reopening
    /// <see cref="AssemblyPath"/> could substitute different content for a supplied
    /// <see cref="MetadataContext"/>.
    ///
    /// Scanners run sequentially (<see cref="ScannerRegistry.RunScanners"/>), so no
    /// synchronization is required.
    ///
    /// Gated by <c>SharedSessionScanners_AllObserveOneSession</c>,
    /// <c>SharedSessionScanners_DoNotObserveAPathRetargetedMidRun</c>,
    /// <c>SharedSessionScanners_ObserveTheImageTheCommandAlreadyOpened</c>, and
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
    /// <paramref name="reopen"/> otherwise, so a scanner keeps its own open-failure mapping.
    /// </summary>
    public TScan Scan<TScan>(
        Func<AssemblyInspectionSession, TScan> shared,
        Func<TScan> reopen)
    {
        if (Session() is not { } session)
            return reopen();

        SharedScanCount++;
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

        SharedScanCount++;
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
            SharedScanCount++;
            return execute(session);
        }

        Exception error = _sessionOpenFailure
            ?? throw new InvalidOperationException(
                "Metadata session acquisition failed without recording an exception.");
        return failed(error);
    }

    /// <summary>
    /// How many scans or typed queries have taken the shared-session branch of
    /// <see cref="Scan{TScan}"/> or <see cref="Query{TResult}"/>.
    ///
    /// This exists so the atomicity property above can be gated rather than asserted. A scanner
    /// that reverts to opening <see cref="AssemblyPath"/> itself still produces correct-looking
    /// output, so nothing else in the suite would notice; the count drops, and
    /// <c>SharedSessionScanners_AllObserveOneSession</c> fails.
    /// </summary>
    public int SharedScanCount { get; private set; }

    /// <summary>
    /// Enters a scanner's resource declaration for the duration of its executor.
    /// </summary>
    internal IDisposable EnterScanner(string key, SectionCost cost)
        => EnterWork(WorkKind.Scanner, key, cost);

    /// <summary>
    /// Enters a typed query's resource declaration for the duration of its executor.
    /// </summary>
    internal IDisposable EnterQuery(string key, SectionCost cost)
        => EnterWork(WorkKind.Query, key, cost);

    private IDisposable EnterWork(WorkKind kind, string key, SectionCost cost)
    {
        var outer = _runningWork;
        _runningWork = (kind, key, cost);
        return new WorkScope(this, outer);
    }

    /// <summary>
    /// Refuses a shared resource to a scanner or query that did not declare it could afford one.
    ///
    /// The body index is a whole-assembly IL build. The registry cannot see that an executor
    /// touches it because <see cref="BodyIndex"/> is handed over as a lazily-invoked method group
    /// — which is exactly how four scanners once declared <see cref="SectionCost.NetworkFree"/>
    /// while doing unbounded work.
    ///
    /// So the declaration is enforced where the cost is actually incurred. Adding a body-index
    /// call to a producer that still claims to be cheap fails loudly instead of quietly restoring
    /// the defect. Gates:
    /// <c>SectionPipelineTests.Scanner_CannotTakeTheBodyIndexWithoutDeclaringItsCost</c> and
    /// <c>SectionPipelineTests.Query_CannotTakeTheBodyIndexWithoutDeclaringItsCost</c>.
    /// </summary>
    private void RequireUnboundedDeclaration(string resource)
    {
        if (_runningWork is not { } running || running.Cost == SectionCost.Unbounded)
            return;

        string message =
            $"{running.Kind} '{running.Key}' declares Cost={running.Cost} but asked for the " +
            $"{resource}, which is unbounded whole-assembly work. Register it with " +
            $"SectionCost.Unbounded, or stop taking the {resource}.";
        throw running.Kind switch
        {
            WorkKind.Scanner => new ScannerCostDeclarationException(message),
            WorkKind.Query => new QueryCostDeclarationException(message),
            _ => throw new InvalidOperationException($"Unknown inspection work kind '{running.Kind}'."),
        };
    }

    private sealed class WorkScope(
        ScannerContext context,
        (WorkKind Kind, string Key, SectionCost Cost)? outer) : IDisposable
    {
        private ScannerContext? _context = context;

        public void Dispose()
        {
            if (_context is not { } current)
                return;

            current._runningWork = outer;
            _context = null;
        }
    }

    private enum WorkKind
    {
        Scanner,
        Query,
    }

    public void Dispose() => _session?.Dispose();

    /// <summary>
    /// Shared method-body analysis index for <see cref="AssemblyPath"/>, built once on first use.
    /// Body-index queries and scanners share it instead of each rebuilding the full
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
                assembly: AssemblyReference);
        }
        catch (Exception ex)
        {
            // Residual scanners map a failed index to an empty section while queries retain a
            // typed failure. Either way the trace must distinguish attempted acquisition from a
            // run that correctly never needed the index.
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

/// <summary>
/// Registry of named scanners for the library command.
/// Each scanner is a function that populates part of a <see cref="LibraryInspection"/>
/// model. Scanners are registered by key and invoked only when needed.
/// </summary>
public sealed class ScannerRegistry
{
    private readonly Dictionary<string, Action<ScannerContext>?> _scanners = [];
    private readonly Dictionary<string, ImmutableArray<string>> _requires = [];
    private readonly Dictionary<string, SectionCost> _costs = [];

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
    /// Registers a scanner by key.
    /// </summary>
    /// <param name="cost">
    /// What this scanner costs to run. Required rather than optional because the defect this
    /// replaced was a silent default: four scanners doing whole-assembly IL work sat at
    /// <see cref="SectionCost.NetworkFree"/> because nothing forced anyone to say otherwise, and
    /// they accounted for 99% of the time a <c>-v:d</c> run spent scanning.
    ///
    /// The declaration is load-bearing, not documentation: a scanner may only reach
    /// <see cref="ScannerContext.BodyIndex"/> or <see cref="ScannerContext.DrillMap"/> if it
    /// declares <see cref="SectionCost.Unbounded"/>, and asking without declaring throws.
    /// </param>
    /// <param name="requires">
    /// Keys of scanners whose output this scanner reads. Declaring a prerequisite is the only
    /// supported way to depend on another scanner's work: <see cref="RunScanners"/> runs
    /// prerequisites first and runs every scanner at most once per context. A scanner that calls
    /// another scanner directly instead hides the dependency from
    /// <see cref="ExpandRequired"/>, so cost and ordering stop being computable from the
    /// registry. Gate: <c>ScannerRegistryTests.LibraryScanners_DeclareTheirPrerequisites</c>.
    /// </param>
    public ScannerRegistry Add(string key, SectionCost cost, Action<ScannerContext> scan, params string[] requires)
    {
        RejectReregistration(key);
        _scanners[key] = scan;
        _requires[key] = [.. requires];
        _costs[key] = cost;
        return this;
    }

    /// <summary>
    /// Registers a key that performs no work of its own and exists only to pull in
    /// <paramref name="requires"/>. A section whose data comes from several scanners binds to a
    /// bundle, because <see cref="ISectionDescriptor{TModel}.ScannerKey"/> names a single key.
    ///
    /// A bundle takes no cost argument: it does no work, so its cost is whatever its prerequisites
    /// cost, and <see cref="CostOf"/> derives that. Letting a bundle declare its own cost would
    /// allow it to under-state the work it pulls in, which is the exact failure this change exists
    /// to remove.
    /// </summary>
    public ScannerRegistry AddBundle(string key, params string[] requires)
    {
        RejectReregistration(key);
        _scanners[key] = null;
        _requires[key] = [.. requires];
        return this;
    }

    /// <summary>
    /// A key may be registered once. Re-registration is rejected because
    /// <see cref="SectionPipeline{TModel}.Add"/> snapshots the scanner's cost into the entry when
    /// the section is registered: if a later <see cref="Add"/> could raise the cost of a key that
    /// already has entries bound to it, the registry and the pipeline would disagree, and the
    /// pipeline is the one the verbosity ladder reads. The section would keep auto-rendering at
    /// its stale cheap cost while <see cref="CostOf"/> reported the truth.
    ///
    /// Raised as blocking by the GPT review of #3626, which demonstrated the divergence: register
    /// NetworkFree, add the entry, re-register Unbounded, and <c>SectionCosts</c> still says
    /// NetworkFree. Making a key's cost immutable once declared is what lets the effective axis
    /// subsume the scanner axis unconditionally rather than only for the current construction
    /// order. Gate: <c>SectionPipelineTests.ScannerKey_CannotBeRegisteredTwice</c>.
    /// </summary>
    private void RejectReregistration(string key)
    {
        if (_scanners.ContainsKey(key))
            throw new InvalidOperationException(
                $"Scanner '{key}' is already registered. A scanner key may be registered once: " +
                "sections snapshot the declared cost when they are added, so re-registering a key " +
                "would let the pipeline keep a stale cost that the verbosity ladder still reads.");
    }

    /// <summary>
    /// What running <paramref name="key"/> costs, taken as the maximum over its transitive
    /// prerequisite closure. A scanner cannot be cheaper than the work it pulls in, so a bundle of
    /// one cheap and one unbounded prerequisite is unbounded.
    ///
    /// Throws on an unregistered key rather than answering <see cref="SectionCost.NetworkFree"/>.
    /// A stale or misspelled <c>ISectionDescriptor.ScannerKey</c> would otherwise resolve to the
    /// cheapest tier and quietly return its section to the <c>-v:d</c> ladder -- reintroducing the
    /// exact under-declaration this type exists to prevent, and doing it silently.
    /// Gate: <c>SectionPipelineTests.CostOf_ThrowsOnAnUnregisteredScannerKey</c>.
    /// </summary>
    public SectionCost CostOf(string key)
    {
        if (!_scanners.ContainsKey(key))
            throw new InvalidOperationException(
                $"No scanner is registered for key '{key}', so its cost cannot be determined. " +
                "A section declaring this ScannerKey would silently keep the cheapest cost.");

        // A bundle legitimately has no cost of its own -- it is exactly what it pulls in. A real
        // scanner without one would resolve to the cheapest tier by omission, which is the same
        // silent under-declaration as an unknown key. Add requires a cost today, so this can only
        // fire if another registration path appears; that is the point.
        if (_scanners[key] is not null && !_costs.ContainsKey(key))
            throw new InvalidOperationException(
                $"Scanner '{key}' was registered without a declared cost.");

        var cost = SectionCost.NetworkFree;
        foreach (var member in ExpandRequired([key]))
        {
            if (_costs.TryGetValue(member, out var declared) && declared > cost)
                cost = declared;
        }

        return cost;
    }

    /// <summary>
    /// The prerequisite keys declared by <paramref name="key"/>, or an empty array when it has
    /// none. Exposed so a test can assert the declared graph rather than infer it from behavior.
    ///
    /// Returns <see cref="ImmutableArray{T}"/> rather than <see cref="IReadOnlyList{T}"/> because
    /// a read-only interface over a <c>string[]</c> can be cast back to the array and mutated.
    /// The prerequisite closure is an input to <see cref="CostOf"/>, and sections snapshot that
    /// cost when they are added, so a caller able to edit this list after the fact could raise a
    /// section's real cost while the pipeline kept auto-rendering it at the cheap cost it
    /// recorded. Raised as blocking by the GPT review of #3626, which mutated both the caller's
    /// original <c>params</c> array and this accessor's return value to produce exactly that
    /// divergence. <see cref="Add"/> and <see cref="AddBundle"/> copy on registration for the same
    /// reason. Gate: <c>SectionPipelineTests.PrerequisiteList_CannotBeMutatedAfterRegistration</c>.
    ///
    /// The boundary this holds against is ordinary calling code, not deliberate subversion:
    /// <c>ImmutableCollectionsMarshal.AsArray</c> still reaches the backing store, as would
    /// reflection or unsafe code. That is out of scope by the same reasoning that applies to every
    /// other <see cref="ImmutableArray{T}"/> accessor in this codebase, and the threat model
    /// (<c>docs/design/untrusted-data-threat-model.md</c>) draws its boundaries around
    /// artifact-derived data rather than in-process callers.
    /// </summary>
    public ImmutableArray<string> RequirementsOf(string key)
        => _requires.TryGetValue(key, out var r) ? r : [];

    /// <summary>
    /// Expands <paramref name="requested"/> to include every transitively required scanner.
    /// Callers that reason about the work a run will do — notably the body-analysis feature
    /// selection, which must see a prerequisite that opens the body index — must expand first, or
    /// they will under-count the run. Throws on a prerequisite cycle, matching
    /// <see cref="RunScanners"/>: expansion runs first in
    /// <c>LibraryMetadataService.InspectAsync</c>, so if only the run threw, a cycle would be
    /// reported from the later of the two places that can see it.
    /// Gate: <c>SectionPipelineTests.ExpandRequired_ThrowsOnPrerequisiteCycle</c>.
    /// </summary>
    public HashSet<string> ExpandRequired(IEnumerable<string> requested)
    {
        HashSet<string> closure = new(StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);
        foreach (var key in requested)
            AddWithRequirements(key, closure, visiting);
        return closure;
    }

    private void AddWithRequirements(string key, HashSet<string> closure, HashSet<string> visiting)
    {
        // Check the visiting stack before the closure: a key already in the closure is merely
        // shared (a diamond), but a key still being visited is a cycle. Testing the closure first
        // would return early and let the cycle through.
        if (!visiting.Add(key))
            throw new InvalidOperationException(
                $"Scanner prerequisite cycle detected at '{key}'.");

        if (closure.Add(key))
        {
            foreach (var required in RequirementsOf(key))
            {
                RequireRegistered(key, required);
                AddWithRequirements(required, closure, visiting);
            }
        }

        visiting.Remove(key);
    }

    /// <summary>
    /// Rejects a declared prerequisite that names no registered scanner.
    ///
    /// A <em>requested</em> key with no registration is skipped on purpose: callers derive requests
    /// from section descriptors across several registries, and the library registry's own
    /// supply/demand equality is held by a separate gate. A <em>declared prerequisite</em> is not
    /// the same thing — it is written next to the scanner that needs it, so a typo or a rename
    /// silently drops a dependency the scanner is relying on and leaves output that looks correct.
    ///
    /// Gate: <c>ExpandRequired_ThrowsOnUnregisteredPrerequisite</c>.
    /// </summary>
    private void RequireRegistered(string key, string required)
    {
        if (!_scanners.ContainsKey(required))
            throw new InvalidOperationException(
                $"Scanner '{key}' requires '{required}', which is not registered.");
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
        {
            RequireRegistered(key, required);
            RunWithRequirements(required, context, ran, running);
        }

        running.Remove(key);
        ran.Add(key);

        if (scan is null)
        {
            context.Trace?.Time(key, isBundle: true, () => { });
            return;
        }

        using IDisposable declaration = context.EnterScanner(key, _costs[key]);
        if (context.Trace is { } trace)
            trace.Time(key, isBundle: false, () => scan(context));
        else
            scan(context);
    }
}
