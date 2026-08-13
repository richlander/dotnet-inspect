using SLF = SourceLinkFetch;

namespace DotnetInspector.Services;

internal static class SourceFetchOriginValidator
{
    public static SLF.SourceLinkFetchOriginResult Validate(
        string requestedUrl,
        string? finalUrl)
        => Validate(
            requestedUrl,
            finalUrl,
            finalUrlReliable: !OperatingSystem.IsBrowser());

    internal static SLF.SourceLinkFetchOriginResult Validate(
        string requestedUrl,
        string? finalUrl,
        bool finalUrlReliable)
    {
        if (!finalUrlReliable)
        {
            SLF.SourceLinkFetchOriginResult requested =
                SLF.SourceLinkProvenance.ValidateFetchOrigin(
                    requestedUrl,
                    requestedUrl);
            if (requested.Status == SLF.SourceLinkFetchOriginStatus.Preserved)
            {
                return new SLF.SourceLinkFetchOriginResult(
                    SLF.SourceLinkFetchOriginStatus.Changed,
                    "the transport cannot report the final response URL");
            }
        }

        return finalUrl is null
            ? new SLF.SourceLinkFetchOriginResult(
                SLF.SourceLinkFetchOriginStatus.Changed,
                "the transport did not report a final response URL")
            : SLF.SourceLinkProvenance.ValidateFetchOrigin(
                requestedUrl,
                finalUrl);
    }
}
