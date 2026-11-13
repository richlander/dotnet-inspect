namespace DotnetInspector.Packages;

/// <summary>Validates package-root path segments shared by package-content stores.</summary>
public static class PackageEntryPath
{
    internal static string GetLibraryPopulationPath(
        string assetPath,
        string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        string[] segments = assetPath.Split('/');
        int populationSegments = LibraryPopulationSegments(
                segments,
                targetFramework)
            ?? throw new InvalidOperationException(
                $"'{assetPath}' is not in a recognized Library population.");
        return $"{string.Join('/', segments[..populationSegments])}/";
    }

    internal static string? TryGetLibraryRelativePath(
        string assetPath,
        string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        string[] segments = assetPath.Split('/');
        int? populationSegments = LibraryPopulationSegments(
            segments,
            targetFramework);
        return populationSegments is null
            || segments.Length <= populationSegments.Value
                ? null
                : string.Join(
                    '/',
                    segments[populationSegments.Value..]);
    }

    static int? LibraryPopulationSegments(
        string[] segments,
        string targetFramework) =>
            segments.Length >= 5
            && segments[0].Equals(
                "runtimes",
                StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals(
                "lib",
                StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals(
                targetFramework,
                StringComparison.OrdinalIgnoreCase)
                ? 4
                : segments.Length >= 4
                && segments[0].Equals(
                    "tools",
                    StringComparison.OrdinalIgnoreCase)
                && segments[1].Equals(
                    targetFramework,
                    StringComparison.OrdinalIgnoreCase)
                && segments[2].Equals(
                    "any",
                    StringComparison.OrdinalIgnoreCase)
                    ? 3
                    : segments.Length >= 3
                    && (segments[0].Equals(
                            "lib",
                            StringComparison.OrdinalIgnoreCase)
                        || segments[0].Equals(
                            "ref",
                            StringComparison.OrdinalIgnoreCase))
                    && segments[1].Equals(
                        targetFramework,
                        StringComparison.OrdinalIgnoreCase)
                        ? 2
                        : segments.Length >= 2
                        && segments[0].Equals(
                            "lib",
                            StringComparison.OrdinalIgnoreCase)
                        && targetFramework.Equals(
                            "any",
                            StringComparison.OrdinalIgnoreCase)
                            ? 1
                            : null;

    /// <summary>
    /// Returns the final lexical segment of a package or archive entry.
    /// Both separator forms are recognized independently of the inspecting host.
    /// </summary>
    public static string GetLeafName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int separator = Math.Max(
            path.LastIndexOf('/'),
            path.LastIndexOf('\\'));
        return path[(separator + 1)..];
    }

    /// <summary>
    /// Returns the final lexical segment without its final extension.
    /// </summary>
    public static string GetStemName(string path)
    {
        string leaf = GetLeafName(path);
        int extension = leaf.LastIndexOf('.');
        return extension < 0
            ? leaf
            : leaf[..extension];
    }

    /// <summary>
    /// Returns the final lexical segment's final extension, including its dot.
    /// </summary>
    public static string GetExtension(string path)
    {
        string leaf = GetLeafName(path);
        int extension = leaf.LastIndexOf('.');
        return extension < 0 || extension == leaf.Length - 1
            ? string.Empty
            : leaf[extension..];
    }

    /// <summary>Recognizes the standard package tool-settings entry layout.</summary>
    public static bool IsToolSettingsPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string[] parts = path.Replace('\\', '/').Split(
            '/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is >= 2 and <= 4
            && parts[0].Equals("tools", StringComparison.OrdinalIgnoreCase)
            && parts[^1].Equals("DotnetToolSettings.xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether a value is a safe single package-entry segment.
    /// <c>ExecuteAsync_RejectsAmbiguousAndUnsafeManifestPaths</c> gates the
    /// host-neutral query boundary.
    /// </summary>
    public static bool IsSafeSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Length != 0
            && segment != "."
            && segment != ".."
            && !segment.Contains('/')
            && !segment.Contains('\\')
            && !segment.Contains(':')
            && !segment.Contains('\0');
    }

    /// <summary>
    /// Returns whether a value is a non-empty, package-root-relative path
    /// composed only of safe entry segments.
    /// </summary>
    public static bool IsSafeRelativePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length != 0
            && path.Split('/').All(IsSafeSegment);
    }
}
