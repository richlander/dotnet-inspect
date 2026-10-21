using System.Diagnostics.CodeAnalysis;
using NuGetFetch;

namespace DotnetInspector.Packages;

internal sealed class PackageSourceDependencyVersionObservations
{
    private readonly Dictionary<
        (ConfiguredPackageAuthority Authority, string PackageId),
        PackageSourceOperationResult<PackageVersionResult>> _observations = [];

    internal bool TryGet(
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity source,
        string packageId,
        [NotNullWhen(true)]
        out PackageSourceOperationResult<PackageVersionResult>? observation)
    {
        if (_observations.TryGetValue(
                (authority, packageId.ToLowerInvariant()),
                out observation)
            && ReferenceEquals(observation.Value?.Source, source))
        {
            return true;
        }

        observation = null;
        return false;
    }

    internal void Retain(
        string packageId,
        IReadOnlyList<ConfiguredPackageAuthority> authorities,
        IReadOnlyList<PackageSourceOperationResult<PackageVersionResult>> observations)
    {
        string normalizedId = packageId.ToLowerInvariant();
        for (int index = 0; index < authorities.Count; index++)
            _observations[(authorities[index], normalizedId)] = observations[index];
    }

    internal void Clear() => _observations.Clear();
}
