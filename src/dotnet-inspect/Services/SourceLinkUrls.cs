using System.Text.RegularExpressions;

namespace DotnetInspector.Services;

/// <summary>
/// Classifies SourceLink URLs by mutability. Only commit-pinned (content-addressed) URLs are
/// immutable and therefore safe to cache permanently; everything else must be re-validated.
/// </summary>
internal static partial class SourceLinkUrls
{
    /// <summary>
    /// Matches URLs whose path embeds a full 40-hex commit SHA, e.g.
    /// <c>https://raw.githubusercontent.com/&lt;org&gt;/&lt;repo&gt;/&lt;40-hex-sha&gt;/...</c>.
    /// These are content-addressed: the bytes at the URL can never change.
    /// </summary>
    [GeneratedRegex(
        @"^https://raw\.githubusercontent\.com/[^/]+/[^/]+/[0-9a-fA-F]{40}/",
        RegexOptions.IgnoreCase)]
    private static partial Regex ImmutableUrlRegex();

    public static bool IsImmutable(string url) => ImmutableUrlRegex().IsMatch(url);
}
