using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// Compares exact package versions by NuGet semantic-version precedence.
/// </summary>
public static class PackageVersionPrecedence
{
    /// <summary>
    /// Compares two exact package-version strings.
    /// </summary>
    public static int Compare(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        return VersionComparer.VersionRelease.Compare(
            NuGetVersion.Parse(left),
            NuGetVersion.Parse(right));
    }
}
