using System.Text.RegularExpressions;

namespace DotnetInspector.Services;

/// <summary>
/// Utilities for resolving and converting GitHub URLs.
/// </summary>
public static class GitHubUrlResolver
{
    private static readonly Regex GitHubFileUrlRegex = new(
        @"https://github\.com/([^/\s)]+/[^/\s)]+)/(?<kind>blob|raw)/([^/\s)]+)/([^\s)]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Resolves a relative sample path to a full URL based on the source file URL.
    /// </summary>
    public static string? ResolveSampleUrl(string sourceUrl, string relativePath)
    {
        try
        {
            var uri = new Uri(sourceUrl);

            var pathSegments = uri.AbsolutePath.Split('/').ToList();

            if (pathSegments.Count > 0)
                pathSegments.RemoveAt(pathSegments.Count - 1);

            var relativeSegments = relativePath.Split('/');
            int i = 0;

            while (i < relativeSegments.Length && relativeSegments[i] == "..")
            {
                if (pathSegments.Count > 0)
                    pathSegments.RemoveAt(pathSegments.Count - 1);
                i++;
            }

            if (i < relativeSegments.Length)
            {
                var firstSegment = relativeSegments[i];
                int metadataEnd = Math.Min(4, pathSegments.Count);
                for (int j = metadataEnd; j < pathSegments.Count; j++)
                {
                    if (pathSegments[j] == firstSegment)
                    {
                        pathSegments = pathSegments.Take(j).ToList();
                        break;
                    }
                }
            }

            while (i < relativeSegments.Length)
            {
                var segment = relativeSegments[i];

                if (segment == "." || string.IsNullOrEmpty(segment))
                {
                    i++;
                    continue;
                }

                pathSegments.Add(segment);
                i++;
            }

            var newPath = string.Join("/", pathSegments);
            var resolvedUri = new UriBuilder(uri.Scheme, uri.Host)
            {
                Path = newPath
            };

            return resolvedUri.Uri.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a GitHub /raw/ URL to a /blob/ URL for browser viewing.
    /// </summary>
    public static string ConvertRawToBlobUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.TrimStart('/').Split('/', 4);
            if (parts.Length == 4)
                return $"https://github.com/{parts[0]}/{parts[1]}/blob/{parts[2]}/{parts[3]}";
        }

        return url.Replace("/raw/", "/blob/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts GitHub browser/raw URLs to raw.githubusercontent.com URLs for
    /// agent-friendly content fetching.
    /// </summary>
    public static string ConvertBlobToRawUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return url;

        var parts = uri.AbsolutePath.TrimStart('/').Split('/', 5);
        if (parts.Length == 5
            && (parts[2].Equals("blob", StringComparison.OrdinalIgnoreCase)
                || parts[2].Equals("raw", StringComparison.OrdinalIgnoreCase)))
            return $"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{parts[3]}/{parts[4]}";

        return url;
    }

    /// <summary>
    /// Normalizes GitHub file links in markdown/content from blob pages to raw
    /// file URLs. This is safe for images because blob-to-raw improves fetchability.
    /// </summary>
    public static string NormalizeGitHubFileLinksToRaw(string content)
    {
        return GitHubFileUrlRegex.Replace(content, match => ConvertBlobToRawUrl(match.Value));
    }
}
