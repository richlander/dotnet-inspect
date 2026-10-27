using System.Collections.Immutable;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

/// <summary>One selected, exact Ecosystem registration in Find order.</summary>
public sealed record EcosystemFindLayer(
    WorkspaceEcosystemRegistrationDeclaration Registration,
    bool Platform)
{
    public string? CorePackageId { get; init; }
}

/// <summary>The host-neutral question, selection, demand, and work bounds.</summary>
public sealed class EcosystemFindSearchRequest
{
    public EcosystemFindSearchRequest(
        FindQuestion question,
        IEnumerable<EcosystemFindLayer> layers,
        IEnumerable<WorkspaceEcosystemRegistrationId> prefixDemand,
        int maximumPrefixPackages,
        int? maximumRows = null)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(prefixDemand);
        if (maximumPrefixPackages is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maximumPrefixPackages));
        if (maximumRows is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        Layers = [.. layers];
        if (Layers.IsEmpty || Layers.Any(layer => layer is null))
            throw new ArgumentException("At least one layer is required.", nameof(layers));
        var selected = Layers.Select(layer => layer.Registration.Id).ToHashSet();
        if (selected.Count != Layers.Length)
            throw new ArgumentException("Duplicate Ecosystem registration.", nameof(layers));
        PrefixDemand = [.. prefixDemand];
        if (PrefixDemand.Distinct().Count() != PrefixDemand.Length
            || PrefixDemand.Any(id => !selected.Contains(id)))
            throw new ArgumentException("Prefix demand must select distinct registrations.", nameof(prefixDemand));
        Prefixes = [.. Layers
            .Where(layer => PrefixDemand.Contains(layer.Registration.Id))
            .SelectMany(layer => layer.Registration.Populations
                .OfType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>()
                .Select(population => population.Prefix))
            .DistinctBy(prefix => prefix.Prefix, StringComparer.OrdinalIgnoreCase)];
        if (!Layers.Any(layer => layer.Platform || !layer.Registration.CorePackages.IsEmpty)
            && Prefixes.IsEmpty)
            throw new ArgumentException("Selection contains no searchable population.", nameof(layers));
        Question = question;
        MaximumPrefixPackages = maximumPrefixPackages;
        MaximumRows = maximumRows;
    }

    public Guid Identity { get; } = Guid.NewGuid();
    public FindQuestion Question { get; }
    public ImmutableArray<EcosystemFindLayer> Layers { get; }
    public ImmutableArray<WorkspaceEcosystemRegistrationId> PrefixDemand { get; }
    public ImmutableArray<PackagePrefixDeclaration> Prefixes { get; }
    public int MaximumPrefixPackages { get; }
    public int? MaximumRows { get; }
}

/// <summary>An immutable Find block returned by an adopter's source evaluator.</summary>
public sealed record EcosystemFindBlock<T>(
    T Content,
    int RowCount,
    bool HasFailures = false,
    bool Incomplete = false)
{
    public WorkspaceEcosystemRegistrationId? Ecosystem { get; init; }
    public EcosystemFindCandidate? Candidate { get; init; }
    public int Ordinal { get; init; }
    public IReadOnlyList<WorkspaceEcosystemRegistrationId> Memberships
        { get; init; } = [];
    public string? Failure { get; init; }
}

public sealed record EcosystemFindCandidate(string PackageId, string Version);

public enum EcosystemFindPrefixPageCompletion
{
    Exhausted,
    RequestedLimit,
    SourcePageLimit,
    ClientPageLimit,
    Failed,
    RowLimitReached,
    CandidateLimitReached,
    Continuing,
}

public sealed record EcosystemFindPrefixPage(
    IReadOnlyList<EcosystemFindCandidate> Candidates,
    EcosystemFindPrefixPageCompletion Completion =
        EcosystemFindPrefixPageCompletion.Exhausted,
    string? Failure = null,
    string? FailureSource = null);

public enum EcosystemFindCompletion
{
    Exhausted,
    RowLimitReached,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    Partial,
    Failed,
}

public sealed record EcosystemFindSourceFailure(
    string Phase, string Source, string Message);

public sealed record EcosystemFindPrefixCoverage(
    string Prefix, EcosystemFindPrefixPageCompletion Completion);

public sealed record EcosystemFindSearchSummary<T>(
    Guid RequestIdentity,
    IReadOnlyList<EcosystemFindBlock<T>> BoundedBlocks,
    IReadOnlyList<EcosystemFindBlock<T>> IndexedBlocks,
    IReadOnlyList<EcosystemFindBlock<T>> PrefixBlocks,
    EcosystemFindCompletion Completion,
    IReadOnlyList<string> Unsearched,
    IReadOnlyList<EcosystemFindSourceFailure> Failures,
    IReadOnlyList<EcosystemFindPrefixCoverage> PrefixCoverage,
    bool HasFailures,
    bool Incomplete);

public sealed record EcosystemFindBoundedOutcome<T>(
    EcosystemFindSearchSummary<T>? Completed,
    EcosystemFindPrefixContinuation<T>? Continuation);

public enum EcosystemFindIndexedCompletion
{
    Exhausted,
    RowLimitReached,
}

public sealed record EcosystemFindIndexedOutcome<T>(
    IReadOnlyList<EcosystemFindBlock<T>> Blocks,
    EcosystemFindIndexedCompletion Completion,
    IReadOnlyList<EcosystemFindSourceFailure> Failures);

/// <summary>A single-use capability issued by exactly one staged session.</summary>
public sealed class EcosystemFindPrefixContinuation<T>
{
    internal EcosystemFindPrefixContinuation(
        EcosystemFindSearchSession<T> session,
        int? maximumRemainingRows)
    {
        Session = session;
        MaximumRemainingRows = maximumRemainingRows;
    }

    internal EcosystemFindSearchSession<T> Session { get; }
    internal int Used;
    public Guid RequestIdentity => Session.Request.Identity;
    public int? MaximumRemainingRows { get; }

    public Task<EcosystemFindSearchSummary<T>> ResumeAsync(
        int? remainingRows,
        CancellationToken cancellationToken = default) =>
        Session.ResumeAsync(this, remainingRows, cancellationToken);
}

/// <summary>
/// Owns index-first staging, the bounded barrier, continuation, and work bounds.
/// Adopters supply source acquisition and Find evaluation without host output policy.
/// </summary>
public sealed class EcosystemFindSearchSession<T> : IDisposable
{
    private readonly Func<EcosystemFindLayer, CancellationToken,
        Task<EcosystemFindBlock<T>>> _bounded;
    private readonly Func<PackagePrefixDeclaration, int, CancellationToken,
        IAsyncEnumerable<EcosystemFindPrefixPage>> _enumerate;
    private readonly Func<EcosystemFindCandidate, IReadOnlyList<WorkspaceEcosystemRegistrationId>,
        CancellationToken, Task<EcosystemFindBlock<T>>> _candidate;
    private readonly Func<int?, CancellationToken,
        Task<EcosystemFindIndexedOutcome<T>>>? _indexed;
    private readonly Action<EcosystemFindBlock<T>>? _publish;
    private readonly Func<EcosystemFindLayer, EcosystemFindBlock<T>, CancellationToken,
        EcosystemFindBlock<T>>? _reuseCore;
    private readonly Func<IReadOnlyList<EcosystemFindBlock<T>>, T>? _combineCore;
    private readonly Dictionary<string, EcosystemFindBlock<T>> _coreCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private CancellationTokenRegistration _cancellationRegistration;
    private readonly List<EcosystemFindBlock<T>> _boundedBlocks = [];
    private readonly List<EcosystemFindBlock<T>> _indexedBlocks = [];
    private readonly List<EcosystemFindBlock<T>> _prefixBlocks = [];
    private readonly List<EcosystemFindSourceFailure> _sourceFailures = [];
    private readonly List<EcosystemFindPrefixCoverage> _prefixCoverage = [];
    private bool _started;
    private bool _completed;
    private bool _failures;
    private bool _incomplete;
    private int _rows;

    public EcosystemFindSearchSession(
        EcosystemFindSearchRequest request,
        Func<EcosystemFindLayer, CancellationToken, Task<EcosystemFindBlock<T>>> bounded,
        Func<PackagePrefixDeclaration, int, CancellationToken,
            IAsyncEnumerable<EcosystemFindPrefixPage>> enumerate,
        Func<EcosystemFindCandidate, IReadOnlyList<WorkspaceEcosystemRegistrationId>,
            CancellationToken, Task<EcosystemFindBlock<T>>> candidate,
        Action<EcosystemFindBlock<T>>? publish = null,
        Func<EcosystemFindLayer, EcosystemFindBlock<T>, CancellationToken,
            EcosystemFindBlock<T>>? reuseCore = null,
        Func<IReadOnlyList<EcosystemFindBlock<T>>, T>? combineCore = null,
        Func<int?, CancellationToken,
            Task<EcosystemFindIndexedOutcome<T>>>? indexed = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _lifetimeToken = _lifetime.Token;
        _bounded = bounded ?? throw new ArgumentNullException(nameof(bounded));
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        _indexed = indexed;
        _publish = publish;
        if ((reuseCore is null) != (combineCore is null))
            throw new ArgumentException(
                "Source reuse requires a layer reducer and attribution.");
        _reuseCore = reuseCore;
        _combineCore = combineCore;
    }

    public EcosystemFindSearchRequest Request { get; }

    public async Task<EcosystemFindBoundedOutcome<T>> RunBoundedAsync(
        CancellationToken cancellationToken = default)
    {
        if (_started)
            throw new InvalidOperationException("Bounded work already started.");
        _started = true;
        _cancellationRegistration = cancellationToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            _lifetime);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeToken);
        CancellationToken token = linked.Token;
        if (_indexed is not null)
        {
            (int indexedRows, bool rowLimitReached) =
                await RunIndexedAsync(Request.MaximumRows, token);
            if (rowLimitReached
                || Request.MaximumRows is int indexedLimit
                    && indexedRows >= indexedLimit)
            {
                return new(Complete(
                    EcosystemFindCompletion.RowLimitReached,
                    [.. Request.Layers.Select(
                            layer => layer.Registration.Id.Value),
                     .. Request.Prefixes.Select(prefix => prefix.Prefix)]), null);
            }
        }

        for (int index = 0; index < Request.Layers.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (Request.MaximumRows is int limit && _rows >= limit)
                return new(Complete(
                    EcosystemFindCompletion.RowLimitReached,
                    [.. Request.Layers.Skip(index).Select(layer => layer.Registration.Id.Value),
                     .. Request.Prefixes.Select(prefix => prefix.Prefix)]), null);
            EcosystemFindLayer layer = Request.Layers[index];
            EcosystemFindBlock<T> block;
            if (_reuseCore is not null && !layer.Platform
                && !layer.Registration.CorePackages.IsEmpty)
            {
                var sources = new List<EcosystemFindBlock<T>>();
                foreach (var core in layer.Registration.CorePackages)
                {
                    token.ThrowIfCancellationRequested();
                    if (_coreCache.TryGetValue(core.PackageId, out var cached))
                        sources.Add(_reuseCore(layer, cached, token));
                    else
                    {
                        var source = await _bounded(
                            layer with { CorePackageId = core.PackageId }, token);
                        source = source with
                        {
                            Memberships = PackageMemberships(core.PackageId),
                        };
                        _coreCache.Add(core.PackageId, source);
                        if (source.Failure is { } failure)
                            _sourceFailures.Add(new(
                                "Bounded", core.PackageId, failure));
                        sources.Add(source);
                    }
                }
                block = new(_combineCore!(sources),
                    sources.Sum(source => source.RowCount),
                    sources.Any(source => source.HasFailures),
                    sources.Any(source => source.Incomplete))
                {
                    Memberships =
                    [
                        .. Request.Layers.Select(layer => layer.Registration.Id)
                            .Where(id => sources.Any(source =>
                                source.Memberships.Contains(id))),
                    ],
                };
            }
            else
                block = await _bounded(layer, token);
            Accept(block with
            {
                Ecosystem = Request.Layers[index].Registration.Id,
                Ordinal = index,
            }, _boundedBlocks, token);
        }
        token.ThrowIfCancellationRequested();
        if (Request.MaximumRows is int maximumRows && _rows >= maximumRows
            && !Request.Prefixes.IsEmpty)
            return new(Complete(EcosystemFindCompletion.RowLimitReached,
                [.. Request.Prefixes.Select(prefix => prefix.Prefix)]), null);
        if (Request.Prefixes.IsEmpty)
            return new(Complete(_failures || _incomplete
                ? EcosystemFindCompletion.Partial
                : EcosystemFindCompletion.Exhausted, []), null);
        return new(null, new(this,
            Request.MaximumRows is int maximum ? Math.Max(0, maximum - _rows) : null));
    }

    internal async Task<EcosystemFindSearchSummary<T>> ResumeAsync(
        EcosystemFindPrefixContinuation<T> continuation,
        int? remainingRows,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(continuation.Session, this))
            throw new InvalidOperationException("Continuation belongs to another session.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeToken);
        linked.Token.ThrowIfCancellationRequested();
        if (remainingRows is < 0
            || continuation.MaximumRemainingRows is int maximum
                && (remainingRows is null || remainingRows > maximum))
            throw new ArgumentOutOfRangeException(nameof(remainingRows));
        if (Interlocked.Exchange(ref continuation.Used, 1) != 0 || _completed)
            throw new InvalidOperationException("Continuation has already been consumed.");
        if (remainingRows == 0)
            return Complete(EcosystemFindCompletion.RowLimitReached,
                [.. Request.Prefixes.Select(prefix => prefix.Prefix)]);

        CancellationToken token = linked.Token;
        var seen = Request.Layers.SelectMany(layer =>
            layer.Registration.CorePackages.Select(package => package.PackageId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int candidates = 0;
        int prefixRows = 0;
        EcosystemFindCompletion? sourceLimit = null;
        for (int index = 0; index < Request.Prefixes.Length; index++)
        {
            PackagePrefixDeclaration prefix = Request.Prefixes[index];
            EcosystemFindPrefixPageCompletion sourceCompletion =
                EcosystemFindPrefixPageCompletion.Exhausted;
            token.ThrowIfCancellationRequested();
            if (remainingRows is int rows && prefixRows >= rows)
                return Complete(EcosystemFindCompletion.RowLimitReached,
                    [.. Request.Prefixes.Skip(index).Select(item => item.Prefix)]);
            if (candidates >= Request.MaximumPrefixPackages)
                return Complete(EcosystemFindCompletion.CandidateLimitReached,
                    [.. Request.Prefixes.Skip(index).Select(item => item.Prefix)]);
            int overlap = seen.Count(packageId => packageId.StartsWith(
                prefix.Prefix,
                StringComparison.OrdinalIgnoreCase));
            int sourceTake = checked(
                Request.MaximumPrefixPackages - candidates + overlap);
            await foreach (EcosystemFindPrefixPage page in _enumerate(
                prefix, sourceTake, token)
                .WithCancellation(token))
            {
                token.ThrowIfCancellationRequested();
                if (page.Completion == EcosystemFindPrefixPageCompletion.Failed)
                {
                    _failures = _incomplete = true;
                    _sourceFailures.Add(new("Prefix",
                        page.FailureSource ?? prefix.Prefix,
                        page.Failure ?? "Prefix enumeration failed."));
                    sourceCompletion = page.Completion;
                    continue;
                }
                foreach (EcosystemFindCandidate item in page.Candidates)
                {
                    token.ThrowIfCancellationRequested();
                    if (!seen.Add(item.PackageId))
                        continue;
                    if (remainingRows is int rowLimit && prefixRows >= rowLimit)
                    {
                        _prefixCoverage.Add(new(prefix.Prefix,
                            EcosystemFindPrefixPageCompletion.RowLimitReached));
                        return Complete(EcosystemFindCompletion.RowLimitReached,
                            [.. Request.Prefixes.Skip(index).Select(source => source.Prefix)]);
                    }
                    if (candidates >= Request.MaximumPrefixPackages)
                    {
                        _prefixCoverage.Add(new(prefix.Prefix,
                            EcosystemFindPrefixPageCompletion.CandidateLimitReached));
                        return Complete(EcosystemFindCompletion.CandidateLimitReached,
                            [.. Request.Prefixes.Skip(index).Select(source => source.Prefix)]);
                    }
                    candidates++;
                    WorkspaceEcosystemRegistrationId[] memberships =
                        PackageMemberships(item.PackageId);
                    EcosystemFindBlock<T> block =
                        await _candidate(item, memberships, token);
                    EcosystemFindBlock<T> attributed = block with
                    {
                        Candidate = item,
                        Ordinal = candidates - 1,
                        Memberships = memberships,
                    };
                    if (attributed.RowCount == 0)
                        Observe(attributed, token);
                    else
                    {
                        Accept(attributed, _prefixBlocks, token);
                        prefixRows = checked(
                            prefixRows + attributed.RowCount);
                    }
                }
                if (remainingRows is int completedRowLimit
                    && prefixRows >= completedRowLimit)
                {
                    _prefixCoverage.Add(new(
                        prefix.Prefix,
                        EcosystemFindPrefixPageCompletion.RowLimitReached));
                    return Complete(
                        EcosystemFindCompletion.RowLimitReached,
                        [.. Request.Prefixes.Skip(index)
                            .Select(source => source.Prefix)]);
                }
                if (page.Completion == EcosystemFindPrefixPageCompletion.Continuing
                    && candidates >= Request.MaximumPrefixPackages)
                {
                    _prefixCoverage.Add(new(prefix.Prefix,
                        EcosystemFindPrefixPageCompletion.CandidateLimitReached));
                    return Complete(EcosystemFindCompletion.CandidateLimitReached,
                        [.. Request.Prefixes.Skip(index).Select(source => source.Prefix)]);
                }
                if (page.Completion is EcosystemFindPrefixPageCompletion.SourcePageLimit
                    or EcosystemFindPrefixPageCompletion.ClientPageLimit)
                {
                    _incomplete = true;
                    sourceLimit ??= page.Completion == EcosystemFindPrefixPageCompletion.SourcePageLimit
                        ? EcosystemFindCompletion.SourcePageLimitReached
                        : EcosystemFindCompletion.ClientPageLimitReached;
                    sourceCompletion = page.Completion;
                    break;
                }
                if (page.Completion == EcosystemFindPrefixPageCompletion.RequestedLimit)
                {
                    _prefixCoverage.Add(new(prefix.Prefix, page.Completion));
                    if (candidates >= Request.MaximumPrefixPackages)
                        return Complete(EcosystemFindCompletion.CandidateLimitReached,
                            [.. Request.Prefixes.Skip(index)
                                .Select(source => source.Prefix)]);
                    _incomplete = true;
                    sourceLimit ??= EcosystemFindCompletion.Partial;
                    sourceCompletion = page.Completion;
                    break;
                }
            }
            if (sourceCompletion != EcosystemFindPrefixPageCompletion.RequestedLimit)
                _prefixCoverage.Add(new(prefix.Prefix, sourceCompletion));
        }
        token.ThrowIfCancellationRequested();
        return Complete(sourceLimit ?? (_failures || _incomplete
            ? EcosystemFindCompletion.Partial
            : EcosystemFindCompletion.Exhausted), []);
    }

    private async Task<(int Rows, bool RowLimitReached)> RunIndexedAsync(
        int? maximumRows, CancellationToken token)
    {
        EcosystemFindIndexedOutcome<T> indexed =
            await _indexed!(maximumRows, token);
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(indexed);
        ArgumentNullException.ThrowIfNull(indexed.Blocks);
        ArgumentNullException.ThrowIfNull(indexed.Failures);
        if (indexed.Blocks.Any(static block => block is null)
            || indexed.Failures.Any(static failure => failure is null))
        {
            throw new InvalidOperationException(
                "Indexed Find outcomes cannot contain null entries.");
        }
        int indexedRows = 0;
        foreach (EcosystemFindBlock<T> block in indexed.Blocks)
        {
            if (block.RowCount < 0)
            {
                throw new InvalidOperationException(
                    "Indexed Find blocks cannot have negative row counts.");
            }
            indexedRows = checked(indexedRows + block.RowCount);
        }
        if (maximumRows is int indexedLimit
            && indexedRows > indexedLimit)
        {
            throw new InvalidOperationException(
                "Indexed Find exceeded its row window.");
        }
        foreach (EcosystemFindSourceFailure failure in indexed.Failures)
            _sourceFailures.Add(failure);
        if (indexed.Failures.Count > 0)
            _failures = _incomplete = true;
        foreach (EcosystemFindBlock<T> block in indexed.Blocks)
            Accept(block, _indexedBlocks, token);
        return (indexedRows,
            indexed.Completion is EcosystemFindIndexedCompletion.RowLimitReached);
    }

    private WorkspaceEcosystemRegistrationId[] PackageMemberships(string packageId) =>
    [
        .. Request.Layers.Where(layer =>
            layer.Registration.CorePackages.Any(core =>
                string.Equals(core.PackageId, packageId,
                    StringComparison.OrdinalIgnoreCase))
            || layer.Registration.Populations
                .OfType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>()
                .Any(declaration => Request.PrefixDemand.Contains(layer.Registration.Id)
                    && declaration.Prefix.MatchesPackageId(packageId)))
            .Select(layer => layer.Registration.Id),
    ];

    public async Task<EcosystemFindSearchSummary<T>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        EcosystemFindBoundedOutcome<T> bounded =
            await RunBoundedAsync(cancellationToken);
        return bounded.Completed ?? await bounded.Continuation!.ResumeAsync(
            bounded.Continuation.MaximumRemainingRows, cancellationToken);
    }

    private void Accept(EcosystemFindBlock<T> block,
        List<EcosystemFindBlock<T>> destination, CancellationToken token)
    {
        Observe(block, token);
        destination.Add(block);
        _rows = checked(_rows + block.RowCount);
        _publish?.Invoke(block);
    }

    private void Observe(
        EcosystemFindBlock<T> block,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (block.RowCount < 0)
            throw new ArgumentOutOfRangeException(nameof(block));
        _failures |= block.HasFailures;
        _incomplete |= block.Incomplete;
        if (block.Failure is { } failure)
            _sourceFailures.Add(new(block.Ecosystem is null
                ? "Prefix" : "Bounded",
                block.Candidate?.PackageId ?? block.Ecosystem!.Value,
                failure));
    }

    private EcosystemFindSearchSummary<T> Complete(
        EcosystemFindCompletion completion, IReadOnlyList<string> unsearched)
    {
        _completed = true;
        if (completion == EcosystemFindCompletion.Partial
            && _boundedBlocks.All(block => block.HasFailures)
            && _boundedBlocks.All(block => block.RowCount == 0)
            && _prefixBlocks.Count == 0
            && _prefixCoverage.All(source =>
                source.Completion == EcosystemFindPrefixPageCompletion.Failed))
            completion = EcosystemFindCompletion.Failed;
        return new(Request.Identity, [.. _boundedBlocks],
            [.. _indexedBlocks], [.. _prefixBlocks],
            completion, unsearched, [.. _sourceFailures],
            [.. _prefixCoverage], _failures,
            _incomplete || unsearched.Count > 0);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _cancellationRegistration.Dispose();
        _lifetime.Dispose();
    }
}
