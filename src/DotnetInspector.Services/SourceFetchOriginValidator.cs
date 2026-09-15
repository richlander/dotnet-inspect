using ILInspector.SourceLink;

namespace DotnetInspector.Services;

internal static class SourceFetchOriginValidator
{
    public static SourceLinkFetchOriginResult Validate(
        string requestedUrl,
        string? finalUrl)
        => Validate(
            requestedUrl,
            finalUrl,
            finalUrlReliable: !OperatingSystem.IsBrowser());

    internal static SourceLinkFetchOriginResult Validate(
        string requestedUrl,
        string? finalUrl,
        bool finalUrlReliable)
    {
        if (!finalUrlReliable)
        {
            SourceLinkFetchOriginResult requested =
                SourceLinkProvenance.ValidateFetchOrigin(
                    requestedUrl,
                    requestedUrl);
            if (requested.Status == SourceLinkFetchOriginStatus.Preserved)
            {
                return new SourceLinkFetchOriginResult(
                    SourceLinkFetchOriginStatus.Changed,
                    "the transport cannot report the final response URL");
            }
        }

        return finalUrl is null
            ? new SourceLinkFetchOriginResult(
                SourceLinkFetchOriginStatus.Changed,
                "the transport did not report a final response URL")
            : SourceLinkProvenance.ValidateFetchOrigin(
                requestedUrl,
                finalUrl);
    }
}
