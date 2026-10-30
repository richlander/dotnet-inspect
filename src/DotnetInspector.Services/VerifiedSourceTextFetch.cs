namespace DotnetInspector.Services;

/// <summary>
/// Fetches one SourceLink document and returns text only after its portable-PDB
/// checksum verifies.
/// </summary>
public static class VerifiedSourceTextFetch
{
    public static async Task<VerifiedSourceTextResult> FetchAsync(
        SourceFetch fetcher,
        string url,
        string? checksumAlgorithm,
        byte[]? checksum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (string.IsNullOrEmpty(checksumAlgorithm) || checksum is not { Length: > 0 })
        {
            return new VerifiedSourceTextResult(
                null,
                "The portable PDB does not provide a usable source checksum.");
        }

        FetchSourceResult fetch = await fetcher.FetchVerifiedSourceBytesAsync(
            url,
            content => SourceLinkService.VerifyChecksum(checksumAlgorithm, checksum, content.Span)
                is SourceChecksumVerification.Exact
                    or SourceChecksumVerification.LineEndingNormalized,
            cancellationToken).ConfigureAwait(false);
        if (fetch is FetchSourceResult.Failure failure)
        {
            return new VerifiedSourceTextResult(
                null,
                failure.Error switch
                {
                    SourceError.RequestNotAuthorized =>
                        "The host does not authorize this SourceLink destination.",
                    SourceError.ValidationFailed =>
                        "Fetched source does not match the portable-PDB checksum.",
                    SourceError.StorageFailed =>
                        "The source-content store failed.",
                    _ => "Could not fetch SourceLink source.",
                });
        }

        return SourceLinkService.VerifySourceContent(
            checksumAlgorithm,
            checksum,
            ((FetchSourceResult.Success)fetch).Content);
    }
}
