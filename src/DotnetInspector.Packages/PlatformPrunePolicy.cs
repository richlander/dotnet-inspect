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
public sealed record PlatformSupply(
    PlatformSubsumption Subsumption,
    string? Family,
    NuGetVersion? SuppliedVersion)
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
        new(PlatformSubsumption.NotSubsumed, null, null);
}

/// <summary>
/// The pure transform from a package identity, in the context of one platform target, to what
/// that target supplies for it.
/// </summary>
/// <remarks>
/// <para>
/// This is policy over the <see cref="PlatformPruneInventory"/> fact rather than part of it.
/// It performs no I/O, holds no state, and does not infer package-to-library correspondence.
/// The contract is owned by <c>platform-package-supply-policy.md</c>.
/// </para>
/// <para>
/// Placement is settled by the input owner rather than provisional.
/// <c>package-dependency-evidence.md</c> names package-pruning policy as the first consumer of
/// its shape and states that it "does not move pruning policy into this owner", so this type
/// stays here and adopts that shape as its input. That adoption is a later step in the input
/// owner's own plan.
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
    /// <exception cref="ArgumentException">
    /// The coordinate names a different target framework than the inventory describes.
    /// </exception>
    public static PlatformSupply Decide(
        PlatformPruneInventory inventory,
        PackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(coordinate);

        if (PackageCoordinateResolver.Validate(coordinate) is { } invalid)
        {
            throw new ArgumentException(invalid.Message, nameof(coordinate));
        }

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
            entry.SuppliedVersion);
    }
}
