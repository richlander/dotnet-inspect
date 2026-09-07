using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// What a platform target supplies for one package identity.
/// </summary>
/// <param name="Subsumption">Whether the target supplies the requested version.</param>
/// <param name="Family">
/// The shared framework carrying the entry, such as <c>Microsoft.NETCore.App</c>. Null when the
/// target has no entry for the identity.
/// </param>
/// <param name="SuppliedVersion">
/// The version the target supplies. Null when the target has no entry for the identity.
/// </param>
/// <param name="PlatformLibrary">
/// The platform library that supplies the API, when a catalog lookup was provided and answered.
/// Null otherwise, including when the identity is subsumed without a library of that name —
/// <c>NETStandard.Library</c> is the shape. Absence means unknown, never "no library".
/// </param>
public sealed record PlatformSupply(
    PlatformSubsumption Subsumption,
    string? Family,
    NuGetVersion? SuppliedVersion,
    string? PlatformLibrary)
{
    /// <summary>Whether a consumer should resolve this identity to the platform.</summary>
    /// <remarks>
    /// True only for <see cref="PlatformSubsumption.Subsumed"/>. Both
    /// <see cref="PlatformSubsumption.NotSubsumed"/> and
    /// <see cref="PlatformSubsumption.NotComparable"/> answer false, because an unanswerable
    /// comparison must not delegate a caller to an older implementation.
    /// </remarks>
    public bool DelegatesToPlatform => Subsumption == PlatformSubsumption.Subsumed;

    /// <summary>A target with no entry for the identity supplies nothing for it.</summary>
    public static PlatformSupply None { get; } =
        new(PlatformSubsumption.NotSubsumed, null, null, null);
}

/// <summary>
/// The pure transform from a package identity, in the context of one platform target, to what
/// that target supplies for it.
/// </summary>
/// <remarks>
/// <para>
/// This is policy over the <see cref="PlatformPruneInventory"/> fact rather than part of it. It
/// performs no I/O, holds no state, and reaches no catalog of its own: a caller that wants the
/// supplying library name passes a lookup, and one that does not gets the subsumption answer
/// alone.
/// </para>
/// <para>
/// The lookup is injected rather than resolved here because assembly identity belongs to the
/// platform library catalog, a separate owner. Deriving a library name from the package id would
/// reintroduce exactly the name heuristic this design replaces: `System.Text.Json` happens to
/// match, `NETStandard.Library` has no library at all, and neither fact is legible from the id.
/// </para>
/// <para>
/// Placement is provisional. The package input abstraction tracked by #6266 separates input,
/// transport, and policy; this type is expected to move into that policy layer once it exists.
/// </para>
/// </remarks>
public static class PlatformPrunePolicy
{
    /// <summary>
    /// Decides what <paramref name="inventory"/>'s target supplies for
    /// <paramref name="coordinate"/>.
    /// </summary>
    /// <param name="inventory">The composed inventory for one target.</param>
    /// <param name="coordinate">
    /// The package acquisition coordinate being asked about. Its framework, when present, must
    /// name the inventory's target: subsumption is a per-framework fact, so answering a
    /// `net8.0` question from a `net11.0` inventory would be a different question quietly
    /// answered. Its runtime identifier is ignored, because pruning is RID-independent.
    /// </param>
    /// <param name="platformLibrary">
    /// Optional catalog lookup from package identity to the platform library supplying it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The coordinate names a different target framework than the inventory describes.
    /// </exception>
    public static PlatformSupply Decide(
        PlatformPruneInventory inventory,
        PackageCoordinate coordinate,
        Func<string, string?>? platformLibrary = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinate.PackageId);

        if (coordinate.Framework is { Length: > 0 } framework
            && !string.Equals(framework, inventory.TargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The coordinate targets '{framework}' but the inventory describes "
                + $"'{inventory.TargetFramework}'.",
                nameof(coordinate));
        }

        if (!inventory.TryGetEntry(coordinate.PackageId, out PlatformPruneEntry entry))
        {
            return PlatformSupply.None;
        }

        // An omitted coordinate version floats to the latest acceptable version rather than
        // meaning "no version", so it is not comparable until something resolves it. Either way
        // the answer is not Subsumed.
        PlatformSubsumption subsumption = coordinate.Version is { Length: > 0 } version
            ? inventory.Subsumes(coordinate.PackageId, version)
            : PlatformSubsumption.NotComparable;

        return new PlatformSupply(
            subsumption,
            entry.Family,
            entry.SuppliedVersion,
            platformLibrary?.Invoke(entry.PackageId));
    }
}
