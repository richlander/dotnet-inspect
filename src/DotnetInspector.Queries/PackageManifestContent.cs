using DotnetInspector.Packages;

namespace DotnetInspector.Queries;

internal static class PackageManifestContent
{
    public static string? FindRootManifest(IPackageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string[] manifests =
        [
            .. content.EnumerateEntries()
                .Where(path => path.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (manifests.Any(path => path.Contains('\\')))
        {
            throw new InvalidDataException(
                "Package manifest paths must use package-root separators.");
        }

        string[][] manifestSegments =
        [
            .. manifests.Select(path => path.Split('/')),
        ];
        if (manifestSegments.Any(segments =>
            segments.Any(segment => !PackageEntryPath.IsSafeSegment(segment))))
        {
            throw new InvalidDataException(
                "Package manifest paths must contain safe package-entry segments.");
        }

        string[] roots =
        [
            .. manifestSegments
                .Where(segments => segments.Length == 1)
                .Select(segments => segments[0]),
        ];

        return roots.Length switch
        {
            0 => null,
            1 => roots[0],
            _ => throw new InvalidDataException(
                "Package content contains more than one root manifest."),
        };
    }
}
