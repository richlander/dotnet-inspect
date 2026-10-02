using System.Collections.Immutable;
using System.Net;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Services;

public enum PortablePdbSettlementCandidate
{
    Embedded,
    PositiveStore,
    ExternalProviders,
    MicrosoftSymbolServer,
    SymbolPackage,
    NuGetSymbolServer,
}

public enum PortablePdbSettlementAttemptOutcome
{
    Acquired,
    Skipped,
    Unavailable,
    Rejected,
    Incomplete,
    Canceled,
    Failed,
}

public enum PortablePdbSettlementSkipReason
{
    EarlierCandidateAcquired,
    NoPortableIdentity,
    UnsupportedFormat,
    UnsupportedProvenance,
    CacheOnly,
    MissingRoutingCoordinates,
    OperationStopped,
}

public enum PortablePdbSettlementSource
{
    Embedded,
    PositiveStore,
    MicrosoftSymbolServer,
    SymbolPackage,
    NuGetSymbolServer,
}

public enum PortablePdbPositiveStoreDisposition
{
    NotUsed,
    Reused,
    Published,
}

public enum PortablePdbSettlementFailureKind
{
    UnsupportedProvenance,
    UnsupportedWindowsPdb,
    InvalidEmbeddedContent,
    PositiveStoreFailed,
    ExternalProviderFailed,
}

public sealed record PortablePdbSettlementReceipt(
    PortablePdbSettlementCandidate Candidate,
    PortablePdbSettlementAttemptOutcome Outcome,
    int RequestCount = 0,
    HttpStatusCode? StatusCode = null,
    long BodyBytesRead = 0,
    TimeSpan Elapsed = default,
    InertString? Coordinates = null,
    PortablePdbStoreFailureKind? StoreFailure = null,
    PortablePdbAcquisitionFailureKind? ProviderFailure = null,
    bool Applicable = true,
    bool Authorized = true,
    PortablePdbSettlementSkipReason? SkipReason = null);

/// <summary>
/// Explicit host capabilities for one exact Portable PDB settlement.
/// </summary>
public sealed class PortablePdbSettlementRequest
{
    private TimeSpan? _timeout;

    public PortablePdbSettlementRequest(
        PdbContext context,
        ResolvedAssemblyReference assembly,
        HttpClient symbolClient,
        IPdbStore positiveStore,
        IPackageSourceAuthorization packageSourceAuthorization)
    {
        Context =
            context
            ?? throw new ArgumentNullException(nameof(context));
        Assembly =
            assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        SymbolClient =
            symbolClient
            ?? throw new ArgumentNullException(nameof(symbolClient));
        PositiveStore =
            positiveStore
            ?? throw new ArgumentNullException(nameof(positiveStore));
        PackageSourceAuthorization =
            packageSourceAuthorization
            ?? throw new ArgumentNullException(
                nameof(packageSourceAuthorization));

        if (!context.IsBoundTo(assembly))
        {
            throw new ArgumentException(
                "The Portable PDB context is not bound to the supplied assembly reference.",
                nameof(assembly));
        }
    }

    public PdbContext Context { get; }
    public ResolvedAssemblyReference Assembly { get; }
    public HttpClient SymbolClient { get; }
    public IPdbStore PositiveStore { get; }
    public IPackageSourceAuthorization PackageSourceAuthorization
    {
        get;
    }
    public bool CacheOnly { get; init; }
    public NuGetSourceOptions? NuGetSourceOptions { get; init; }
    public SymbolAcquisitionLimits? Limits { get; init; }
    public TimeSpan? Timeout
    {
        get => _timeout;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "The Portable PDB settlement timeout must be positive.");
            }

            _timeout = value;
        }
    }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Repeatable content admitted for an exact Portable PDB settlement.
/// </summary>
public sealed class SettledPortablePdbContent
{
    private readonly ImmutableArray<byte>? _embeddedImage;
    private readonly AcquiredPortablePdb? _storedContent;
    private readonly IPdbStore? _positiveStore;
    private readonly string? _positiveStoreKey;

    internal SettledPortablePdbContent(
        ImmutableArray<byte> embeddedImage)
        => _embeddedImage = embeddedImage;

    internal SettledPortablePdbContent(
        AcquiredPortablePdb storedContent)
        => _storedContent =
            storedContent
            ?? throw new ArgumentNullException(nameof(storedContent));

    internal SettledPortablePdbContent(
        IPdbStore positiveStore,
        string positiveStoreKey)
    {
        _positiveStore =
            positiveStore
            ?? throw new ArgumentNullException(nameof(positiveStore));
        _positiveStoreKey =
            positiveStoreKey
            ?? throw new ArgumentNullException(nameof(positiveStoreKey));
    }

    public async ValueTask<Stream> OpenReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_storedContent is not null)
        {
            return await _storedContent.OpenReadAsync(
                cancellationToken).ConfigureAwait(false);
        }

        if (_positiveStore is not null)
        {
            Stream? stored =
                await _positiveStore.TryOpenAsync(
                    _positiveStoreKey!,
                    cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                throw new IOException(
                    "The settled Portable PDB content is no longer available.");
            }

            if (!stored.CanRead || !stored.CanSeek)
            {
                await stored.DisposeAsync().ConfigureAwait(false);
                throw new IOException(
                    "The Portable PDB store returned unreadable content.");
            }

            stored.Position = 0;
            return stored;
        }

        if (_embeddedImage is not { } embeddedImage
            || embeddedImage.IsDefault)
        {
            throw new IOException(
                "The settled Portable PDB content is unavailable.");
        }

        Stream stream =
            new MemoryStream(
                embeddedImage.ToArray(),
                writable: false);
        return stream;
    }

    internal string? SymbolServer => _storedContent?.SymbolServer;
}

/// <summary>The typed outcome of one exact Portable PDB settlement.</summary>
public abstract record PortablePdbSettlementResult
{
    private protected PortablePdbSettlementResult(
        ResolvedAssemblyReference assembly,
        PortablePdbContentIdentity? portablePdbIdentity,
        ImmutableArray<PortablePdbSettlementReceipt> receipts)
    {
        Assembly = assembly;
        PortablePdbIdentity = portablePdbIdentity;
        Receipts = receipts;
    }

    public ResolvedAssemblyReference Assembly { get; }
    public PortablePdbContentIdentity? PortablePdbIdentity { get; }
    public ImmutableArray<PortablePdbSettlementReceipt> Receipts { get; }

    public sealed record Acquired : PortablePdbSettlementResult
    {
        internal Acquired(
            ResolvedAssemblyReference assembly,
            PortablePdbContentIdentity portablePdbIdentity,
            SettledPortablePdbContent content,
            PortablePdbSettlementSource source,
            PortablePdbPositiveStoreDisposition positiveStore,
            bool networkOccurred,
            ImmutableArray<PortablePdbSettlementReceipt> receipts)
            : base(assembly, portablePdbIdentity, receipts)
        {
            Content = content;
            Source = source;
            PositiveStore = positiveStore;
            NetworkOccurred = networkOccurred;
        }

        public SettledPortablePdbContent Content { get; }
        public PortablePdbSettlementSource Source { get; }
        public PortablePdbPositiveStoreDisposition PositiveStore { get; }
        public bool NetworkOccurred { get; }

        public async Task LoadIntoAsync(
            PdbContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!context.IsBoundTo(Assembly))
            {
                throw new ArgumentException(
                    "The Portable PDB result is not bound to the target assembly context.",
                    nameof(context));
            }

            if (context.HasPdb)
                return;

            Stream stream =
                await Content.OpenReadAsync(
                    cancellationToken).ConfigureAwait(false);
            context.LoadPdbFromStream(
                stream,
                Source == PortablePdbSettlementSource.Embedded
                    ? "Embedded"
                    : "Settlement",
                Content.SymbolServer,
                throwOnReadFailure: true);
            if (!context.HasPdb)
            {
                throw new InvalidDataException(
                    context.LastPdbLoadError
                    ?? "The settled Portable PDB content could not be loaded.");
            }
        }
    }

    public sealed record Unavailable : PortablePdbSettlementResult
    {
        internal Unavailable(
            ResolvedAssemblyReference assembly,
            PortablePdbContentIdentity? portablePdbIdentity,
            ImmutableArray<PortablePdbSettlementReceipt> receipts)
            : base(assembly, portablePdbIdentity, receipts)
        {
        }
    }

    public sealed record Incomplete : PortablePdbSettlementResult
    {
        internal Incomplete(
            ResolvedAssemblyReference assembly,
            PortablePdbContentIdentity portablePdbIdentity,
            ImmutableArray<PortablePdbSettlementReceipt> receipts)
            : base(assembly, portablePdbIdentity, receipts)
        {
        }
    }

    public sealed record Canceled : PortablePdbSettlementResult
    {
        internal Canceled(
            ResolvedAssemblyReference assembly,
            PortablePdbContentIdentity? portablePdbIdentity,
            ImmutableArray<PortablePdbSettlementReceipt> receipts)
            : base(assembly, portablePdbIdentity, receipts)
        {
        }
    }

    public sealed record Failed : PortablePdbSettlementResult
    {
        internal Failed(
            ResolvedAssemblyReference assembly,
            PortablePdbContentIdentity? portablePdbIdentity,
            PortablePdbSettlementFailureKind failure,
            PortablePdbStoreFailureKind? storeFailure,
            ImmutableArray<PortablePdbSettlementReceipt> receipts)
            : base(assembly, portablePdbIdentity, receipts)
        {
            Failure = failure;
            StoreFailure = storeFailure;
        }

        public PortablePdbSettlementFailureKind Failure { get; }
        public PortablePdbStoreFailureKind? StoreFailure { get; }
    }
}

/// <summary>
/// Settles Portable PDB content for one exact symbol-bearing assembly.
/// </summary>
public static class PortablePdbSettlement
{
    private sealed record PositiveStoreProbe(
        SettledPortablePdbContent? Content,
        PortablePdbStoreFailureKind? Failure,
        bool Incomplete)
    {
        internal static PositiveStoreProbe Missing { get; } =
            new(null, null, false);
    }

    public static async Task<PortablePdbSettlementResult> SettleAsync(
        PortablePdbSettlementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var receipts =
            ImmutableArray.CreateBuilder<
                PortablePdbSettlementReceipt>();
        CodeViewInfo? codeView = request.Context.PdbId;
        if (request.Context.HasPdb)
        {
            ImmutableArray<byte>? image =
                request.Context.GetPortablePdbImage();
            PortablePdbContentIdentity? embeddedIdentity =
                request.Context.GetPortablePdbContentIdentity();
            if (image is not { } embeddedImage
                || embeddedImage.IsDefaultOrEmpty
                || embeddedIdentity is null)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.Embedded,
                    PortablePdbSettlementAttemptOutcome.Failed));
                return new PortablePdbSettlementResult.Failed(
                    request.Assembly,
                    embeddedIdentity,
                    PortablePdbSettlementFailureKind
                        .InvalidEmbeddedContent,
                    storeFailure: null,
                    receipts.ToImmutable());
            }

            receipts.Add(new(
                PortablePdbSettlementCandidate.Embedded,
                PortablePdbSettlementAttemptOutcome.Acquired));
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.PositiveStore,
                PortablePdbSettlementSkipReason
                    .EarlierCandidateAcquired));
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.ExternalProviders,
                PortablePdbSettlementSkipReason
                    .EarlierCandidateAcquired));
            return new PortablePdbSettlementResult.Acquired(
                request.Assembly,
                embeddedIdentity.Value,
                new SettledPortablePdbContent(embeddedImage),
                PortablePdbSettlementSource.Embedded,
                PortablePdbPositiveStoreDisposition.NotUsed,
                networkOccurred: false,
                receipts.ToImmutable());
        }

        receipts.Add(new(
            PortablePdbSettlementCandidate.Embedded,
            PortablePdbSettlementAttemptOutcome.Unavailable));
        if (codeView is null)
        {
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.PositiveStore,
                PortablePdbSettlementSkipReason.NoPortableIdentity,
                applicable: false));
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.ExternalProviders,
                PortablePdbSettlementSkipReason.NoPortableIdentity,
                applicable: false));
            return new PortablePdbSettlementResult.Unavailable(
                request.Assembly,
                portablePdbIdentity: null,
                receipts.ToImmutable());
        }

        PortablePdbContentIdentity identity =
            new(codeView.Guid, codeView.Stamp);
        if (!codeView.IsPortable)
        {
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.PositiveStore,
                PortablePdbSettlementSkipReason.UnsupportedFormat,
                applicable: false));
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.ExternalProviders,
                PortablePdbSettlementSkipReason.UnsupportedFormat,
                applicable: false));
            return new PortablePdbSettlementResult.Failed(
                request.Assembly,
                identity,
                PortablePdbSettlementFailureKind.UnsupportedWindowsPdb,
                storeFailure: null,
                receipts.ToImmutable());
        }

        string positiveStoreKey =
            PositiveStoreKey(identity);
        var identityStore =
            new IdentityScopedPdbStore(
                request.PositiveStore,
                positiveStoreKey);
        var evidence =
            new PortablePdbAcquisitionEvidenceCollector();
        PortablePdbAcquisitionResult? acquisition;
        using CancellationTokenSource? timeout =
            CreateTimeoutSource(
                request.Timeout,
                cancellationToken);
        CancellationToken operationToken =
            timeout?.Token ?? cancellationToken;
        PositiveStoreProbe storeProbe =
            PositiveStoreProbe.Missing;
        bool storeProbeCompleted = false;
        try
        {
            storeProbe =
                await ProbePositiveStoreAsync(
                    request.PositiveStore,
                    positiveStoreKey,
                    identity,
                    request.Limits,
                    request.Log,
                    operationToken).ConfigureAwait(false);
            storeProbeCompleted = true;
            if (storeProbe.Content is not null)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.PositiveStore,
                    PortablePdbSettlementAttemptOutcome.Acquired));
                receipts.Add(Skipped(
                    request.Assembly.Provenance
                        is AssemblyResolutionProvenance.PlatformAsset
                            ? PortablePdbSettlementCandidate
                                .MicrosoftSymbolServer
                            : PortablePdbSettlementCandidate
                                .ExternalProviders,
                    request.Assembly.Provenance
                        is AssemblyResolutionProvenance.PlatformAsset
                            ? PortablePdbSettlementSkipReason
                                .EarlierCandidateAcquired
                            : PortablePdbSettlementSkipReason
                                .UnsupportedProvenance,
                    authorized:
                        request.Assembly.Provenance
                        is AssemblyResolutionProvenance.PlatformAsset));
                return new PortablePdbSettlementResult.Acquired(
                    request.Assembly,
                    identity,
                    storeProbe.Content,
                    PortablePdbSettlementSource.PositiveStore,
                    PortablePdbPositiveStoreDisposition.Reused,
                    networkOccurred: false,
                    receipts.ToImmutable());
            }

            if (storeProbe.Incomplete)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.PositiveStore,
                    PortablePdbSettlementAttemptOutcome.Incomplete));
                receipts.Add(Skipped(
                    request.Assembly.Provenance
                        is AssemblyResolutionProvenance.PlatformAsset
                            ? PortablePdbSettlementCandidate
                                .MicrosoftSymbolServer
                            : PortablePdbSettlementCandidate
                                .ExternalProviders,
                    PortablePdbSettlementSkipReason.OperationStopped));
                return new PortablePdbSettlementResult.Incomplete(
                    request.Assembly,
                    identity,
                    receipts.ToImmutable());
            }

            if (request.Assembly.Provenance
                is not AssemblyResolutionProvenance.PlatformAsset)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.PositiveStore,
                    storeProbe.Failure is null
                        ? PortablePdbSettlementAttemptOutcome.Unavailable
                        : PortablePdbSettlementAttemptOutcome.Failed,
                    StoreFailure: storeProbe.Failure));
                receipts.Add(Skipped(
                    PortablePdbSettlementCandidate.ExternalProviders,
                    PortablePdbSettlementSkipReason
                        .UnsupportedProvenance,
                    authorized: false));
                return new PortablePdbSettlementResult.Failed(
                    request.Assembly,
                    identity,
                    storeProbe.Failure is null
                        ? PortablePdbSettlementFailureKind
                            .UnsupportedProvenance
                        : PortablePdbSettlementFailureKind
                            .PositiveStoreFailed,
                    storeProbe.Failure,
                    receipts.ToImmutable());
            }

            acquisition =
                await PdbAcquisitionService.AcquireContentAsync(
                    request.Context,
                    request.Assembly,
                    request.SymbolClient,
                    identityStore,
                    request.PackageSourceAuthorization,
                    request.Log,
                    request.CacheOnly,
                    request.NuGetSourceOptions,
                    operationToken,
                    request.Limits,
                    evidence).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PortablePdbAcquisitionEvidenceDocument canceled =
                evidence.ToDocument();
            bool callerCanceled =
                cancellationToken.IsCancellationRequested;
            receipts.Add(new(
                PortablePdbSettlementCandidate.PositiveStore,
                storeProbeCompleted
                    ? storeProbe.Failure is null
                        ? PortablePdbSettlementAttemptOutcome.Unavailable
                        : PortablePdbSettlementAttemptOutcome.Failed
                    : canceled.NetworkAttempts.IsEmpty
                    ? callerCanceled
                        ? PortablePdbSettlementAttemptOutcome.Canceled
                        : PortablePdbSettlementAttemptOutcome.Incomplete
                    : PortablePdbSettlementAttemptOutcome.Unavailable,
                StoreFailure: storeProbe.Failure));
            foreach (PortablePdbNetworkAttemptEvidence attempt
                in canceled.NetworkAttempts)
            {
                receipts.Add(new(
                    Candidate(attempt.Route),
                    Outcome(
                        attempt.Outcome,
                        admitted: false,
                        failedAfterTransfer: false),
                    attempt.RequestCount,
                    attempt.StatusCode,
                    attempt.BodyBytesRead,
                    attempt.Elapsed,
                    attempt.Url));
            }
            if (canceled.NetworkAttempts.IsEmpty)
            {
                receipts.Add(Skipped(
                    PortablePdbSettlementCandidate
                        .MicrosoftSymbolServer,
                    PortablePdbSettlementSkipReason
                        .OperationStopped));
            }
            return callerCanceled
                ? new PortablePdbSettlementResult.Canceled(
                    request.Assembly,
                    identity,
                    receipts.ToImmutable())
                : new PortablePdbSettlementResult.Incomplete(
                    request.Assembly,
                    identity,
                    receipts.ToImmutable());
        }

        PortablePdbAcquisitionEvidenceDocument document =
            evidence.ToDocument();
        PortablePdbStoreFailureKind? storeFailure =
            storeProbe.Failure ?? document.StoreFailure;
        bool fromCache =
            acquisition is PortablePdbAcquisitionResult.Acquired
            {
                Pdb.FromCache: true,
            };
        receipts.Add(new(
            PortablePdbSettlementCandidate.PositiveStore,
            fromCache
                ? PortablePdbSettlementAttemptOutcome.Acquired
                : storeFailure is not null
                    ? PortablePdbSettlementAttemptOutcome.Failed
                    : PortablePdbSettlementAttemptOutcome.Unavailable,
            StoreFailure: storeFailure));
        int supplyingAttempt =
            acquisition is PortablePdbAcquisitionResult.Acquired
                ? LastSucceededAttempt(document.NetworkAttempts)
                : -1;
        for (int i = 0; i < document.NetworkAttempts.Length; i++)
        {
            PortablePdbNetworkAttemptEvidence attempt =
                document.NetworkAttempts[i];
            PortablePdbSettlementAttemptOutcome attemptOutcome =
                Outcome(
                    attempt.Outcome,
                    admitted: i == supplyingAttempt,
                    failedAfterTransfer:
                        acquisition?.StoreFailure is not null);
            receipts.Add(new(
                Candidate(attempt.Route),
                attemptOutcome,
                attempt.RequestCount,
                attempt.StatusCode,
                attempt.BodyBytesRead,
                attempt.Elapsed,
                attempt.Url,
                ProviderFailure:
                    attemptOutcome
                        is PortablePdbSettlementAttemptOutcome
                            .Rejected
                            or PortablePdbSettlementAttemptOutcome
                                .Failed
                        ? acquisition?.AcquisitionFailure
                        : null));
        }
        if (document.NetworkAttempts.IsEmpty)
        {
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate
                    .MicrosoftSymbolServer,
                acquisition
                    is PortablePdbAcquisitionResult.Acquired
                    {
                        Pdb.FromCache: true,
                    }
                        ? PortablePdbSettlementSkipReason
                            .EarlierCandidateAcquired
                        : request.CacheOnly
                            ? PortablePdbSettlementSkipReason.CacheOnly
                            : PortablePdbSettlementSkipReason
                                .MissingRoutingCoordinates));
        }

        ImmutableArray<PortablePdbSettlementReceipt>
            settledReceipts = receipts.ToImmutable();
        if (acquisition
            is PortablePdbAcquisitionResult.Acquired acquired)
        {
            PortablePdbSettlementSource source =
                acquired.Pdb.FromCache
                    ? PortablePdbSettlementSource.PositiveStore
                    : Source(document.NetworkAttempts);
            return new PortablePdbSettlementResult.Acquired(
                request.Assembly,
                identity,
                new SettledPortablePdbContent(acquired.Pdb),
                source,
                acquired.Pdb.FromCache
                    ? PortablePdbPositiveStoreDisposition.Reused
                    : PortablePdbPositiveStoreDisposition.Published,
                document.NetworkAttempts.Any(
                    static attempt => attempt.RequestCount > 0),
                settledReceipts);
        }

        if (acquisition is null)
        {
            return new PortablePdbSettlementResult.Unavailable(
                request.Assembly,
                identity,
                settledReceipts);
        }

        if (document.NetworkAttempts.Any(
                static attempt =>
                    attempt.Outcome
                    == PortablePdbNetworkAttemptOutcome.TooLarge))
        {
            return new PortablePdbSettlementResult.Incomplete(
                request.Assembly,
                identity,
                settledReceipts);
        }

        if (storeFailure is { } retainedStoreFailure)
        {
            return new PortablePdbSettlementResult.Failed(
                request.Assembly,
                identity,
                PortablePdbSettlementFailureKind.PositiveStoreFailed,
                retainedStoreFailure,
                settledReceipts);
        }

        if (acquisition.AcquisitionFailure is not null)
        {
            return new PortablePdbSettlementResult.Failed(
                request.Assembly,
                identity,
                PortablePdbSettlementFailureKind.ExternalProviderFailed,
                storeFailure: null,
                settledReceipts);
        }

        if (acquisition.WindowsPdbDetected)
        {
            return new PortablePdbSettlementResult.Failed(
                request.Assembly,
                identity,
                PortablePdbSettlementFailureKind.UnsupportedWindowsPdb,
                storeFailure: null,
                settledReceipts);
        }

        return new PortablePdbSettlementResult.Unavailable(
            request.Assembly,
            identity,
            settledReceipts);
    }

    private static PortablePdbSettlementCandidate Candidate(
        PortablePdbAcquisitionNetworkRoute route)
        => route switch
        {
            PortablePdbAcquisitionNetworkRoute
                .MicrosoftSymbolServer =>
                    PortablePdbSettlementCandidate
                        .MicrosoftSymbolServer,
            PortablePdbAcquisitionNetworkRoute.SymbolPackage =>
                PortablePdbSettlementCandidate.SymbolPackage,
            PortablePdbAcquisitionNetworkRoute.SymbolServer =>
                PortablePdbSettlementCandidate.NuGetSymbolServer,
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

    private static PortablePdbSettlementReceipt Skipped(
        PortablePdbSettlementCandidate candidate,
        PortablePdbSettlementSkipReason reason,
        bool applicable = true,
        bool authorized = true)
        => new(
            candidate,
            PortablePdbSettlementAttemptOutcome.Skipped,
            Applicable: applicable,
            Authorized: authorized,
            SkipReason: reason);

    private static CancellationTokenSource? CreateTimeoutSource(
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is not { } timeoutValue)
            return null;

        CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        source.CancelAfter(timeoutValue);
        return source;
    }

    private static string PositiveStoreKey(
        PortablePdbContentIdentity identity)
        => "portable/"
            + identity.Guid.ToString("N").ToUpperInvariant()
            + identity.Stamp.ToString("X8")
            + ".pdb";

    private static async Task<PositiveStoreProbe>
        ProbePositiveStoreAsync(
        IPdbStore store,
        string key,
        PortablePdbContentIdentity identity,
        SymbolAcquisitionLimits? limits,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        Stream? stream;
        try
        {
            stream =
                await store.TryOpenAsync(
                    key,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new(
                null,
                PortablePdbStoreFailureKind.ReadFailed,
                Incomplete: false);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                null,
                PortablePdbStoreFailureKind.ReadFailed,
                Incomplete: false);
        }

        if (stream is null)
            return PositiveStoreProbe.Missing;

        try
        {
            await using (stream.ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!stream.CanRead || !stream.CanSeek)
                {
                    return new(
                        null,
                        PortablePdbStoreFailureKind.ReadFailed,
                        Incomplete: false);
                }

                if (limits is not null
                    && stream.Length > limits.MaxPortablePdbBytes)
                {
                    return new(
                        null,
                        Failure: null,
                        Incomplete: true);
                }

                PortablePdbIdentityMatch match =
                    PdbContext.ClassifyPortablePdbIdentity(
                        stream,
                        identity,
                        log);
                if (match == PortablePdbIdentityMatch.Match)
                {
                    return new(
                        new SettledPortablePdbContent(store, key),
                        Failure: null,
                        Incomplete: false);
                }

                return new(
                    null,
                    PortablePdbStoreFailureKind.InvalidCachedContent,
                    Incomplete: false);
            }
        }
        catch (IOException)
        {
            return new(
                null,
                PortablePdbStoreFailureKind.ReadFailed,
                Incomplete: false);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                null,
                PortablePdbStoreFailureKind.ReadFailed,
                Incomplete: false);
        }
    }

    private sealed class IdentityScopedPdbStore(
        IPdbStore store,
        string identityKey) : IPdbStore
    {
        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
            => store.TryOpenAsync(
                identityKey,
                cancellationToken);

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
            => store.PutAsync(
                identityKey,
                content,
                cancellationToken);

        public string? TryGetLocalPath(string key)
            => store.TryGetLocalPath(identityKey);
    }

    private static PortablePdbSettlementAttemptOutcome Outcome(
        PortablePdbNetworkAttemptOutcome outcome,
        bool admitted,
        bool failedAfterTransfer)
        => outcome switch
        {
            PortablePdbNetworkAttemptOutcome.Succeeded =>
                admitted
                    ? PortablePdbSettlementAttemptOutcome.Acquired
                    : failedAfterTransfer
                        ? PortablePdbSettlementAttemptOutcome.Failed
                    : PortablePdbSettlementAttemptOutcome.Rejected,
            PortablePdbNetworkAttemptOutcome.Unavailable =>
                PortablePdbSettlementAttemptOutcome.Unavailable,
            PortablePdbNetworkAttemptOutcome.ResponseRejected =>
                PortablePdbSettlementAttemptOutcome.Rejected,
            PortablePdbNetworkAttemptOutcome.TooLarge =>
                PortablePdbSettlementAttemptOutcome.Incomplete,
            PortablePdbNetworkAttemptOutcome.Canceled =>
                PortablePdbSettlementAttemptOutcome.Canceled,
            PortablePdbNetworkAttemptOutcome.Failed =>
                PortablePdbSettlementAttemptOutcome.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    private static int LastSucceededAttempt(
        ImmutableArray<PortablePdbNetworkAttemptEvidence> attempts)
    {
        for (int i = attempts.Length - 1; i >= 0; i--)
        {
            if (attempts[i].Outcome
                == PortablePdbNetworkAttemptOutcome.Succeeded)
            {
                return i;
            }
        }

        return -1;
    }

    private static PortablePdbSettlementSource Source(
        ImmutableArray<PortablePdbNetworkAttemptEvidence> attempts)
    {
        PortablePdbNetworkAttemptEvidence? succeeded =
            attempts.LastOrDefault(
                static attempt =>
                    attempt.Outcome
                    == PortablePdbNetworkAttemptOutcome.Succeeded);
        return succeeded?.Route switch
        {
            PortablePdbAcquisitionNetworkRoute
                .MicrosoftSymbolServer =>
                    PortablePdbSettlementSource
                        .MicrosoftSymbolServer,
            PortablePdbAcquisitionNetworkRoute.SymbolPackage =>
                PortablePdbSettlementSource.SymbolPackage,
            PortablePdbAcquisitionNetworkRoute.SymbolServer =>
                PortablePdbSettlementSource.NuGetSymbolServer,
            _ => throw new InvalidDataException(
                "Acquired Portable PDB content has no successful provider receipt."),
        };
    }
}
