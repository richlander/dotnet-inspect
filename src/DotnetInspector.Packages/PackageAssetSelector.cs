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
/// <c>runtimes/{rid}/lib/{tfm}</c>. A requested runtime identifier matches its
/// folder exactly and ordinally: runtime identifiers are canonically lowercase,
/// so a differently-cased folder is a different name rather than a spelling of
/// the requested one.
/// </para>
/// <para>
/// Framework applicability and ordering come from the shared
/// <see cref="TfmResolver"/> rules the rest of the product uses, applied to the
/// <em>base</em> framework of each folder, plus an explicit platform rule those
/// rules do not express:
/// </para>
/// <list type="bullet">
/// <item>
/// a neutral target (<c>net10.0</c>) admits only neutral candidates, so a
/// platform-specific asset is never selected for a target that never asked for
/// that platform;
/// </item>
/// <item>
/// a platform target (<c>net10.0-windows</c>) admits a neutral candidate as a
/// fallback and a candidate whose platform token it matches exactly;
/// </item>
/// <item>
/// at the same base framework the exact platform match outranks the neutral
/// fallback, so the more specific universe wins rather than tying with it; and
/// </item>
/// <item>
/// every other platform, including a platform version spelled differently from
/// the target's, is inapplicable. That is deliberately conservative: the
/// product owns no platform-version reduction table, and inventing one risks
/// selecting bytes built for another platform version. The visible cost is a
/// <see cref="PackageAssetSelection.NoMatch"/> where full NuGet reduction would
/// have accepted a lower platform version.
/// </item>
/// </list>
/// <para>
/// The highest-priority applicable candidate wins: base framework first,
/// platform specificity second. Two distinct folders of equal rank are an
/// ambiguity, not a coin flip.
/// </para>
/// <para>
/// Exactly one framework is selected for the whole universe. Runtime-specific
/// assets replace runtime-neutral assets with the same relative path under the
/// selected framework; there is no per-asset framework fallback and no
/// runtime-identifier fallback graph, so an exact runtime identifier selects
/// its own folder or nothing, while the runtime-neutral assets it does not
/// replace stay in the universe.
/// </para>
/// <para>
/// No package-controlled text reaches a failure message. A folder name, entry
/// path, or archive-derived framework is described, never quoted, and the only
/// framework a message names is the one the caller asked for.
/// </para>
/// <para>
/// Gated by <c>PackageAssetSelectorTests</c>:
/// <c>Select_TakesHighestApplicableFrameworkFolder</c> and
/// <c>Select_RejectsAnIncompatibleFrameworkFamily</c> for framework selection;
/// <c>Select_NeutralTargetRejectsAPlatformSpecificFolder</c>,
/// <c>Select_NeutralTargetPrefersTheNeutralFolder</c>,
/// <c>Select_PlatformTargetPrefersTheExactPlatformFolder</c>,
/// <c>Select_PlatformTargetRejectsAnotherPlatform</c>,
/// <c>Select_PlatformTargetSelectsTheExactVersionedPlatformFolder</c>, and
/// <c>Select_PlatformTargetRejectsADifferentPlatformVersionSpelling</c> for the
/// platform rule;
/// <c>Select_PrefersTheRuntimeSpecificAssetForTheRequestedRid</c>,
/// <c>Select_IgnoresRuntimeAssetsForAnotherRid</c>, and
/// <c>Select_IgnoresARuntimeFolderMatchingTheRidOnlyByCase</c> for the
/// runtime-identifier rule;
/// <c>Select_ReportsCaseCollidingNeutralAssetsAsAmbiguous</c> for ambiguity;
/// <c>Select_AmbiguityNamesOnlyTheRequestedFramework</c> and
/// <c>Select_KeepsABidiBearingEntryOutOfTheFailureMessage</c> for the message
/// rule; and <c>Select_RejectsAnUnsafeCandidateEntryPath</c> with
/// <c>Select_RejectsAControlBearingCandidateEntryPath</c> for entry-path
/// containment.
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

        FrameworkFolder target = FrameworkFolder.Parse(targetFramework);
        List<string> applicable =
        [
            .. candidates
                .Select(static candidate => candidate.TargetFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(tfm => IsApplicable(FrameworkFolder.Parse(tfm), target)),
        ];
        if (applicable.Count == 0)
        {
            return new PackageAssetSelection.NoMatch(
                $"The package carries no assembly assets applicable to '{targetFramework}'.");
        }

        (int BasePriority, int PlatformRank) bestRank = applicable.Max(Rank);
        List<string> best =
        [
            .. applicable
                .Where(tfm => Rank(tfm) == bestRank)
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
            // select the bytes. The selected folder name is archive-controlled
            // text, so the message names the framework the caller asked for.
            return new PackageAssetSelection.Ambiguous(
                $"More than one assembly asset has the same identity in the universe selected for '{targetFramework}'.");
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

        static (int BasePriority, int PlatformRank) Rank(string tfm)
        {
            FrameworkFolder folder = FrameworkFolder.Parse(tfm);
            return (
                TfmResolver.GetTfmPriority(folder.BaseFramework),
                folder.Platform is null ? 0 : 1);
        }
    }

    /// <summary>
    /// True when a candidate asset folder may serve <paramref name="target"/>:
    /// its base framework is a recognized, compatible framework no newer than
    /// the target's, and its platform is either absent or exactly the target's.
    /// </summary>
    static bool IsApplicable(FrameworkFolder candidate, FrameworkFolder target)
    {
        int candidatePriority =
            TfmResolver.GetTfmPriority(candidate.BaseFramework);
        int targetPriority = TfmResolver.GetTfmPriority(target.BaseFramework);
        if (candidatePriority <= 0
            || targetPriority <= 0
            || candidatePriority > targetPriority
            || !TfmResolver.IsTfmCompatible(
                candidate.BaseFramework,
                target.BaseFramework))
        {
            return false;
        }

        if (candidate.Platform is not { } candidatePlatform)
            return true;

        return target.Platform is { } targetPlatform
            && string.Equals(
                candidatePlatform,
                targetPlatform,
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A framework folder name split into its base framework and its optional
    /// platform token, which carries the platform's version spelling exactly as
    /// written.
    /// </summary>
    /// <remarks>
    /// The split is textual on the first <c>-</c>, so a name that is not a
    /// platform-qualified framework at all — a legacy profile such as
    /// <c>net40-client</c> — parses as a platform token and is admitted only by
    /// a target that spells it identically. That is the conservative direction:
    /// an unrecognized qualifier never becomes an unqualified match.
    /// </remarks>
    readonly record struct FrameworkFolder(
        string BaseFramework,
        string? Platform)
    {
        internal static FrameworkFolder Parse(string name)
        {
            int separator = name.IndexOf('-', StringComparison.Ordinal);
            return separator < 0
                ? new FrameworkFolder(name, null)
                : new FrameworkFolder(
                    name[..separator],
                    name[(separator + 1)..]);
        }
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
            && string.Equals(
                segments[1],
                runtimeIdentifier,
                StringComparison.Ordinal)
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

    /// <summary>
    /// True when a candidate entry path can address content safely: no
    /// backslash, no unsafe segment, and no control character.
    /// </summary>
    /// <remarks>
    /// Control characters are refused here rather than downstream because an
    /// entry path is package-controlled text that reaches a filesystem-backed
    /// store, a log line, and a descriptor. Refusing them keeps the one shape
    /// that could smuggle terminal control sequences out of every consumer at
    /// once, and the offending value is described rather than echoed.
    /// </remarks>
    static bool IsSafeEntryPath(string entryPath) =>
        !entryPath.Contains('\\', StringComparison.Ordinal)
        && !entryPath.Any(char.IsControl)
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
