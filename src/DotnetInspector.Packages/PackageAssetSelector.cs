using System.Collections.ObjectModel;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>One assembly asset addressed inside a package.</summary>
/// <param name="EntryPath">
/// The <c>/</c>-separated, package-root relative entry path.
/// </param>
/// <param name="FileName">The entry's file name.</param>
/// <param name="RuntimeIdentifier">
/// The runtime identifier whose asset folder supplied the entry, or
/// <see langword="null"/> for a runtime-neutral <c>lib</c> asset.
/// </param>
public sealed record PackageAssetEntry(
    string EntryPath,
    string FileName,
    string? RuntimeIdentifier);

/// <summary>
/// The single effective asset universe selected inside one package: one target
/// framework, one optional runtime identifier, and every candidate assembly
/// that belongs to it.
/// </summary>
public sealed record PackageAssetUniverse
{
    internal PackageAssetUniverse(
        string targetFramework,
        string? runtimeIdentifier,
        IEnumerable<PackageAssetEntry> assets)
    {
        TargetFramework = targetFramework;
        RuntimeIdentifier = runtimeIdentifier;
        Assets = new ReadOnlyCollection<PackageAssetEntry>([.. assets]);
    }

    /// <summary>The asset folder framework selected for the whole universe.</summary>
    public string TargetFramework { get; }

    /// <summary>
    /// The runtime identifier the caller requested, or <see langword="null"/>
    /// for a runtime-neutral request. Which folder actually supplied an asset
    /// is carried per asset by
    /// <see cref="PackageAssetEntry.RuntimeIdentifier"/>.
    /// </summary>
    public string? RuntimeIdentifier { get; }

    /// <summary>
    /// Candidate assemblies in deterministic entry-path order. Satellite
    /// resource assemblies are excluded; whether an entry carries managed
    /// metadata is decided by the acquisition owner that opens it.
    /// </summary>
    public IReadOnlyList<PackageAssetEntry> Assets { get; }
}

/// <summary>The result of selecting a package's effective asset universe.</summary>
public abstract record PackageAssetSelection
{
    private protected PackageAssetSelection()
    {
    }

    /// <summary>One effective universe was selected.</summary>
    public sealed record Selected : PackageAssetSelection
    {
        internal Selected(PackageAssetUniverse universe) =>
            Universe = universe;

        public PackageAssetUniverse Universe { get; }
    }

    /// <summary>The package carries no asset folder for the request.</summary>
    public sealed record NoMatch : PackageAssetSelection
    {
        internal NoMatch(string message) => Message = message;

        public string Message { get; }
    }

    /// <summary>
    /// More than one asset folder is equally applicable, so no single universe
    /// can be selected without choosing by enumeration order.
    /// </summary>
    /// <remarks>
    /// The colliding folder names are package-controlled text and are
    /// deliberately not carried here; the message names only the framework the
    /// caller asked for.
    /// </remarks>
    public sealed record Ambiguous : PackageAssetSelection
    {
        internal Ambiguous(string message) => Message = message;

        public string Message { get; }
    }

    /// <summary>The request or the package layout is not usable.</summary>
    public sealed record Invalid : PackageAssetSelection
    {
        internal Invalid(string message) => Message = message;

        public string Message { get; }
    }
}

/// <summary>
/// Selects one effective assembly asset universe from package content, over
/// <see cref="IPackageContent.EnumerateEntries"/> only, so a host without a
/// filesystem uses the same selection as the desktop cache.
/// </summary>
/// <remarks>
/// <para>
/// Candidate folders are the standard NuGet assembly layouts:
/// <c>lib/{tfm}</c> and, when a runtime identifier is requested,
/// <c>runtimes/{rid}/lib/{tfm}</c>. Framework applicability and ordering come
/// from the shared <see cref="TfmResolver"/> rules the rest of the product
/// uses: a candidate folder must be compatible with the requested framework
/// and no newer than it, and the highest-priority candidate wins. Two distinct
/// folders of equal priority are an ambiguity, not a coin flip.
/// </para>
/// <para>
/// Exactly one framework is selected for the whole universe. Runtime-specific
/// assets replace runtime-neutral assets with the same relative path under the
/// selected framework; there is no per-asset framework fallback and no
/// runtime-identifier fallback graph, so an exact runtime identifier selects
/// its own folder or nothing.
/// </para>
/// <para>
/// Gated by <c>PackageAssetSelectorTests</c>:
/// <c>Select_TakesHighestApplicableFrameworkFolder</c> and
/// <c>Select_RejectsAnIncompatibleFrameworkFamily</c> for framework
/// selection, <c>Select_PrefersTheRuntimeSpecificAssetForTheRequestedRid</c>
/// and <c>Select_IgnoresRuntimeAssetsForAnotherRid</c> for the
/// runtime-identifier rule,
/// <c>Select_ReportsEquallyApplicableFoldersAsAmbiguous</c> for ambiguity, and
/// <c>Select_RejectsAnUnsafeCandidateEntryPath</c> for entry-path containment.
/// </para>
/// </remarks>
public static class PackageAssetSelector
{
    const string AssemblyExtension = ".dll";
    const string SatelliteSuffix = ".resources.dll";

    /// <summary>
    /// Selects the effective asset universe for
    /// <paramref name="targetFramework"/> and the optional
    /// <paramref name="runtimeIdentifier"/>.
    /// </summary>
    public static PackageAssetSelection Select(
        IPackageContent content,
        string targetFramework,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (IsBlankOrPadded(targetFramework))
        {
            return new PackageAssetSelection.Invalid(
                "A package asset request requires a target framework without surrounding whitespace.");
        }

        if (runtimeIdentifier is not null
            && IsBlankOrPadded(runtimeIdentifier))
        {
            return new PackageAssetSelection.Invalid(
                "A package asset runtime identifier cannot be empty or have surrounding whitespace.");
        }

        List<CandidateEntry> candidates = [];
        foreach (string entry in content.EnumerateEntries())
        {
            if (entry is null
                || !entry.EndsWith(
                    AssemblyExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Classify(entry, runtimeIdentifier) is not { } candidate)
                continue;

            if (!IsSafeEntryPath(entry))
            {
                // A candidate asset path that cannot address content safely is
                // a defect in the package layout, not an asset to skip
                // quietly. The offending value is deliberately not echoed.
                return new PackageAssetSelection.Invalid(
                    "A candidate assembly entry in the package has an unusable path.");
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            return new PackageAssetSelection.NoMatch(
                "The package carries no assembly asset folder.");
        }

        int targetPriority = TfmResolver.GetTfmPriority(targetFramework);
        List<string> applicable =
        [
            .. candidates
                .Select(static candidate => candidate.TargetFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(tfm =>
                    TfmResolver.GetTfmPriority(tfm) > 0
                    && TfmResolver.GetTfmPriority(tfm) <= targetPriority
                    && TfmResolver.IsTfmCompatible(tfm, targetFramework)),
        ];
        if (applicable.Count == 0)
        {
            return new PackageAssetSelection.NoMatch(
                $"The package carries no assembly assets applicable to '{targetFramework}'.");
        }

        int bestPriority = applicable.Max(TfmResolver.GetTfmPriority);
        List<string> best =
        [
            .. applicable
                .Where(tfm => TfmResolver.GetTfmPriority(tfm) == bestPriority)
                .OrderBy(static tfm => tfm, StringComparer.Ordinal),
        ];
        if (best.Count > 1)
        {
            return new PackageAssetSelection.Ambiguous(
                $"More than one asset folder is equally applicable to '{targetFramework}'.");
        }

        string selectedFramework = best[0];
        List<CandidateEntry> selected =
        [
            .. candidates.Where(candidate =>
                string.Equals(
                    candidate.TargetFramework,
                    selectedFramework,
                    StringComparison.OrdinalIgnoreCase)),
        ];

        Dictionary<string, CandidateEntry> byRelativePath =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (CandidateEntry candidate in selected)
        {
            if (!byRelativePath.TryGetValue(
                    candidate.RelativePath,
                    out CandidateEntry? existing))
            {
                byRelativePath[candidate.RelativePath] = candidate;
                continue;
            }

            bool candidateIsRuntimeSpecific =
                candidate.RuntimeIdentifier is not null;
            bool existingIsRuntimeSpecific =
                existing.RuntimeIdentifier is not null;
            if (candidateIsRuntimeSpecific && !existingIsRuntimeSpecific)
            {
                // An exact-RID asset intentionally replaces the neutral asset
                // at the same relative path.
                byRelativePath[candidate.RelativePath] = candidate;
                continue;
            }

            if (!candidateIsRuntimeSpecific && existingIsRuntimeSpecific)
            {
                continue;
            }

            // Two entries at the same specificity differ only by package-path
            // spelling. Choosing either would make archive enumeration order
            // select the bytes.
            return new PackageAssetSelection.Ambiguous(
                $"More than one assembly asset has the same identity in the selected '{selectedFramework}' universe.");
        }

        var assetPaths = new HashSet<string>(
            byRelativePath.Values.Select(
                static candidate => candidate.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        List<PackageAssetEntry> assets =
        [
            .. byRelativePath.Values
                .Where(candidate => !IsSatelliteAsset(candidate, assetPaths))
                .OrderBy(
                    static candidate => candidate.EntryPath,
                    StringComparer.Ordinal)
                .Select(static candidate => new PackageAssetEntry(
                    candidate.EntryPath,
                    candidate.FileName,
                    candidate.RuntimeIdentifier)),
        ];
        // Every satellite excluded above has its primary in the same folder, so
        // a folder with candidates always keeps at least one asset.
        return new PackageAssetSelection.Selected(
            new PackageAssetUniverse(
                selectedFramework,
                runtimeIdentifier,
                assets));
    }

    static CandidateEntry? Classify(
        string entryPath,
        string? runtimeIdentifier)
    {
        string[] segments = entryPath.Split('/');

        // lib/{tfm}/{relative-path}
        if (segments.Length >= 3
            && segments[0].Equals("lib", StringComparison.OrdinalIgnoreCase))
        {
            return new CandidateEntry(
                entryPath,
                segments[1].ToLowerInvariant(),
                string.Join('/', segments[2..]),
                segments[^1],
                RuntimeIdentifier: null);
        }

        // runtimes/{rid}/lib/{tfm}/{relative-path}
        if (runtimeIdentifier is not null
            && segments.Length >= 5
            && segments[0].Equals(
                "runtimes",
                StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals(
                runtimeIdentifier,
                StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("lib", StringComparison.OrdinalIgnoreCase))
        {
            return new CandidateEntry(
                entryPath,
                segments[3].ToLowerInvariant(),
                string.Join('/', segments[4..]),
                segments[^1],
                runtimeIdentifier);
        }

        return null;
    }

    static bool IsSatelliteAsset(
        CandidateEntry candidate,
        HashSet<string> assetPaths)
    {
        if (!candidate.FileName.EndsWith(
                SatelliteSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = candidate.RelativePath.Split('/');
        if (segments.Length < 2
            || !TfmResolver.IsCultureFolderName(segments[^2]))
        {
            return false;
        }

        string primary =
            candidate.FileName[..^SatelliteSuffix.Length] + AssemblyExtension;
        string primaryRelativePath = string.Join(
            '/',
            [.. segments[..^2], primary]);
        return assetPaths.Contains(primaryRelativePath);
    }

    static bool IsSafeEntryPath(string entryPath) =>
        !entryPath.Contains('\\', StringComparison.Ordinal)
        && entryPath.Split('/').All(StorePath.IsSafeSegment);

    static bool IsBlankOrPadded(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || !string.Equals(value, value.Trim(), StringComparison.Ordinal);

    sealed record CandidateEntry(
        string EntryPath,
        string TargetFramework,
        string RelativePath,
        string FileName,
        string? RuntimeIdentifier);
}
