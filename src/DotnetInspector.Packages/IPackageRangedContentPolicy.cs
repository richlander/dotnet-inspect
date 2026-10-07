using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Optional host admission for the expanded entries retained by a ranged read.
/// The reservation covers cached and newly read entries before their bodies
/// are materialized. It is separate from complete archive transfer admission.
/// </summary>
public interface IPackageRangedContentPolicy
{
    ValueTask<IPackageRangedContentReservation> ReserveRangedAsync(
        PackageSourceCoordinate coordinate,
        long expandedBytes,
        CancellationToken cancellationToken);
}

/// <summary>
/// A host charge for selected expanded content. Completion publishes the
/// checked content; disposal releases an incomplete charge.
/// </summary>
public interface IPackageRangedContentReservation : IDisposable
{
    void Complete(RangedPackageContent content);
}
