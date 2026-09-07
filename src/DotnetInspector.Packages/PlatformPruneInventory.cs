using System.Collections.Immutable;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>How precisely an inventory value describes its queried target.</summary>
public enum PlatformPrunePrecision
{
    /// <summary>
    /// The value came from a committed projection. It is comparable only when the projection's
    /// source target is also the queried target.
    /// </summary>
    Projected,

    /// <summary>The value came from the queried target's exact reference pack.</summary>
    Exact,
}

/// <summary>
/// Whether a requested package version is at or below the version a platform target supplies.
/// </summary>
public enum PlatformSubsumption
{
    /// <summary>
    /// No comparison was possible. Never treat this as <see cref="Subsumed"/>: the caller decides
    /// what an absent, floating, unparsable, or cross-target projected request means.
    /// </summary>
    NotComparable,

    /// <summary>The target supplies this version or a higher one.</summary>
    Subsumed,

    /// <summary>The target does not supply this package identity or version.</summary>
    NotSubsumed,
}

/// <summary>
/// One package identity a platform target subsumes, as published in a reference pack's
/// <c>data/PackageOverrides.txt</c>.
/// </summary>
/// <param name="PackageId">The package identity with a subsumption entry.</param>
/// <param name="Family">The shared framework that published the entry.</param>
/// <param name="SuppliedVersion">The literal supplied version published by the source pack.</param>
/// <param name="TargetPackVersion">The pack version selected by the queried target.</param>
/// <param name="SourcePackVersion">The exact pack version from which the entry was read.</param>
/// <param name="Precision">Whether the entry came from a projection or the exact queried pack.</param>
public sealed record PlatformPruneEntry(
    string PackageId,
    string Family,
    NuGetVersion SuppliedVersion,
    NuGetVersion TargetPackVersion,
    NuGetVersion SourcePackVersion,
    PlatformPrunePrecision Precision);

/// <summary>
/// One shared framework in a queried target and the source inventory used to describe it.
/// </summary>
/// <param name="Name">The shared-framework family name.</param>
/// <param name="TargetPackVersion">The pack version selected by the queried target.</param>
/// <param name="SourcePackVersion">The pack version from which inventory values were read.</param>
/// <param name="Precision">Whether those values are projected or exact.</param>
public sealed record PlatformPruneFamily(
    string Name,
    NuGetVersion TargetPackVersion,
    NuGetVersion SourcePackVersion,
    PlatformPrunePrecision Precision)
{
    /// <summary>Whether this inventory source describes the queried family target exactly.</summary>
    public bool DescribesTarget => TargetPackVersion == SourcePackVersion;
}

/// <summary>
/// The package identities a platform target subsumes, and the comparison that decides whether a
/// requested version is among them.
/// </summary>
/// <remarks>
/// <para>
/// A console app normally composes only <c>Microsoft.NETCore.App</c>; a web app also composes
/// <c>Microsoft.AspNetCore.App</c>. The resulting membership follows those actual framework
/// families rather than a package-name prefix.
/// </para>
/// <para>
/// A selected target may temporarily use a projection from another patch. Its membership remains
/// visible with the source coordinate, but a present entry is <see cref="PlatformSubsumption.NotComparable"/>
/// until exact data for the selected target replaces it.
/// </para>
/// </remarks>
public sealed class PlatformPruneInventory
{
    readonly ImmutableDictionary<string, PlatformPruneEntry> entries;
    readonly ImmutableDictionary<string, PlatformPruneFamily> families;

    PlatformPruneInventory(
        string targetFramework,
        ImmutableDictionary<string, PlatformPruneFamily> families,
        ImmutableDictionary<string, PlatformPruneEntry> entries)
    {
        TargetFramework = targetFramework;
        this.families = families;
        this.entries = entries;
    }

    /// <summary>The target framework this inventory describes, such as <c>net11.0</c>.</summary>
    public string TargetFramework { get; }

    /// <summary>The shared frameworks composed into this inventory, ordered by name.</summary>
    public IEnumerable<PlatformPruneFamily> Families =>
        families.Values.OrderBy(family => family.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The subsumption entries, ordered by package id.</summary>
    public IEnumerable<PlatformPruneEntry> Entries =>
        entries.Values.OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The inventory of a workspace with no platform. No identity has an entry and nothing is
    /// subsumed; package availability, traversal, and acquisition remain consumer decisions.
    /// </summary>
    public static PlatformPruneInventory None(string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        return new PlatformPruneInventory(
            targetFramework,
            ImmutableDictionary<string, PlatformPruneFamily>.Empty.WithComparers(
                StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary<string, PlatformPruneEntry>.Empty.WithComparers(
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the exact <c>data/PackageOverrides.txt</c> from one selected shared-framework pack.
    /// </summary>
    public static PlatformPruneInventory FromExactFamily(
        string family,
        string targetFramework,
        NuGetVersion packVersion,
        IEnumerable<string> lines) =>
        FromFamily(
            family,
            targetFramework,
            packVersion,
            packVersion,
            PlatformPrunePrecision.Exact,
            lines);

    /// <summary>
    /// Applies a committed projection to one selected shared-framework pack. When
    /// <paramref name="targetPackVersion"/> differs from <paramref name="sourcePackVersion"/>,
    /// membership remains queryable but a present entry cannot produce <c>Subsumed</c>.
    /// </summary>
    public static PlatformPruneInventory FromProjectedFamily(
        string family,
        string targetFramework,
        NuGetVersion targetPackVersion,
        NuGetVersion sourcePackVersion,
        IEnumerable<string> lines) =>
        FromFamily(
            family,
            targetFramework,
            targetPackVersion,
            sourcePackVersion,
            PlatformPrunePrecision.Projected,
            lines);

    static PlatformPruneInventory FromFamily(
        string family,
        string targetFramework,
        NuGetVersion targetPackVersion,
        NuGetVersion sourcePackVersion,
        PlatformPrunePrecision precision,
        IEnumerable<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentNullException.ThrowIfNull(targetPackVersion);
        ArgumentNullException.ThrowIfNull(sourcePackVersion);
        ArgumentNullException.ThrowIfNull(lines);

        var builder = ImmutableDictionary.CreateBuilder<string, PlatformPruneEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var text = line.AsSpan().Trim();
            if (text.IsEmpty)
            {
                continue;
            }

            var separator = text.IndexOf('|');
            if (separator <= 0 || separator == text.Length - 1)
            {
                throw new FormatException(
                    $"Malformed package-override line for {family} {targetFramework}: '{line}'.");
            }

            var packageId = text[..separator].Trim().ToString();
            var versionText = text[(separator + 1)..].Trim().ToString();
            if (!NuGetVersion.TryParse(versionText, out var suppliedVersion))
            {
                throw new FormatException(
                    $"Malformed supplied version for '{packageId}' in {family} "
                    + $"{targetFramework}: '{versionText}'.");
            }

            builder[packageId] = new PlatformPruneEntry(
                packageId,
                family,
                suppliedVersion,
                targetPackVersion,
                sourcePackVersion,
                precision);
        }

        var familyValue = new PlatformPruneFamily(
            family,
            targetPackVersion,
            sourcePackVersion,
            precision);
        return new PlatformPruneInventory(
            targetFramework,
            ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [KeyValuePair.Create(family, familyValue)]),
            builder.ToImmutable());
    }

    /// <summary>
    /// Composes the families a target references. Inventories must agree on the target framework,
    /// because a composition across frameworks would describe no real target.
    /// </summary>
    /// <remarks>
    /// Published families currently have disjoint identities. If two families publish one
    /// identity, the lower literal supplied version wins because that cannot over-claim.
    /// </remarks>
    public static PlatformPruneInventory Compose(IEnumerable<PlatformPruneInventory> inventories)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        var composed = inventories.ToArray();
        if (composed.Length == 0)
        {
            throw new ArgumentException(
                "A platform prune composition needs at least one family.",
                nameof(inventories));
        }

        var targetFramework = composed[0].TargetFramework;
        foreach (var inventory in composed)
        {
            if (!string.Equals(
                    inventory.TargetFramework,
                    targetFramework,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Cannot compose prune inventories across '{targetFramework}' and "
                    + $"'{inventory.TargetFramework}'.",
                    nameof(inventories));
            }
        }

        var familyBuilder = ImmutableDictionary.CreateBuilder<string, PlatformPruneFamily>(
            StringComparer.OrdinalIgnoreCase);
        var entryBuilder = ImmutableDictionary.CreateBuilder<string, PlatformPruneEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var inventory in composed)
        {
            foreach (var family in inventory.families.Values)
            {
                if (familyBuilder.TryGetValue(family.Name, out var existingFamily)
                    && existingFamily != family)
                {
                    throw new ArgumentException(
                        $"Family '{family.Name}' has conflicting target or source coordinates.",
                        nameof(inventories));
                }

                familyBuilder[family.Name] = family;
            }

            foreach (var entry in inventory.entries.Values)
            {
                if (!entryBuilder.TryGetValue(entry.PackageId, out var existing)
                    || entry.SuppliedVersion < existing.SuppliedVersion
                    || (entry.SuppliedVersion == existing.SuppliedVersion
                        && entry.Precision == PlatformPrunePrecision.Exact
                        && existing.Precision == PlatformPrunePrecision.Projected))
                {
                    entryBuilder[entry.PackageId] = entry;
                }
            }
        }

        return new PlatformPruneInventory(
            targetFramework,
            familyBuilder.ToImmutable(),
            entryBuilder.ToImmutable());
    }

    /// <summary>Whether the target inventory contains a subsumption entry for this identity.</summary>
    public bool Contains(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return entries.ContainsKey(packageId);
    }

    /// <summary>
    /// Gets the owner-issued entry, including its literal, source target, and precision.
    /// </summary>
    public bool TryGetEntry(string packageId, out PlatformPruneEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return entries.TryGetValue(packageId, out entry!);
    }

    /// <summary>
    /// Whether this target supplies <paramref name="requestedVersion"/> of
    /// <paramref name="packageId"/>. Every uncertainty resolves away from
    /// <see cref="PlatformSubsumption.Subsumed"/>.
    /// </summary>
    public PlatformSubsumption Subsumes(string packageId, NuGetVersion? requestedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!entries.TryGetValue(packageId, out var entry))
        {
            return PlatformSubsumption.NotSubsumed;
        }

        if (requestedVersion is null)
        {
            return PlatformSubsumption.NotComparable;
        }

        if (!families[entry.Family].DescribesTarget)
        {
            return PlatformSubsumption.NotComparable;
        }

        return requestedVersion <= entry.SuppliedVersion
            ? PlatformSubsumption.Subsumed
            : PlatformSubsumption.NotSubsumed;
    }

    /// <summary>
    /// Whether this target supplies <paramref name="requestedVersion"/>, parsing the request.
    /// An absent or unparsable request is <see cref="PlatformSubsumption.NotComparable"/> rather
    /// than a comparison against a guessed version.
    /// </summary>
    public PlatformSubsumption Subsumes(string packageId, string? requestedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return NuGetVersion.TryParse(requestedVersion, out var parsed)
            ? Subsumes(packageId, parsed)
            : entries.ContainsKey(packageId)
                ? PlatformSubsumption.NotComparable
                : PlatformSubsumption.NotSubsumed;
    }
}
