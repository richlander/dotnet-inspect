using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>The package layout that supplied a compile assembly.</summary>
public enum PackageCompileAssetKind
{
    Reference,
    Library,
}

/// <summary>One selected compile assembly with product-owned identity and provenance.</summary>
public sealed record PackageCompileAsset(
    string Id,
    string Path,
    string AssemblyName,
    string TargetFramework,
    PackageCompileAssetKind Kind);

/// <summary>The outcome of selecting one package's compile-assembly set.</summary>
public enum PackageCompileAssetSelectionStatus
{
    Selected,
    NoCompileAssets,
    NoMatchingTargetFramework,

    /// <summary>
    /// The package declares an explicit empty compile group (<c>ref/&lt;tfm&gt;/_._</c>) that
    /// covers the selected target framework. NuGet reads that marker as "this package
    /// deliberately contributes no compile-time assembly here", so the <c>lib/</c> assets are not
    /// a fallback for it.
    /// </summary>
    EmptyCompileGroup,

    /// <summary>The shared implementation-asset selector rejected the package layout.</summary>
    InvalidImplementationAssets,
}

/// <summary>
/// A deterministic package compile-asset selection. <see cref="Assets"/> contains only one
/// target framework and one preferred root; <see cref="CandidateAssets"/> retains the complete
/// discovered set when selection fails.
/// </summary>
public sealed record PackageCompileAssetSelection(
    PackageCompileAssetSelectionStatus Status,
    string? TargetFramework,
    IReadOnlyList<string> AvailableTargetFrameworks,
    IReadOnlyList<PackageCompileAsset> Assets,
    PackageCompileAsset? DefaultAsset,
    IReadOnlyList<PackageCompileAsset> CandidateAssets,
    IReadOnlyList<PackageCompileAsset> ImplementationAssets,
    string? Message = null)
{
    public bool IsSelected =>
        Status == PackageCompileAssetSelectionStatus.Selected
        && Assets.Count > 0
        && DefaultAsset is not null;

    /// <summary>Every discovered compile asset in the selected target framework.</summary>
    public IReadOnlyList<PackageCompileAsset> FrameworkAssets =>
        TargetFramework is null
            ? []
            : CandidateAssets
                .Where(asset => asset.TargetFramework.Equals(
                    TargetFramework,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

    /// <summary>Resolves an opaque asset identity within this selected set.</summary>
    public PackageCompileAsset? FindAsset(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Assets.FirstOrDefault(
            asset => asset.Id.Equals(id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the implementation counterpart of one selected compile asset. An exact retained
    /// library asset is its own counterpart; otherwise correspondence uses the selector-owned
    /// relative path so a neutral compile asset maps only to its RID-specific replacement.
    /// </summary>
    public PackageCompileAsset? FindImplementationAsset(PackageCompileAsset compileAsset)
    {
        ArgumentNullException.ThrowIfNull(compileAsset);
        if (!Assets.Any(asset => asset.Id.Equals(compileAsset.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The compile asset is not part of this selected set.",
                nameof(compileAsset));
        }

        PackageCompileAsset? exact =
            ImplementationAssets.FirstOrDefault(asset =>
                asset.Id.Equals(compileAsset.Id, StringComparison.Ordinal));
        if (exact is not null)
            return exact;

        string? relativePath = TryGetRelativePath(compileAsset);
        return relativePath is null
            ? null
            : ImplementationAssets.FirstOrDefault(asset =>
                TryGetRelativePath(asset)?.Equals(
                    relativePath,
                    StringComparison.OrdinalIgnoreCase)
                is true);
    }

    static string? TryGetRelativePath(PackageCompileAsset asset)
    {
        string[] segments = asset.Path.Split('/');
        if (segments.Length >= 3
            && (segments[0].Equals("ref", StringComparison.OrdinalIgnoreCase)
                || segments[0].Equals("lib", StringComparison.OrdinalIgnoreCase))
            && segments[1].Equals(
                asset.TargetFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('/', segments[2..]);
        }

        return segments.Length >= 5
            && segments[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("lib", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals(
                asset.TargetFramework,
                StringComparison.OrdinalIgnoreCase)
                ? string.Join('/', segments[4..])
                : null;
    }
}

/// <summary>
/// Resource-free evidence binding one compile asset-selection outcome to the
/// content generation and exact request that produced it.
/// </summary>
public sealed class PackageCompileAssetSelectionReceipt
{
    internal PackageCompileAssetSelectionReceipt(
        PackageContentGenerationIdentity generation,
        string packageId,
        string? requestedTargetFramework,
        string? requestedRuntimeIdentifier,
        PackageCompileAssetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(selection);
        Generation = generation;
        PackageId = packageId;
        RequestedTargetFramework = requestedTargetFramework;
        RequestedRuntimeIdentifier = requestedRuntimeIdentifier;
        Selection = selection;
    }

    public PackageContentGenerationIdentity Generation { get; }

    public string PackageId { get; }

    public string? RequestedTargetFramework { get; }

    public string? RequestedRuntimeIdentifier { get; }

    public PackageCompileAssetSelection Selection { get; }
}

/// <summary>
/// Adds reference-assembly and explicit-empty-group semantics to the implementation universe
/// selected by <see cref="PackageAssetSelector"/>, without requiring a filesystem.
/// </summary>
public static class PackageCompileAssetSelector
{
    const string AssetIdPrefix = "compile:";

    /// <summary>NuGet's empty-group marker file name.</summary>
    const string EmptyGroupMarker = "_._";

    public static PackageCompileAssetSelection Select(
        IPackageContent content,
        string packageId,
        string? targetFramework = null,
        string? runtimeIdentifier = null) =>
        Evaluate(
            content,
            packageId,
            targetFramework,
            runtimeIdentifier).Selection;

    /// <summary>
    /// Selects compile roles for an already-selected compatible implementation
    /// universe while reducing explicit empty reference groups against the
    /// original requested framework.
    /// </summary>
    public static PackageCompileAssetSelection SelectForCompatibleImplementation(
        IPackageContent content,
        string packageId,
        string requestedTargetFramework,
        string implementationTargetFramework,
        string? runtimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedTargetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationTargetFramework);
        return SelectCore(
            content,
            packageId,
            implementationTargetFramework,
            runtimeIdentifier,
            requestedTargetFramework);
    }

    /// <summary>
    /// Selects compile assets and retains the exact invocation correspondence
    /// without retaining package content.
    /// </summary>
    public static PackageCompileAssetSelectionReceipt Evaluate(
        IPackageContent content,
        string packageId,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new PackageCompileAssetSelectionReceipt(
            content.GenerationIdentity,
            packageId,
            targetFramework,
            runtimeIdentifier,
            SelectCore(
                content,
                packageId,
                targetFramework,
                runtimeIdentifier));
    }

    private static PackageCompileAssetSelection SelectCore(
        IPackageContent content,
        string packageId,
        string? targetFramework = null,
        string? runtimeIdentifier = null,
        string? emptyGroupTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        string[] entries = [.. content.EnumerateEntries()];
        PackageCompileAsset[] discovered =
        [
            .. entries
                .Select(Parse)
                .OfType<PackageCompileAsset>()
                .GroupBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(asset => asset.Path, StringComparer.Ordinal)
                    .First())
                .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.Ordinal),
        ];
        string[] emptyReferenceGroups =
        [
            .. entries
                .Select(ParseEmptyReferenceGroup)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        if (discovered.Length == 0)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.NoCompileAssets,
                null,
                [],
                [],
                null,
                [],
                []);
        }

        string[] frameworks =
        [
            .. discovered
                .Select(asset => asset.TargetFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(framework =>
                    TfmResolver.GetTfmPriority(framework.ToLowerInvariant()))
                .ThenBy(framework => framework, StringComparer.OrdinalIgnoreCase),
        ];
        string? selectedFramework;
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            selectedFramework = frameworks[0];
        }
        else
        {
            selectedFramework = frameworks.FirstOrDefault(
                framework => framework.Equals(
                    targetFramework,
                    StringComparison.OrdinalIgnoreCase));
            if (selectedFramework is null
                && emptyReferenceGroups.Contains(
                    targetFramework,
                    StringComparer.OrdinalIgnoreCase))
            {
                selectedFramework = targetFramework;
            }
        }
        if (selectedFramework is null)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
                targetFramework,
                frameworks,
                [],
                null,
                discovered,
                []);
        }

        PackageCompileAsset[] frameworkAssets =
        [
            .. discovered.Where(
                asset => asset.TargetFramework.Equals(
                    selectedFramework,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        PackageAssetSelection implementationSelection =
            PackageAssetSelector.Select(
                content,
                selectedFramework,
                runtimeIdentifier);
        if (implementationSelection
            is PackageAssetSelection.Ambiguous ambiguous)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.InvalidImplementationAssets,
                selectedFramework,
                frameworks,
                [],
                null,
                discovered,
                [],
                ambiguous.Message);
        }
        if (implementationSelection is PackageAssetSelection.Invalid invalid)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.InvalidImplementationAssets,
                selectedFramework,
                frameworks,
                [],
                null,
                discovered,
                [],
                invalid.Message);
        }

        PackageCompileAsset[] implementationAssets =
            implementationSelection is PackageAssetSelection.Selected implementation
                ?
                [
                    .. implementation.Universe.Assets.Select(asset =>
                        new PackageCompileAsset(
                            AssetIdPrefix + asset.EntryPath,
                            asset.EntryPath,
                            asset.FileName,
                            implementation.Universe.TargetFramework,
                            PackageCompileAssetKind.Library)),
                ]
                : [];

        // An explicit empty compile group is a statement, not an absence: NuGet's nearest-group
        // rule picks the closest compatible ref group, and when that group is `_._` the package
        // contributes no compile-time assembly for the request. Falling back to lib/ there would
        // compile against assets the package deliberately withheld. Compatible implementation
        // selection still reduces empty groups against the original requested framework.
        if (!frameworkAssets.Any(asset => asset.Kind == PackageCompileAssetKind.Reference)
            && NearestCompatibleEmptyGroup(
                emptyReferenceGroups,
                emptyGroupTargetFramework ?? selectedFramework) is not null)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.EmptyCompileGroup,
                selectedFramework,
                frameworks,
                [],
                null,
                discovered,
                implementationAssets);
        }

        bool hasReferenceAssets = frameworkAssets.Any(
            asset => asset.Kind == PackageCompileAssetKind.Reference);
        PackageCompileAsset[] libraryFallback =
        [
            .. frameworkAssets
                .Where(asset => asset.Kind == PackageCompileAssetKind.Library)
                .Select(asset =>
                    implementationAssets.FirstOrDefault(candidate =>
                        candidate.Id.Equals(asset.Id, StringComparison.Ordinal))
                    ?? asset),
        ];
        PackageCompileAsset[] selected =
        [
            .. (hasReferenceAssets
                    ? frameworkAssets.Where(
                        asset => asset.Kind == PackageCompileAssetKind.Reference)
                    : libraryFallback)
                .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.Ordinal),
        ];
        if (selected.Length == 0)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.NoCompileAssets,
                selectedFramework,
                frameworks,
                [],
                null,
                discovered,
                implementationAssets);
        }

        PackageCompileAsset defaultAsset = selected.FirstOrDefault(
            asset => Path.GetFileNameWithoutExtension(asset.AssemblyName)
                .Equals(packageId, StringComparison.OrdinalIgnoreCase))
            ?? selected[0];
        return new PackageCompileAssetSelection(
            PackageCompileAssetSelectionStatus.Selected,
            selectedFramework,
            frameworks,
            selected,
            defaultAsset,
            discovered,
            implementationAssets);
    }

    static PackageCompileAsset? Parse(string entry)
    {
        if (!TryParsePathParts(entry, out string[]? parts))
            return null;

        string path = entry;
        if (!parts![2].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(parts[2]))
            || !TfmResolver.IsTfmLike(parts[1]))
        {
            return null;
        }

        PackageCompileAssetKind? kind =
            parts[0].Equals("ref", StringComparison.OrdinalIgnoreCase)
                ? PackageCompileAssetKind.Reference
                : parts[0].Equals("lib", StringComparison.OrdinalIgnoreCase)
                    ? PackageCompileAssetKind.Library
                    : null;
        return kind is null
            ? null
            : new PackageCompileAsset(
                AssetIdPrefix + path,
                path,
                parts[2],
                parts[1],
                kind.Value);
    }

    /// <summary>
    /// The target framework of an explicit empty reference group (<c>ref/&lt;tfm&gt;/_._</c>), or
    /// null for any other entry. Only the exact marker name counts; a file that merely resembles
    /// it is an ordinary entry.
    /// </summary>
    static string? ParseEmptyReferenceGroup(string entry)
    {
        if (!TryParsePathParts(entry, out string[]? parts))
            return null;

        return parts![0].Equals("ref", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals(EmptyGroupMarker, StringComparison.Ordinal)
            && TfmResolver.IsTfmLike(parts[1])
                ? parts[1]
                : null;
    }

    /// <summary>
    /// The nearest empty reference group that a consumer of <paramref name="selectedFramework"/>
    /// would bind to: family-compatible and no newer than the selected framework, highest
    /// priority first. An exact framework match is its own nearest group.
    /// </summary>
    static string? NearestCompatibleEmptyGroup(
        IReadOnlyList<string> emptyGroups,
        string selectedFramework)
    {
        int selectedPriority = TfmResolver.GetTfmPriority(selectedFramework.ToLowerInvariant());
        string? nearest = null;
        int nearestPriority = int.MinValue;
        foreach (string group in emptyGroups)
        {
            if (group.Equals(selectedFramework, StringComparison.OrdinalIgnoreCase))
                return group;

            int priority = TfmResolver.GetTfmPriority(group.ToLowerInvariant());
            if (!TfmResolver.IsTfmCompatible(group, selectedFramework)
                || priority > selectedPriority
                || priority <= nearestPriority)
            {
                continue;
            }

            nearest = group;
            nearestPriority = priority;
        }

        return nearest;
    }

    /// <summary>
    /// The three <c>&lt;root&gt;/&lt;tfm&gt;/&lt;file&gt;</c> segments of a package entry, or
    /// false for an entry that is not shaped like one — including traversal-shaped and
    /// backslash-separated spellings.
    /// </summary>
    static bool TryParsePathParts(string entry, out string[]? parts)
    {
        parts = null;
        if (string.IsNullOrWhiteSpace(entry) || entry.Contains('\\'))
            return false;

        string[] candidate = entry.Split('/');
        if (candidate.Length != 3 || candidate.Any(part => part is "." or ".."))
            return false;

        parts = candidate;
        return true;
    }
}
