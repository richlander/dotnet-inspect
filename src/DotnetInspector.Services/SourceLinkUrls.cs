namespace DotnetInspector.Services;

/// <summary>
/// Classifies SourceLink URLs by mutability. Only commit-pinned (content-addressed) URLs are
/// immutable and therefore safe to cache permanently; everything else must be re-validated.
/// </summary>
public static class SourceLinkUrls
{
    public static bool IsImmutable(string url) =>
        SourceLinkFetch.SourceLinkProvenance.IsImmutableContentUrl(url);
}
