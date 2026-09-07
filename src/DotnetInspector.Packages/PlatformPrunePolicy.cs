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
    /// Decides what <paramref name="inventory"/>'s target supplies for <paramref name="packageId"/>
    /// at <paramref name="requestedVersion"/>.
    /// </summary>
    /// <param name="inventory">The composed inventory for one target.</param>
    /// <param name="packageId">The package identity being asked about.</param>
    /// <param name="requestedVersion">
    /// The requested version, or null when the caller has none. A null request is
    /// <see cref="PlatformSubsumption.NotComparable"/> rather than an assumed match.
    /// </param>
    /// <param name="platformLibrary">
    /// Optional catalog lookup from package identity to the platform library supplying it.
    /// </param>
    public static PlatformSupply Decide(
        PlatformPruneInventory inventory,
        string packageId,
        NuGetVersion? requestedVersion,
        Func<string, string?>? platformLibrary = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        if (!inventory.TryGetEntry(packageId, out PlatformPruneEntry entry))
        {
            return PlatformSupply.None;
        }

        return new PlatformSupply(
            inventory.Subsumes(packageId, requestedVersion),
            entry.Family,
            entry.SuppliedVersion,
            platformLibrary?.Invoke(entry.PackageId));
    }

    /// <summary>
    /// Decides using a requested version in text form. An unparsable version is
    /// <see cref="PlatformSubsumption.NotComparable"/> for a known identity rather than a
    /// comparison against a guessed value.
    /// </summary>
    public static PlatformSupply Decide(
        PlatformPruneInventory inventory,
        string packageId,
        string? requestedVersion,
        Func<string, string?>? platformLibrary = null) =>
        Decide(
            inventory,
            packageId,
            NuGetVersion.TryParse(requestedVersion, out NuGetVersion? parsed) ? parsed : null,
            platformLibrary);
}
