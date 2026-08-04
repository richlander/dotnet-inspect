using System.Collections.Immutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Sections;

/// <summary>
/// A scanner requested unbounded work without the registry-owned declaration that authorizes it.
/// This is a programming error, not an inspected-artifact failure.
/// </summary>
internal sealed class ScannerCostDeclarationException(string message) : Exception(message);

/// <summary>
/// Context passed to each scanner during data collection.
/// </summary>
public sealed class ScannerContext : IDisposable
{
    public required string AssemblyPath { get; init; }
    public required LibraryInspection Model { get; init; }
    public required VerboseLogger Logger { get; init; }
    public PdbContext? MetadataContext { get; init; }

    /// <summary>
    /// When supplied, records scanner execution and expensive resource acquisition. Null for an
    /// untraced run, which is every run that did not pass <c>--trace</c>.
    /// </summary>
    public InspectionTrace? Trace { get; init; }

    /// <summary>
    /// Analysis features required by the complete scanner set. The shared body session computes
    /// their union once, so Array Pool Escapes can share acquisition with leverage/performance scans
    /// without making a resource-only request pay for unrelated body evidence.
    /// </summary>
    public Analysis.LibraryBodyAnalysisFeatures BodyAnalysisFeatures { get; init; }
        = Analysis.LibraryBodyAnalysisFeatures.Default;

    private MethodBodyInspectionSession? _bodySession;
    private AssemblyInspectionSession? _session;
    private bool _sessionOpenAttempted;
    private Dictionary<int, (string? Stable, string Visibility, string Selector)>?
        _drillMap;
    private ScannerRegistry.ScannerAuthorization? _scannerAuthorization;

    /// <summary>
    /// One metadata session over the assembly, opened on first use and shared by the scanners that
    /// ask for it through <see cref="Scan{TScan}"/>.
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
    /// Returns <see langword="null"/> when the assembly cannot be opened, so the caller falls back
    /// to the path-based overload. That is deliberate: each path overload maps its own open
    /// failure onto its own inspection type, and reproducing those mappings here would duplicate
    /// them. The fallback reopens and fails again, which costs an extra open only on a path that is
    /// already failing.
    ///
    /// Scanners run sequentially (<see cref="ScannerRegistry.RunScanners"/>), so no
    /// synchronization is required.
    ///
    /// Gated by <c>SharedSessionScanners_AllObserveOneSession</c>,
    /// <c>SharedSessionScanners_DoNotObserveAPathRetargetedMidRun</c>,
    /// <c>SharedSessionScanners_ObserveTheImageTheCommandAlreadyOpened</c>, and
    /// <c>SharedSession_FallsBackToReopenWhenAssemblyCannotBeOpened</c>.
    /// </summary>
    public AssemblyInspectionSession? Session()
    {
        if (_sessionOpenAttempted)
            return _session;

        _sessionOpenAttempted = true;
        try
        {
            if (MetadataContext is { HasMetadata: true } context)
            {
                _session = AssemblyInspectionSession.Borrow(context);
                Trace?.RecordResource("metadata session", "borrowed from the command's open image");
            }
            else
            {
                _session = AssemblyInspectionSession.Open(AssemblyPath);
                Trace?.RecordResource("metadata session", "opened (no shared image available)");
            }
        }
        catch (Exception)
        {
            // Left to the fallback path overload, which logs and produces the failed inspection.
            _session = null;
            Trace?.RecordResource("metadata session", "failed to open; scanners reopen individually");
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
    /// How many scans have taken the shared-session branch of <see cref="Scan{TScan}"/>.
    ///
    /// This exists so the atomicity property above can be gated rather than asserted. A scanner
    /// that reverts to opening <see cref="AssemblyPath"/> itself still produces correct-looking
    /// output, so nothing else in the suite would notice; the count drops, and
    /// <c>SharedSessionScanners_AllObserveOneSession</c> fails.
    /// </summary>
    public int SharedScanCount { get; private set; }

    /// <summary>
    /// Applies the registry-owned authorization for one scanner invocation. The authorization's
    /// constructor is private to <see cref="ScannerRegistry"/>, so scanner code cannot promote
    /// itself by writing an <see cref="SectionCost.Unbounded"/> value into the context.
    /// </summary>
    internal IDisposable AuthorizeScanner(ScannerRegistry.ScannerAuthorization authorization)
    {
        if (_scannerAuthorization is not null)
        {
            throw new ScannerCostDeclarationException(
                "A scanner tried to start a nested scanner run on the active context. " +
                "Scanner authorization cannot be replaced while a scanner is running.");
        }

        var outer = _scannerAuthorization;
        _scannerAuthorization = authorization;
        return new ScannerAuthorizationScope(this, outer);
    }

    /// <summary>
    /// Refuses a shared resource to a caller that did not declare it could afford one.
    ///
    /// The body index is a whole-assembly IL build: measured at 1.4 s on a 1.7 MB assembly, and the
    /// two scanners that consume it account for 99% of the time a <c>-v:d</c> run spends scanning.
    /// The registry cannot see that a scanner touches it, because <see cref="BodyIndex"/> is handed
    /// over as a lazily-invoked method group — which is exactly how four scanners came to declare
    /// <see cref="SectionCost.NetworkFree"/> while doing unbounded work.
    ///
    /// So the declaration is enforced where the cost is actually incurred. Adding a body-index call
    /// to a scanner that still claims to be cheap fails loudly instead of quietly restoring the
    /// defect. Gate: <c>SectionPipelineTests.Scanner_CannotTakeTheBodyIndexWithoutDeclaringItsCost</c>.
    ///
    /// A caller outside a scanner run is refused rather than allowed. This branch used to return,
    /// on the reasoning that such a caller "has no declaration to check against" — but that made
    /// the absence of a declaration the one way to escape needing one. The GPT review of #3626
    /// exploited it in ordinary code: a descriptor's <c>CanRender</c> that captured the context
    /// called <see cref="BodyIndex"/> while rendering, spending seconds with no check, because the
    /// scanner authorization is removed once the run ends. Cost is declared per scanner, so work
    /// that is not attributable to one cannot be afforded by anything.
    /// Gate: <c>SectionPipelineTests.UnscopedCallers_AreRefusedTheBodyIndex</c>.
    /// </summary>
    private void RequireUnboundedDeclaration(string resource)
    {
        if (_scannerAuthorization is not { } authorization)
        {
            throw new ScannerCostDeclarationException(
                $"The {resource} was requested outside a scanner run, so no cost declaration " +
                $"covers it. It is unbounded whole-assembly work and must be attributed to a " +
                $"scanner registered with SectionCost.Unbounded.");
        }

        if (authorization.Cost == SectionCost.Unbounded)
            return;

        throw new ScannerCostDeclarationException(
            $"Scanner '{authorization.Key}' declares Cost={authorization.Cost} but asked for the {resource}, " +
            $"which is unbounded whole-assembly work. Register it with SectionCost.Unbounded, or " +
            $"stop taking the {resource}.");
    }

    private sealed class ScannerAuthorizationScope(
        ScannerContext context,
        ScannerRegistry.ScannerAuthorization? outer) : IDisposable
    {
        private ScannerContext? _context = context;

        public void Dispose()
        {
            if (_context is not { } current)
                return;

            current._scannerAuthorization = outer;
            _context = null;
        }
    }

    public void Dispose() => _session?.Dispose();

    /// <summary>
    /// Shared method-body analysis index for <see cref="AssemblyPath"/>, built once on first use.
    /// The body-index scanners (unsafe members, top leverage, optimization opportunities) share it
    /// instead of each rebuilding the full <c>LibraryBodyIndex</c>. Scanners run sequentially
    /// (<see cref="ScannerRegistry.RunScanners"/>), so no synchronization is required. The build is
    /// narrowed to the phases the requested scanners consume (see
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
                BodyAnalysisFeatures);
        }
        catch (Exception ex)
        {
            // Scanners swallow a failed index and render an empty section, so without this the
            // trace would show no body index for a run that tried to build one and failed —
            // indistinguishable from a run that correctly never needed it.
            Trace?.RecordResource("body index", $"FAILED after {Elapsed(start)}: {ex.GetType().Name}");
            throw;
        }

        var index = _bodySession.BodyIndex;
        Trace?.RecordResource(
            "body index",
            $"built in {Elapsed(start)} (features: {BodyAnalysisFeatures})");
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
        _drillMap = BuildDrillMap(GetMetadataContext(), Logger);
        Trace?.RecordResource("drill map", $"built in {Elapsed(start)} ({_drillMap.Count} members)");
        return _drillMap;
    }

    /// <summary>
    /// Builds stable member drill coordinates across the whole assembly. This stays private to
    /// the context so scanner code cannot bypass <see cref="DrillMap"/>'s cost gate by invoking
    /// the implementation directly.
    /// Gate: <c>SectionPipelineTests.DrillMapConstruction_IsPrivateToScannerContext</c>.
    /// </summary>
    private static Dictionary<int, (string? Stable, string Visibility, string Selector)>
        BuildDrillMap(
            PdbContext context,
            VerboseLogger logger)
    {
        var map = new Dictionary<int, (string? Stable, string Visibility, string Selector)>();
        try
        {
            if (!context.HasMetadata)
                return map;

            // All-members first (covers non-public, numbered as `--all` drilling resolves them).
            AddSurface(context.ExtractApiSurface(includeAll: true), map);
            // Default surface overwrites public members with their public-only Name:N, which is
            // what `member Name:N` resolves without `--all`.
            AddSurface(context.ExtractApiSurface(includeAll: false), map);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                $"Error building leverage selectors for {context.AssemblyPath}: {ex.Message}");
        }
        return map;

        static void AddSurface(
            ApiSurface surface,
            Dictionary<int, (string? Stable, string Visibility, string Selector)> target)
        {
            foreach (var type in surface.Types)
            {
                foreach (var (token, drill) in ApiOutputFormatter.BuildMemberDrillMap(type))
                    target[token] = drill;
            }
        }
    }

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
    private static readonly object AuthorizationSecret = new();
    private readonly Dictionary<string, Action<ScannerContext>?> _scanners = [];
    private readonly Dictionary<string, ImmutableArray<string>> _requires = [];
    private readonly Dictionary<string, SectionCost> _costs = [];
    private readonly Dictionary<string, ScannerAuthorization> _authorizations = [];

    internal sealed class ScannerAuthorization
    {
        internal ScannerAuthorization(string key, SectionCost cost, object secret)
        {
            if (!ReferenceEquals(secret, AuthorizationSecret))
                throw new InvalidOperationException(
                    "Scanner authorizations can only be created by their registry.");

            Key = key;
            Cost = cost;
        }

        internal string Key { get; }
        internal SectionCost Cost { get; }
    }

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
        _authorizations[key] = new ScannerAuthorization(key, cost, AuthorizationSecret);
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

        // Scoped around the invocation and restored on disposal, so a nested run cannot leave the
        // outer scanner unattributed. The authorization object can only be created by this
        // registry; scanner code receives the context but never the token.
        using IDisposable authorization = context.AuthorizeScanner(_authorizations[key]);
        if (context.Trace is { } trace)
            trace.Time(key, isBundle: false, () => scan(context));
        else
            scan(context);
    }
}
