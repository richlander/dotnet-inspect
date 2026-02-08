using System.Text.RegularExpressions;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Utilities for resolving and converting GitHub URLs.
/// </summary>
internal static class GitHubUrlResolver
{
    /// <summary>
    /// Resolves a relative sample path to a full URL based on the source file URL.
    /// </summary>
    internal static string? ResolveSampleUrl(string sourceUrl, string relativePath)
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
    internal static string ConvertRawToBlobUrl(string url)
    {
        return url.Replace("/raw/", "/blob/");
    }

    /// <summary>
    /// Converts a raw.githubusercontent.com URL to a github.com/raw/ URL.
    /// </summary>
    internal static string? ConvertToGitHubRawUrl(string rawUrl)
    {
        if (rawUrl.StartsWith("https://raw.githubusercontent.com/"))
        {
            return rawUrl
                .Replace("https://raw.githubusercontent.com/", "https://github.com/")
                .Replace($"/{GetCommitFromUrl(rawUrl)}/", $"/raw/{GetCommitFromUrl(rawUrl)}/");
        }
        return rawUrl;
    }

    private static string? GetCommitFromUrl(string url)
    {
        var match = Regex.Match(url, @"githubusercontent\.com/[^/]+/[^/]+/([^/]+)/");
        return match.Success ? match.Groups[1].Value : null;
    }
}
