using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net;
using System.Text;

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
    private readonly string? _positiveStoreSymbolServer;

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
        string positiveStoreKey,
        string? symbolServer)
    {
        _positiveStore =
            positiveStore
            ?? throw new ArgumentNullException(nameof(positiveStore));
        _positiveStoreKey =
            positiveStoreKey
            ?? throw new ArgumentNullException(nameof(positiveStoreKey));
        _positiveStoreSymbolServer = symbolServer;
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

    internal string? SymbolServer =>
        _storedContent?.SymbolServer
        ?? _positiveStoreSymbolServer;
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
            InertString? providerCoordinates,
            PortablePdbPositiveStoreDisposition positiveStore,
            bool networkOccurred,
            ImmutableArray<PortablePdbSettlementReceipt> receipts)
            : base(assembly, portablePdbIdentity, receipts)
        {
            Content = content;
            Source = source;
            ProviderCoordinates = providerCoordinates;
            PositiveStore = positiveStore;
            NetworkOccurred = networkOccurred;
        }

        public SettledPortablePdbContent Content { get; }
        public PortablePdbSettlementSource Source { get; }
        public InertString? ProviderCoordinates { get; }
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
    private const int MaxProvenanceBytes = 4096;
    private const uint ProvenanceMagic = 0x31564450;

    private sealed record PositiveStoreProvenance(
        PortablePdbSettlementSource Source,
        InertString? Coordinates,
        string? SymbolServer);

    private sealed record PositiveStoreProbe(
        SettledPortablePdbContent? Content,
        PositiveStoreProvenance? Provenance,
        PortablePdbStoreFailureKind? Failure,
        bool Incomplete)
    {
        internal static PositiveStoreProbe Missing { get; } =
            new(null, null, null, false);
    }

    private readonly record struct ProvenancePublicationResult(
        PortablePdbStoreFailureKind? Failure,
        bool Canceled);

    public static async Task<PortablePdbSettlementResult> SettleAsync(
        PortablePdbSettlementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var receipts =
            ImmutableArray.CreateBuilder<
                PortablePdbSettlementReceipt>();
        CodeViewInfo? codeView = request.Context.PdbId;
        PortablePdbContentIdentity? requestedIdentity =
            codeView is null
                ? null
                : new(codeView.Guid, codeView.Stamp);
        using CancellationTokenSource? timeout =
            CreateTimeoutSource(
                request.Timeout,
                cancellationToken);
        CancellationToken operationToken =
            timeout?.Token ?? cancellationToken;
        if (operationToken.IsCancellationRequested)
        {
            bool callerCanceled =
                cancellationToken.IsCancellationRequested;
            receipts.Add(new(
                PortablePdbSettlementCandidate.Embedded,
                callerCanceled
                    ? PortablePdbSettlementAttemptOutcome.Canceled
                    : PortablePdbSettlementAttemptOutcome.Incomplete));
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.PositiveStore,
                PortablePdbSettlementSkipReason.OperationStopped));
            receipts.Add(Skipped(
                ExternalCandidate(request.Assembly),
                PortablePdbSettlementSkipReason.OperationStopped));
            return callerCanceled
                ? new PortablePdbSettlementResult.Canceled(
                    request.Assembly,
                    requestedIdentity,
                    receipts.ToImmutable())
                : requestedIdentity is { } timeoutIdentity
                    ? new PortablePdbSettlementResult.Incomplete(
                        request.Assembly,
                        timeoutIdentity,
                        receipts.ToImmutable())
                    : new PortablePdbSettlementResult.Failed(
                        request.Assembly,
                        portablePdbIdentity: null,
                        PortablePdbSettlementFailureKind
                            .InvalidEmbeddedContent,
                        storeFailure: null,
                        receipts.ToImmutable());
        }

        bool embeddedCandidate =
            request.Context.HasEmbeddedPdb
            || request.Context.HasPdb
                && string.Equals(
                    request.Context.PdbLocation,
                    "Embedded",
                    StringComparison.Ordinal);
        if (embeddedCandidate)
        {
            try
            {
                if (!request.Context.HasPdb)
                {
                    int maxEmbeddedPdbBytes =
                        request.Limits is null
                            ? int.MaxValue
                            : checked((int)request.Limits
                                .MaxPortablePdbBytes);
                    _ = request.Context.TryLoadEmbeddedPortablePdb(
                        maxEmbeddedPdbBytes);
                }
            }
            catch (PdbResourceLimitException)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.Embedded,
                    PortablePdbSettlementAttemptOutcome.Incomplete));
                receipts.Add(Skipped(
                    PortablePdbSettlementCandidate.PositiveStore,
                    PortablePdbSettlementSkipReason.OperationStopped));
                receipts.Add(Skipped(
                    ExternalCandidate(request.Assembly),
                    PortablePdbSettlementSkipReason.OperationStopped));
                return requestedIdentity is { } limitedIdentity
                    ? new PortablePdbSettlementResult.Incomplete(
                        request.Assembly,
                        limitedIdentity,
                        receipts.ToImmutable())
                    : new PortablePdbSettlementResult.Failed(
                        request.Assembly,
                        portablePdbIdentity: null,
                        PortablePdbSettlementFailureKind
                            .InvalidEmbeddedContent,
                        storeFailure: null,
                        receipts.ToImmutable());
            }
            catch (BadImageFormatException)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.Embedded,
                    PortablePdbSettlementAttemptOutcome.Failed));
                return new PortablePdbSettlementResult.Failed(
                    request.Assembly,
                    requestedIdentity,
                    PortablePdbSettlementFailureKind
                        .InvalidEmbeddedContent,
                    storeFailure: null,
                    receipts.ToImmutable());
            }
            catch (IOException)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.Embedded,
                    PortablePdbSettlementAttemptOutcome.Failed));
                return new PortablePdbSettlementResult.Failed(
                    request.Assembly,
                    requestedIdentity,
                    PortablePdbSettlementFailureKind
                        .InvalidEmbeddedContent,
                    storeFailure: null,
                    receipts.ToImmutable());
            }
        }

        if (operationToken.IsCancellationRequested)
        {
            bool callerCanceled =
                cancellationToken.IsCancellationRequested;
            receipts.Add(new(
                PortablePdbSettlementCandidate.Embedded,
                callerCanceled
                    ? PortablePdbSettlementAttemptOutcome.Canceled
                    : PortablePdbSettlementAttemptOutcome.Incomplete));
            receipts.Add(Skipped(
                PortablePdbSettlementCandidate.PositiveStore,
                PortablePdbSettlementSkipReason.OperationStopped));
            receipts.Add(Skipped(
                ExternalCandidate(request.Assembly),
                PortablePdbSettlementSkipReason.OperationStopped));
            return callerCanceled
                ? new PortablePdbSettlementResult.Canceled(
                    request.Assembly,
                    requestedIdentity,
                    receipts.ToImmutable())
                : requestedIdentity is { } timeoutIdentity
                    ? new PortablePdbSettlementResult.Incomplete(
                        request.Assembly,
                        timeoutIdentity,
                        receipts.ToImmutable())
                    : new PortablePdbSettlementResult.Failed(
                        request.Assembly,
                        portablePdbIdentity: null,
                        PortablePdbSettlementFailureKind
                            .InvalidEmbeddedContent,
                        storeFailure: null,
                        receipts.ToImmutable());
        }

        if (request.Context.HasPdb
            && string.Equals(
                request.Context.PdbLocation,
                "Embedded",
                StringComparison.Ordinal))
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

            if (request.Limits is not null
                && embeddedImage.Length
                    > request.Limits.MaxPortablePdbBytes)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.Embedded,
                    PortablePdbSettlementAttemptOutcome.Incomplete));
                receipts.Add(Skipped(
                    PortablePdbSettlementCandidate.PositiveStore,
                    PortablePdbSettlementSkipReason.OperationStopped));
                receipts.Add(Skipped(
                    ExternalCandidate(request.Assembly),
                    PortablePdbSettlementSkipReason.OperationStopped));
                return new PortablePdbSettlementResult.Incomplete(
                    request.Assembly,
                    embeddedIdentity.Value,
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
                providerCoordinates: null,
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
        string positiveStoreProvenanceKey =
            PositiveStoreProvenanceKey(identity);
        var evidence =
            new PortablePdbAcquisitionEvidenceCollector();
        PortablePdbAcquisitionResult? acquisition;
        PositiveStoreProbe storeProbe =
            PositiveStoreProbe.Missing;
        bool storeProbeCompleted = false;
        try
        {
            storeProbe =
                await ProbePositiveStoreAsync(
                    request.PositiveStore,
                    positiveStoreKey,
                    positiveStoreProvenanceKey,
                    identity,
                    request.Limits,
                    request.Log,
                    operationToken).ConfigureAwait(false);
            storeProbeCompleted = true;
            if (storeProbe.Content is not null)
            {
                receipts.Add(new(
                    PortablePdbSettlementCandidate.PositiveStore,
                    PortablePdbSettlementAttemptOutcome.Acquired,
                    Coordinates:
                        storeProbe.Provenance?.Coordinates));
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
                    storeProbe.Provenance!.Source,
                    storeProbe.Provenance.Coordinates,
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

            var identityStore =
                new IdentityScopedPdbStore(
                    request.PositiveStore,
                    positiveStoreKey,
                    allowInitialRead:
                        storeProbe.Failure is null);
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
        catch (PdbStoreAcquisitionException exception)
        {
            evidence.RecordStoreFailure(
                exception.StoreFailure);
            acquisition = null;
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
        PortablePdbStoreFailureKind? blockingStoreFailure =
            acquisition
                is PortablePdbAcquisitionResult.Acquired
                    ? null
                    : acquisition?.StoreFailure
                        ?? document.StoreFailure;
        bool fromCache =
            acquisition is PortablePdbAcquisitionResult.Acquired
            {
                Pdb.FromCache: true,
            };
        int supplyingAttempt =
            acquisition is PortablePdbAcquisitionResult.Acquired
                ? LastSucceededAttempt(document.NetworkAttempts)
                : -1;
        PositiveStoreProvenance? acquiredProvenance = null;
        bool provenancePublicationCanceled = false;
        if (acquisition
            is PortablePdbAcquisitionResult.Acquired acquiredContent)
        {
            if (fromCache || supplyingAttempt < 0)
            {
                blockingStoreFailure ??=
                    PortablePdbStoreFailureKind
                        .ProvenanceUnavailable;
            }
            else
            {
                PortablePdbNetworkAttemptEvidence attempt =
                    document.NetworkAttempts[supplyingAttempt];
                acquiredProvenance = new(
                    Source(attempt.Route),
                    attempt.Url,
                    acquiredContent.Pdb.SymbolServer);
                ProvenancePublicationResult publication =
                    await PublishPositiveStoreProvenanceAsync(
                        request.PositiveStore,
                        positiveStoreProvenanceKey,
                        acquiredProvenance,
                        operationToken).ConfigureAwait(false);
                blockingStoreFailure ??=
                    publication.Failure;
                provenancePublicationCanceled =
                    publication.Canceled;
            }
        }
        else
        {
            blockingStoreFailure ??=
                storeProbe.Failure;
        }

        PortablePdbStoreFailureKind? observedStoreFailure =
            storeProbe.Failure ?? blockingStoreFailure;

        receipts.Add(new(
            PortablePdbSettlementCandidate.PositiveStore,
            fromCache && blockingStoreFailure is null
                ? PortablePdbSettlementAttemptOutcome.Acquired
                : observedStoreFailure is not null
                    ? PortablePdbSettlementAttemptOutcome.Failed
                    : PortablePdbSettlementAttemptOutcome.Unavailable,
            StoreFailure: observedStoreFailure));
        for (int i = 0; i < document.NetworkAttempts.Length; i++)
        {
            PortablePdbNetworkAttemptEvidence attempt =
                document.NetworkAttempts[i];
            PortablePdbSettlementAttemptOutcome attemptOutcome =
                Outcome(
                    attempt.Outcome,
                    admitted:
                        i == supplyingAttempt
                        && blockingStoreFailure is null
                        && !provenancePublicationCanceled,
                    failedAfterTransfer:
                        attempt.Outcome
                            == PortablePdbNetworkAttemptOutcome
                                .Succeeded
                        && (blockingStoreFailure is not null
                            || provenancePublicationCanceled));
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
        if (provenancePublicationCanceled)
        {
            return cancellationToken.IsCancellationRequested
                ? new PortablePdbSettlementResult.Canceled(
                    request.Assembly,
                    identity,
                    settledReceipts)
                : new PortablePdbSettlementResult.Incomplete(
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

        if (blockingStoreFailure
            is { } retainedStoreFailure)
        {
            return new PortablePdbSettlementResult.Failed(
                request.Assembly,
                identity,
                PortablePdbSettlementFailureKind.PositiveStoreFailed,
                retainedStoreFailure,
                settledReceipts);
        }

        if (acquisition
            is PortablePdbAcquisitionResult.Acquired acquired)
        {
            return new PortablePdbSettlementResult.Acquired(
                request.Assembly,
                identity,
                new SettledPortablePdbContent(acquired.Pdb),
                acquiredProvenance!.Source,
                acquiredProvenance.Coordinates,
                PortablePdbPositiveStoreDisposition.Published,
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

    private static PortablePdbSettlementCandidate ExternalCandidate(
        ResolvedAssemblyReference assembly)
        => assembly.Provenance
            is AssemblyResolutionProvenance.PlatformAsset
                ? PortablePdbSettlementCandidate
                    .MicrosoftSymbolServer
                : PortablePdbSettlementCandidate
                    .ExternalProviders;

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

    private static string PositiveStoreProvenanceKey(
        PortablePdbContentIdentity identity)
        => "portable/"
            + identity.Guid.ToString("N").ToUpperInvariant()
            + identity.Stamp.ToString("X8")
            + ".provenance";

    private static async Task<PositiveStoreProbe>
        ProbePositiveStoreAsync(
        IPdbStore store,
        string key,
        string provenanceKey,
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
                null,
                PortablePdbStoreFailureKind.ReadFailed,
                Incomplete: false);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                null,
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
                        null,
                        PortablePdbStoreFailureKind.ReadFailed,
                        Incomplete: false);
                }

                if (limits is not null
                    && stream.Length > limits.MaxPortablePdbBytes)
                {
                    return new(
                        null,
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
                    PositiveStoreProvenance? provenance =
                        await TryReadPositiveStoreProvenanceAsync(
                            store,
                            provenanceKey,
                            cancellationToken).ConfigureAwait(false);
                    if (provenance is null)
                    {
                        return new(
                            null,
                            null,
                            PortablePdbStoreFailureKind
                                .ProvenanceUnavailable,
                            Incomplete: false);
                    }

                    return new(
                        new SettledPortablePdbContent(
                            store,
                            key,
                            provenance.SymbolServer),
                        provenance,
                        Failure: null,
                        Incomplete: false);
                }

                return new(
                    null,
                    null,
                    PortablePdbStoreFailureKind.InvalidCachedContent,
                    Incomplete: false);
            }
        }
        catch (IOException)
        {
            return new(
                null,
                null,
                PortablePdbStoreFailureKind.ReadFailed,
                Incomplete: false);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                null,
                null,
                PortablePdbStoreFailureKind.ReadFailed,
                Incomplete: false);
        }
    }

    private static async Task<ProvenancePublicationResult>
        PublishPositiveStoreProvenanceAsync(
        IPdbStore store,
        string key,
        PositiveStoreProvenance provenance,
        CancellationToken cancellationToken)
    {
        byte[] content = SerializeProvenance(provenance);
        try
        {
            using var stream =
                new MemoryStream(content, writable: false);
            await store.PutAsync(
                key,
                stream,
                cancellationToken).ConfigureAwait(false);
            PositiveStoreProvenance? retained =
                await TryReadPositiveStoreProvenanceAsync(
                    store,
                    key,
                    cancellationToken).ConfigureAwait(false);
            if (!SameProvenance(provenance, retained))
            {
                return new(
                    PortablePdbStoreFailureKind
                        .ProvenanceUnavailable,
                    Canceled: false);
            }

            return new(Failure: null, Canceled: false);
        }
        catch (OperationCanceledException)
        {
            return new(Failure: null, Canceled: true);
        }
        catch (InvalidDataException)
        {
            return new(
                PortablePdbStoreFailureKind.PublicationNotRetained,
                Canceled: false);
        }
        catch (IOException)
        {
            return new(
                PortablePdbStoreFailureKind.PublicationNotRetained,
                Canceled: false);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                PortablePdbStoreFailureKind.PublicationNotRetained,
                Canceled: false);
        }
        catch (ArgumentException)
        {
            return new(
                PortablePdbStoreFailureKind.PublicationNotRetained,
                Canceled: false);
        }
    }

    private static async Task<PositiveStoreProvenance?>
        TryReadPositiveStoreProvenanceAsync(
        IPdbStore store,
        string key,
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
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (stream is null)
            return null;

        await using (stream.ConfigureAwait(false))
        {
            if (!stream.CanRead)
                return null;

            long? declaredLength =
                stream.CanSeek
                    ? stream.Length - stream.Position
                    : null;
            byte[] bytes;
            try
            {
                bytes =
                    await BoundedContentReader.ReadAllBytesAsync(
                        stream,
                        MaxProvenanceBytes,
                        declaredLength,
                        cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return null;
            }

            return ParseProvenance(bytes);
        }
    }

    private static byte[] SerializeProvenance(
        PositiveStoreProvenance provenance)
    {
        string? encodedCoordinates =
            provenance.Coordinates?.ToString();
        byte[] coordinates =
            encodedCoordinates is null
                ? []
                : Encoding.UTF8.GetBytes(
                    encodedCoordinates);
        byte[] symbolServer =
            provenance.SymbolServer is null
                ? []
                : Encoding.UTF8.GetBytes(
                    provenance.SymbolServer);
        int length =
            13 + coordinates.Length + symbolServer.Length;
        if (length > MaxProvenanceBytes)
        {
            throw new InvalidDataException(
                "Portable PDB supplying provenance exceeds the settlement limit.");
        }

        byte[] content = new byte[length];
        Span<byte> span = content;
        BinaryPrimitives.WriteUInt32LittleEndian(
            span,
            ProvenanceMagic);
        span[4] = checked((byte)provenance.Source);
        BinaryPrimitives.WriteInt32LittleEndian(
            span[5..],
            provenance.Coordinates is null
                ? -1
                : coordinates.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            span[9..],
            provenance.SymbolServer is null
                ? -1
                : symbolServer.Length);
        coordinates.CopyTo(span[13..]);
        symbolServer.CopyTo(
            span[(13 + coordinates.Length)..]);
        return content;
    }

    private static PositiveStoreProvenance? ParseProvenance(
        ReadOnlySpan<byte> content)
    {
        if (content.Length < 13
            || BinaryPrimitives.ReadUInt32LittleEndian(content)
                != ProvenanceMagic)
        {
            return null;
        }

        var source =
            (PortablePdbSettlementSource)content[4];
        if (!Enum.IsDefined(source)
            || source
                is PortablePdbSettlementSource.Embedded
                    or PortablePdbSettlementSource.PositiveStore)
        {
            return null;
        }

        int coordinatesLength =
            BinaryPrimitives.ReadInt32LittleEndian(
                content[5..]);
        int symbolServerLength =
            BinaryPrimitives.ReadInt32LittleEndian(
                content[9..]);
        if (coordinatesLength < -1
            || symbolServerLength < -1)
        {
            return null;
        }

        int encodedCoordinatesLength =
            Math.Max(0, coordinatesLength);
        int encodedSymbolServerLength =
            Math.Max(0, symbolServerLength);
        if (13 + encodedCoordinatesLength
                + encodedSymbolServerLength
            != content.Length)
        {
            return null;
        }

        InertString? coordinates =
            coordinatesLength < 0
                ? null
                : InertString.FromEncoded(
                    TextPolicy.Field,
                    Encoding.UTF8.GetString(
                        content.Slice(
                            13,
                            encodedCoordinatesLength)));
        string? symbolServer =
            symbolServerLength < 0
                ? null
                : new InertString(
                    TextPolicy.Field,
                    Encoding.UTF8.GetString(
                        content.Slice(
                            13 + encodedCoordinatesLength,
                            encodedSymbolServerLength)))
                    .ToString();
        return new(source, coordinates, symbolServer);
    }

    private static bool SameProvenance(
        PositiveStoreProvenance expected,
        PositiveStoreProvenance? actual)
        => actual is not null
            && expected.Source == actual.Source
            && string.Equals(
                expected.Coordinates?.ToString(),
                actual.Coordinates?.ToString(),
                StringComparison.Ordinal)
            && string.Equals(
                expected.SymbolServer,
                actual.SymbolServer,
                StringComparison.Ordinal);

    private sealed class IdentityScopedPdbStore(
        IPdbStore store,
        string identityKey,
        bool allowInitialRead) : IPdbStore
    {
        private bool _allowRead = allowInitialRead;

        public async ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            if (!Volatile.Read(ref _allowRead))
                return null;

            try
            {
                return await store.TryOpenAsync(
                    identityKey,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or ArgumentException)
            {
                throw new PdbStoreAcquisitionException(
                    PortablePdbStoreFailureKind.ReadFailed,
                    exception);
            }
        }

        public async ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await store.PutAsync(
                    identityKey,
                    content,
                    cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _allowRead, true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or ArgumentException)
            {
                throw new PdbStoreAcquisitionException(
                    PortablePdbStoreFailureKind
                        .PublicationNotRetained,
                    exception);
            }
        }

        public string? TryGetLocalPath(string key)
        {
            if (!Volatile.Read(ref _allowRead))
                return null;

            try
            {
                return store.TryGetLocalPath(identityKey);
            }
            catch (Exception exception)
                when (exception
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or ArgumentException)
            {
                throw new PdbStoreAcquisitionException(
                    PortablePdbStoreFailureKind.ReadFailed,
                    exception);
            }
        }
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
        PortablePdbAcquisitionNetworkRoute route)
        => route switch
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
