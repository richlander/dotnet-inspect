namespace DotnetInspector.Packages;

/// <summary>Logical directories derived from an admitted archive snapshot.</summary>
internal static class PackageArchiveDirectories
{
    internal static IReadOnlyList<string> FromPaths(IEnumerable<string> paths)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            for (int index = path.IndexOf('/'); index >= 0;
                index = path.IndexOf('/', index + 1))
            {
                if (index > 0) directories.Add(path[..index]);
            }
        }
        return Array.AsReadOnly(directories.Order(StringComparer.Ordinal).ToArray());
    }
}
