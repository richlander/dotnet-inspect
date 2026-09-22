using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.SourceLink;

namespace DotnetInspector.Queries;

/// <summary>
/// Adapts one host-authorized source-query context into SourceHouse source
/// capabilities without changing the host's local, repository, or network
/// policy.
/// </summary>
public static class AssemblyContextSourceCapabilities
{
    public static IReadOnlyList<ISourceHouseSourceCapability> Create(
        AssemblyContextSourceQueryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<ISourceHouseSourceCapability> capabilities = [];
        if (context.AllowLocalSourceReads)
            capabilities.Add(new LocalSourceCapability());
        if (context.RepositoryPaths is { Count: > 0 } paths)
            capabilities.Add(new RepositorySourceCapability(paths));
        capabilities.Add(new RemoteSourceCapability(context.SourceFetch));
        return capabilities;
    }

    private sealed class LocalSourceCapability
        : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create("local-source");

        public SourceHouseCapabilityCategory Category =>
            SourceHouseCapabilityCategory.Local;

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? bytes =
                PdbSourceHouse.TryReadVerifiedLocalSource(
                    candidate.Document);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                CapturedSource(
                    bytes,
                    maximumBytes,
                    "LocalSourceUnavailable"));
        }
    }

    private sealed class RepositorySourceCapability(
        IReadOnlyList<string> paths)
        : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create("repository-source");

        public SourceHouseCapabilityCategory Category =>
            SourceHouseCapabilityCategory.Repository;

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? bytes =
                LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                    candidate.Document,
                    paths);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                CapturedSource(
                    bytes,
                    maximumBytes,
                    "RepositorySourceUnavailable"));
        }
    }

    private sealed class RemoteSourceCapability(SourceFetch fetcher)
        : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create("remote-source");

        public SourceHouseCapabilityCategory Category =>
            SourceHouseCapabilityCategory.Remote;

        public async ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (candidate.Document.ResolvedUrl is not { Length: > 0 } url)
            {
                return new SourceHouseCapabilityOutcome.Unavailable(
                    new("SourceUrlUnavailable"));
            }

            SourceChecksumVerification checksum =
                SourceLinkService.VerifyChecksum(candidate.Document, []);
            if (checksum is SourceChecksumVerification.Unavailable
                or SourceChecksumVerification.Unsupported)
            {
                return new SourceHouseCapabilityOutcome.Rejected(
                    new(checksum.ToString()));
            }

            FetchSourceResult result =
                await fetcher.FetchVerifiedSourceBytesAsync(
                        url,
                        bytes =>
                            SourceLinkService.VerifyChecksum(
                                candidate.Document,
                                bytes.Span)
                            is SourceChecksumVerification.Exact
                                or SourceChecksumVerification
                                    .LineEndingNormalized,
                        cancellationToken)
                    .ConfigureAwait(false);
            return result switch
            {
                FetchSourceResult.Success success =>
                    CapturedSource(
                        success.Content,
                        maximumBytes,
                        "SourceUnavailable"),
                FetchSourceResult.Failure
                    {
                        Error: SourceError.NotFound,
                    } =>
                    new SourceHouseCapabilityOutcome.Unavailable(
                        new("SourceNotFound")),
                FetchSourceResult.Failure
                    {
                        Error: SourceError.ValidationFailed,
                    } =>
                    new SourceHouseCapabilityOutcome.Failed(
                        new("ChecksumMismatch")),
                FetchSourceResult.Failure failure =>
                    new SourceHouseCapabilityOutcome.Failed(
                        new(failure.Error.ToString())),
                _ => throw new InvalidOperationException(
                    "Unknown source fetch result."),
            };
        }
    }

    private static SourceHouseCapabilityOutcome CapturedSource(
        byte[]? bytes,
        int maximumBytes,
        string unavailable) =>
        bytes is null
            ? new SourceHouseCapabilityOutcome.Unavailable(
                new(unavailable))
            : bytes.Length > maximumBytes
                ? new SourceHouseCapabilityOutcome.Incomplete(
                    new("SourceBytesExceeded"))
                : new SourceHouseCapabilityOutcome.Available(bytes);
}
