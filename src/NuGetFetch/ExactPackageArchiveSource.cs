using System.Security.Cryptography;
using InertText;

namespace NuGetFetch;

/// <summary>
/// Configures finite admission limits for one immutable package archive.
/// </summary>
public sealed record ExactPackageArchiveSourceOptions
{
    public const int DefaultMaxArchiveEntries =
        LocalPackageSourceOptions.DefaultMaxArchiveEntries;
    public const long DefaultMaxCentralDirectoryBytes =
        LocalPackageSourceOptions.DefaultMaxCentralDirectoryBytes;
    public const long DefaultMaxManifestBytes =
        LocalPackageSourceOptions.DefaultMaxManifestBytes;
    public const long DefaultMaxPackageBytes =
        LocalPackageSourceOptions.DefaultMaxPackageBytes;

    public int MaxArchiveEntries { get; init; } =
        DefaultMaxArchiveEntries;

    public long MaxCentralDirectoryBytes { get; init; } =
        DefaultMaxCentralDirectoryBytes;

    public long MaxManifestBytes { get; init; } =
        DefaultMaxManifestBytes;

    public long MaxPackageBytes { get; init; } =
        DefaultMaxPackageBytes;

    internal static ExactPackageArchiveSourceOptions Validate(
        ExactPackageArchiveSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxArchiveEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxCentralDirectoryBytes);
        if (options.MaxCentralDirectoryBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxCentralDirectoryBytes),
                options.MaxCentralDirectoryBytes,
                "The central-directory limit cannot exceed the maximum array length.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxManifestBytes);
        if (options.MaxManifestBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxManifestBytes),
                options.MaxManifestBytes,
                "The manifest limit cannot exceed the maximum array length.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxPackageBytes);
        return options;
    }
}

/// <summary>The reason an exact package archive was not admitted.</summary>
public enum ExactPackageArchiveSourceFailureKind
{
    EmptyArchive,
    ResourceBudget,
    InvalidArchive,
}

/// <summary>One visible exact-archive admission failure.</summary>
public sealed record ExactPackageArchiveSourceFailure(
    ExactPackageArchiveSourceFailureKind Kind,
    string Message);

/// <summary>
/// The typed result of admitting one immutable package archive as a source.
/// </summary>
public abstract class ExactPackageArchiveSourceAdmission
{
    private ExactPackageArchiveSourceAdmission()
    {
    }

    /// <summary>One admitted exact-only source. The caller owns the client.</summary>
    public sealed class Available : ExactPackageArchiveSourceAdmission
    {
        internal Available(
            PackageSourceCoordinate coordinate,
            IPackageSourceClient client)
        {
            Coordinate = coordinate;
            Client = client;
        }

        public PackageSourceCoordinate Coordinate { get; }

        public IPackageSourceClient Client { get; }
    }

    /// <summary>One archive rejected before a source was published.</summary>
    public sealed class Rejected : ExactPackageArchiveSourceAdmission
    {
        internal Rejected(ExactPackageArchiveSourceFailure failure) =>
            Failure = failure;

        public ExactPackageArchiveSourceFailure Failure { get; }
    }
}

public static partial class PackageSourceClientFactory
{
    /// <summary>
    /// Snapshots and admits one immutable package archive as an exact-only
    /// source bound to <paramref name="association"/>.
    /// </summary>
    public static async Task<ExactPackageArchiveSourceAdmission>
        CreateExactArchiveAsync(
        ReadOnlyMemory<byte> content,
        PackageSourceAssociation association,
        ExactPackageArchiveSourceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);
        ExactPackageArchiveSourceOptions limits =
            ExactPackageArchiveSourceOptions.Validate(
                options ?? new ExactPackageArchiveSourceOptions());
        cancellationToken.ThrowIfCancellationRequested();
        if (content.IsEmpty)
        {
            return Rejected(
                ExactPackageArchiveSourceFailureKind.EmptyArchive,
                "The package archive is empty.");
        }
        if (content.Length > limits.MaxPackageBytes)
        {
            return Rejected(
                ExactPackageArchiveSourceFailureKind.ResourceBudget,
                "The package archive exceeds its configured byte limit.");
        }

        byte[] snapshot;
        LocalPackageArchive archive;
        string digest;
        using (var operation = new NuGetOperationDeadline(
                   new NuGetFetchOptions(),
                   Timeout.InfiniteTimeSpan,
                   cancellationToken))
        {
            try
            {
                snapshot = Snapshot(content, operation);
                using var stream =
                    new MemoryStream(snapshot, writable: false);
                archive =
                    await LocalPackageArchiveReader.ReadAsync(
                            stream,
                            snapshot.LongLength,
                            limits.MaxPackageBytes,
                            limits.MaxArchiveEntries,
                            limits.MaxCentralDirectoryBytes,
                            limits.MaxManifestBytes,
                            operation)
                        .ConfigureAwait(false);
                digest = ComputeDigest(snapshot, operation);
            }
            catch (LocalPackageSourceLimitExceededException)
            {
                return Rejected(
                    ExactPackageArchiveSourceFailureKind.ResourceBudget,
                    "The package archive exceeds a configured safety bound.");
            }
            catch (NuGetOperationTimeoutException)
            {
                return Rejected(
                    ExactPackageArchiveSourceFailureKind.ResourceBudget,
                    "Exact package archive admission exceeded its configured deadline.");
            }
            catch (InvalidDataException)
            {
                return Rejected(
                    ExactPackageArchiveSourceFailureKind.InvalidArchive,
                    "The package archive or its root manifest is invalid.");
            }
            catch (IOException)
            {
                return Rejected(
                    ExactPackageArchiveSourceFailureKind.InvalidArchive,
                    "The package archive could not be read completely.");
            }
        }

        var producer = new PackageProducerIdentity(
            OwnerCapability,
            $"nfs-exact-archive-1.{digest}",
            new InertString(
                TextPolicy.Field,
                $"package archive sha256:{digest}"));
        PackageSourceResultFactory results = CreateResultFactory(
            producer,
            association,
            PackageSourceKind.ExactArchive,
            compatibilitySourceIdentity: null);
        return new ExactPackageArchiveSourceAdmission.Available(
            archive.Coordinate,
            new ExactPackageArchiveSourceClient(
                snapshot,
                archive,
                results));
    }

    private static ExactPackageArchiveSourceAdmission.Rejected Rejected(
        ExactPackageArchiveSourceFailureKind kind,
        string message) =>
        new(new(kind, message));

    private static byte[] Snapshot(
        ReadOnlyMemory<byte> content,
        NuGetOperationDeadline operation)
    {
        byte[] snapshot = GC.AllocateUninitializedArray<byte>(content.Length);
        const int ChunkSize = 1024 * 1024;
        for (int offset = 0; offset < content.Length; offset += ChunkSize)
        {
            operation.ThrowIfExpired();
            int length = Math.Min(ChunkSize, content.Length - offset);
            content.Span.Slice(offset, length)
                .CopyTo(snapshot.AsSpan(offset, length));
        }

        operation.ThrowIfExpired();
        return snapshot;
    }

    private static string ComputeDigest(
        ReadOnlySpan<byte> content,
        NuGetOperationDeadline operation)
    {
        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int ChunkSize = 64 * 1024;
        while (!content.IsEmpty)
        {
            operation.ThrowIfExpired();
            int length = Math.Min(content.Length, ChunkSize);
            hash.AppendData(content[..length]);
            content = content[length..];
        }

        operation.ThrowIfExpired();
        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }
}

internal sealed class ExactPackageArchiveSourceClient(
    byte[] content,
    LocalPackageArchive archive,
    PackageSourceResultFactory results)
    : IPackageSourceClient
{
    public PackageSourceResultIdentity Source => results.Source;

    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.Manifest
        | PackageSourceCapabilities.PackagePayload;

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(take);
        return UnsupportedSearch(cancellationToken, operationContext);
    }

    public Task<PackageSourceOperationResult<PackageSearchResult>>
        SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegative(take);
        return UnsupportedSearch(cancellationToken, operationContext);
    }

    public Task<PackageSourceOperationResult<PackageVersionResult>>
        GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        return PackageSourceOperation.CaptureVersionsAsync(
            results,
            () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(cancellationToken, operationContext);
                operation.ThrowIfExpired();
                return Task.FromException<PackageVersionResult>(
                    new NuGetSourceCapabilityUnavailableException());
            },
            cancellationToken,
            operationContext);
    }

    public Task<PackageSourceOperationResult<PackageSourceManifest>>
        GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return PackageSourceOperation.CaptureManifestAsync(
            results,
            coordinate,
            () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(cancellationToken, operationContext);
                operation.ThrowIfExpired();
                if (coordinate != archive.Coordinate)
                    throw new LocalPackageSourceNotFoundException();

                PackageSourceManifest manifest = results.Manifest(
                    coordinate,
                    archive.Manifest);
                operation.ThrowIfExpired();
                return Task.FromResult(manifest);
            },
            cancellationToken,
            operationContext);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>>
        GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return await PackageSourceOperation.CapturePackageAsync(
            results,
            coordinate,
            () =>
            {
                NuGetOperationDeadline? operation =
                    CreateOperation(cancellationToken, operationContext);
                try
                {
                    operation.ThrowIfExpired();
                    if (coordinate != archive.Coordinate)
                        throw new LocalPackageSourceNotFoundException();

                    var archiveStream =
                        new MemoryStream(content, writable: false);
                    Stream payloadStream;
                    try
                    {
                        payloadStream = new LocalPackagePayloadStream(
                            archiveStream,
                            operation,
                            Source);
                    }
                    catch
                    {
                        archiveStream.Dispose();
                        throw;
                    }

                    try
                    {
                        PackageSourcePayload payload = results.Payload(
                            coordinate,
                            PackageSourcePayloadKind.Package,
                            payloadStream,
                            content.LongLength);
                        operation = null;
                        return Task.FromResult(payload);
                    }
                    catch
                    {
                        payloadStream.Dispose();
                        throw;
                    }
                }
                finally
                {
                    operation?.Dispose();
                }
            },
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    public Task<PackageSourceOperationResult<PackageSourcePayload>>
        TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return PackageSourceOperation.CaptureSymbolsAsync(
            results,
            coordinate,
            () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(cancellationToken, operationContext);
                operation.ThrowIfExpired();
                return Task.FromException<PackageSourcePayload>(
                    new NuGetSourceCapabilityUnavailableException());
            },
            cancellationToken,
            operationContext);
    }

    public void Dispose()
    {
    }

    private Task<PackageSourceOperationResult<PackageSearchResult>>
        UnsupportedSearch(
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext) =>
        PackageSourceOperation.CaptureSearchAsync(
            results,
            () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(cancellationToken, operationContext);
                operation.ThrowIfExpired();
                return Task.FromException<PackageSearchResult>(
                    new NuGetSourceCapabilityUnavailableException());
            },
            cancellationToken,
            operationContext);

    private NuGetOperationDeadline CreateOperation(
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext) =>
        operationContext is null
            ? new NuGetOperationDeadline(
                new NuGetFetchOptions(),
                Timeout.InfiniteTimeSpan,
                cancellationToken,
                Source)
            : operationContext.CreateDeadline(
                Timeout.InfiniteTimeSpan,
                cancellationToken,
                Source);
}
