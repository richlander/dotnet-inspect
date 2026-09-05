using System.Collections.Immutable;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// A package-query row. Manifest facts exist only after explicit acquisition;
/// unavailable source metadata is not replaced by zero or false.
/// </summary>
public sealed record PackageQueryPackage(
    string PackageId,
    string Version,
    ImmutableArray<string> Owners,
    long? TotalDownloads,
    bool? Verified,
    PackageSourceResultIdentity Source,
    PackageManifestFacts? Manifest = null,
    string? Description = null)
{
    public PackageQueryPackage(PackageProfileMatch profile)
        : this(
            profile.PackageId,
            profile.Version,
            profile.Owners,
            profile.TotalDownloads,
            profile.Verified,
            profile.Source,
            profile.Manifest)
    {
    }

    internal PackageManifestFacts RequiredManifest =>
        Manifest ?? throw new InvalidOperationException(
            "Manifest facet evaluation requires acquired manifest facts.");
}
