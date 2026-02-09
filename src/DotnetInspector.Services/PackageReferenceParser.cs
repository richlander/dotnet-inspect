namespace DotnetInspector.Services;

/// <summary>
/// Parses package references from various formats (name@version, .nupkg files, paths).
/// </summary>
public static class PackageReferenceParser
{
    public static (string? name, string? version) ParsePackageReference(string packageSource)
    {
        if (packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(packageSource);
            return ParsePackageFileName(fileName);
        }

        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
        {
            return (packageSource[..atIndex], packageSource[(atIndex + 1)..]);
        }

        return (packageSource, null);
    }

    private static (string? name, string? version) ParsePackageFileName(string fileName)
    {
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

    public static string? ExtractVersionFromPath(string dllPath, string packageName)
    {
        var normalizedPath = dllPath.Replace('\\', '/');
        var normalizedPackageName = packageName.ToLowerInvariant();

        var searchPattern = $"/{normalizedPackageName}/";
        var index = normalizedPath.ToLowerInvariant().IndexOf(searchPattern, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var afterPackage = normalizedPath[(index + searchPattern.Length)..];
        var nextSlash = afterPackage.IndexOf('/');
        if (nextSlash > 0)
        {
            var possibleVersion = afterPackage[..nextSlash];
            if (possibleVersion.Length > 0 && char.IsDigit(possibleVersion[0]))
            {
                return possibleVersion;
            }
        }

        return null;
    }
}
