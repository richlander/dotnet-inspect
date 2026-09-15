namespace DotnetInspector.Packages;

/// <summary>
/// Pure package-reference parsing, independent of package acquisition and cache initialization.
/// </summary>
public static class PackageReferenceParser
{
    /// <summary>
    /// Splits a package reference or infers name/version text from a local archive filename.
    /// </summary>
    public static (string name, string? version) Parse(string packageSource)
    {
        if (packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileNameWithoutExtension(packageSource);
            // The first segment starting with a digit begins the inferred version.
            var parts = fileName.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0 && char.IsDigit(parts[i][0]))
                {
                    var name = string.Join(".", parts.Take(i));
                    var version = string.Join(".", parts.Skip(i));
                    return (name, version);
                }
            }
            return (fileName, null);
        }

        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
            return (packageSource[..atIndex], packageSource[(atIndex + 1)..]);

        return (packageSource, null);
    }

    public static bool IsValidVersion(string? version) =>
        string.IsNullOrEmpty(version)
        || string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase)
        || version.Contains('*', StringComparison.Ordinal)
        || NuGet.Versioning.NuGetVersion.TryParse(version, out _);
}
