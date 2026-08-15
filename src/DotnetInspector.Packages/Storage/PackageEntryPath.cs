namespace DotnetInspector.Packages;

/// <summary>Validates package-root path segments shared by package-content stores.</summary>
public static class PackageEntryPath
{
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
            && !segment.Contains('\0')
            && !Path.IsPathRooted(segment);
    }
}
