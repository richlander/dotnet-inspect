using System.Collections.Immutable;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// How a package identity relates to one exact platform target.
/// </summary>
public enum PlatformPruneClassification
{
    /// <summary>The target neither subsumes the identity nor ships a library of that name.</summary>
    PackageOnly,

    /// <summary>The target subsumes the identity, whether or not it ships that assembly name.</summary>
    Overlapping,

    /// <summary>The target ships a library of that name and subsumes no package of it.</summary>
    PlatformOnly,
}

/// <summary>
/// Whether a requested package version is at or below the version a platform target supplies.
/// </summary>
public enum PlatformSubsumption
{
    /// <summary>
    /// No comparison was possible. Never treat this as <see cref="Subsumed"/>: the caller decides
    /// what an absent, floating, or unparsable request means.
    /// </summary>
    NotComparable,

    /// <summary>The target supplies this version or a higher one.</summary>
    Subsumed,

    /// <summary>The target does not supply this version. The package remains a distinct subject.</summary>
    NotSubsumed,
}

/// <summary>
/// One package identity a platform target subsumes, as published in a reference pack's
/// <c>data/PackageOverrides.txt</c>.
/// </summary>
/// <param name="PackageId">The subsumed package identity.</param>
/// <param name="Family">
/// The shared framework that supplies it, such as <c>Microsoft.NETCore.App</c>. A target subsumes
/// an identity only when it references that family, so the same package is Platform for one target
/// composition and an ordinary package for another.
/// </param>
/// <param name="IsLive">
/// Whether the entry's supplied version tracks the pack's own version. A live entry's package
/// still ships in lockstep with the runtime and is bumped mechanically each patch release; a
/// frozen entry names a legacy version that never moves.
/// </param>
/// <param name="FrozenVersion">
/// The literal supplied version for a frozen entry. Null for a live entry, whose supplied version
/// is derived from the owning inventory's target instead of stored.
/// </param>
public sealed record PlatformPruneEntry(
    string PackageId,
    string Family,
    bool IsLive,
    NuGetVersion? FrozenVersion);

/// <summary>One shared framework a target references, and the exact pack version it was read from.</summary>
public sealed record PlatformPruneFamily(string Name, NuGetVersion PackVersion);

/// <summary>
/// The package identities a platform target subsumes, and the comparison that decides whether a
/// requested version is among them.
/// </summary>
/// <remarks>
/// <para>
/// An inventory is read per shared framework and composed for a target. A console app references
/// only <c>Microsoft.NETCore.App</c>; a web app also references <c>Microsoft.AspNetCore.App</c>
/// and subsumes more. On <c>net10.0</c> a console app has no
/// <c>Microsoft.Extensions.*</c> package pruned at all, while the same app on <c>net11.0</c> has
/// nine, because those libraries moved into the base framework. Composition is therefore part of
/// the question, not a detail.
/// </para>
/// <para>
/// An inventory carries the exact pack version each family was read from, so a derived supplied
/// version can never adopt a version observed later by discovery. Selecting a different target
/// requires reading that target's inventory.
/// </para>
/// </remarks>
public sealed class PlatformPruneInventory
{
    readonly ImmutableDictionary<string, PlatformPruneEntry> entries;
    readonly ImmutableDictionary<string, NuGetVersion> packVersions;

    PlatformPruneInventory(
        string targetFramework,
        ImmutableDictionary<string, NuGetVersion> packVersions,
        ImmutableDictionary<string, PlatformPruneEntry> entries)
    {
        TargetFramework = targetFramework;
        this.packVersions = packVersions;
        this.entries = entries;
    }

    /// <summary>The target framework this inventory describes, such as <c>net11.0</c>.</summary>
    public string TargetFramework { get; }

    /// <summary>The shared frameworks composed into this inventory, ordered by name.</summary>
    public IEnumerable<PlatformPruneFamily> Families =>
        packVersions
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PlatformPruneFamily(pair.Key, pair.Value));

    /// <summary>The subsumed identities, ordered by package id.</summary>
    public IEnumerable<PlatformPruneEntry> Entries =>
        entries.Values.OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase);

    /// <summary>The entries whose supplied version tracks their family's pack version.</summary>
    public IEnumerable<PlatformPruneEntry> LiveEntries => Entries.Where(entry => entry.IsLive);

    /// <summary>
    /// Reads one shared framework's <c>data/PackageOverrides.txt</c>. Each non-empty line is
    /// <c>PackageId|Version</c>; an entry whose version equals <paramref name="packVersion"/> is
    /// live. A malformed line is a failure rather than a silently dropped identity, because a
    /// dropped identity would turn an overlapping package into a package-only one.
    /// </summary>
    public static PlatformPruneInventory ForFamily(
        string family,
        string targetFramework,
        NuGetVersion packVersion,
        IEnumerable<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentNullException.ThrowIfNull(packVersion);
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
            if (!NuGetVersion.TryParse(versionText, out var supplied))
            {
                throw new FormatException(
                    $"Malformed supplied version for '{packageId}' in {family} {targetFramework}: '{versionText}'.");
            }

            var isLive = supplied == packVersion;
            builder[packageId] = new PlatformPruneEntry(
                packageId,
                family,
                isLive,
                isLive ? null : supplied);
        }

        return new PlatformPruneInventory(
            targetFramework,
            ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [KeyValuePair.Create(family, packVersion)]),
            builder.ToImmutable());
    }

    /// <summary>
    /// Composes the families a target references. Inventories must agree on the target framework,
    /// because a composition across frameworks would describe no real target.
    /// </summary>
    /// <remarks>
    /// The shipped families publish disjoint identities, so the conflict rule below is defensive.
    /// When two families do supply one identity, the lower supplied version wins: that is the
    /// direction that cannot over-claim, and over-claiming is the failure that matters.
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
            if (!string.Equals(inventory.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Cannot compose prune inventories across '{targetFramework}' and "
                    + $"'{inventory.TargetFramework}'.",
                    nameof(inventories));
            }
        }

        var packs = ImmutableDictionary.CreateBuilder<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
        var entries = ImmutableDictionary.CreateBuilder<string, PlatformPruneEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var inventory in composed)
        {
            foreach (var (family, packVersion) in inventory.packVersions)
            {
                packs[family] = packVersion;
            }

            foreach (var entry in inventory.entries.Values)
            {
                if (!entries.TryGetValue(entry.PackageId, out var existing))
                {
                    entries[entry.PackageId] = entry;
                    continue;
                }

                var incoming = inventory.Supplied(entry);
                var kept = composed.First(c => c.packVersions.ContainsKey(existing.Family)).Supplied(existing);
                if (incoming < kept)
                {
                    entries[entry.PackageId] = entry;
                }
            }
        }

        return new PlatformPruneInventory(targetFramework, packs.ToImmutable(), entries.ToImmutable());
    }

    NuGetVersion Supplied(PlatformPruneEntry entry) =>
        entry.IsLive ? packVersions[entry.Family] : entry.FrozenVersion!;

    /// <summary>
    /// Classifies a package identity against this target. <paramref name="hasPlatformLibrary"/>
    /// comes from the target's library catalog, which this owner does not define.
    /// </summary>
    public PlatformPruneClassification Classify(string packageId, bool hasPlatformLibrary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return entries.ContainsKey(packageId)
            ? PlatformPruneClassification.Overlapping
            : hasPlatformLibrary
                ? PlatformPruneClassification.PlatformOnly
                : PlatformPruneClassification.PackageOnly;
    }

    /// <summary>
    /// The version this target supplies for a subsumed identity, and the family supplying it. A
    /// live entry derives the version from its family's pack version; a frozen entry uses its
    /// stored literal.
    /// </summary>
    public bool TryGetSuppliedVersion(
        string packageId,
        out NuGetVersion suppliedVersion,
        out string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!entries.TryGetValue(packageId, out var entry))
        {
            suppliedVersion = null!;
            family = string.Empty;
            return false;
        }

        suppliedVersion = Supplied(entry);
        family = entry.Family;
        return true;
    }

    /// <summary>
    /// Whether this target supplies <paramref name="requestedVersion"/> of
    /// <paramref name="packageId"/>. Every uncertainty resolves away from
    /// <see cref="PlatformSubsumption.Subsumed"/>, because over-claiming would delegate a caller
    /// to an older implementation and hide that a newer package exists.
    /// </summary>
    public PlatformSubsumption Subsumes(string packageId, NuGetVersion? requestedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!entries.ContainsKey(packageId))
        {
            return PlatformSubsumption.NotSubsumed;
        }

        if (requestedVersion is null)
        {
            return PlatformSubsumption.NotComparable;
        }

        return TryGetSuppliedVersion(packageId, out var supplied, out _)
            && requestedVersion <= supplied
                ? PlatformSubsumption.Subsumed
                : PlatformSubsumption.NotSubsumed;
    }

    /// <summary>
    /// Whether this target supplies <paramref name="requestedVersion"/>, parsing the request.
    /// An absent or unparsable request is <see cref="PlatformSubsumption.NotComparable"/> rather
    /// than a comparison against a guessed version.
    /// </summary>
    public PlatformSubsumption Subsumes(string packageId, string? requestedVersion) =>
        NuGetVersion.TryParse(requestedVersion, out var parsed)
            ? Subsumes(packageId, parsed)
            : entries.ContainsKey(packageId)
                ? PlatformSubsumption.NotComparable
                : PlatformSubsumption.NotSubsumed;
}
