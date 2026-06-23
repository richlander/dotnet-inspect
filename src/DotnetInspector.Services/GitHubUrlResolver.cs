namespace DotnetInspector.Services;

/// <summary>
/// Utilities for resolving and converting GitHub URLs.
/// </summary>
public static class GitHubUrlResolver
{
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
}
