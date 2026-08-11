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
    IReadOnlyList<PackageCompileAsset> CandidateAssets)
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

    /// <summary>
    /// Every implementation assembly in the selected target framework. This remains distinct
    /// from <see cref="Assets"/>, which prefers reference assemblies for API inspection.
    /// </summary>
    public IReadOnlyList<PackageCompileAsset> ImplementationAssets =>
        FrameworkAssets
            .Where(asset => asset.Kind == PackageCompileAssetKind.Library)
            .ToArray();

    /// <summary>Resolves an opaque asset identity within this selected set.</summary>
    public PackageCompileAsset? FindAsset(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Assets.FirstOrDefault(
            asset => asset.Id.Equals(id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the implementation counterpart of one selected compile asset. A library asset is
    /// its own counterpart; a reference asset matches by framework and assembly name.
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

        return compileAsset.Kind == PackageCompileAssetKind.Library
            ? compileAsset
            : ImplementationAssets.FirstOrDefault(asset =>
                asset.AssemblyName.Equals(
                    compileAsset.AssemblyName,
                    StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Discovers and selects package compile assets without requiring a filesystem. This is the
/// shared owner for desktop package selection and in-memory Browser/Wasm package selection.
/// </summary>
public static class PackageCompileAssetSelector
{
    const string AssetIdPrefix = "compile:";

    public static PackageCompileAssetSelection Select(
        IPackageContent content,
        string packageId,
        string? targetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        PackageCompileAsset[] discovered =
        [
            .. content.EnumerateEntries()
                .Select(Parse)
                .OfType<PackageCompileAsset>()
                .GroupBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(asset => asset.Path, StringComparer.Ordinal)
                    .First())
                .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.Ordinal),
        ];
        if (discovered.Length == 0)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.NoCompileAssets,
                null,
                [],
                [],
                null,
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
        string? selectedFramework = string.IsNullOrWhiteSpace(targetFramework)
            ? frameworks[0]
            : frameworks.FirstOrDefault(
                framework => framework.Equals(
                    targetFramework,
                    StringComparison.OrdinalIgnoreCase));
        if (selectedFramework is null)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
                targetFramework,
                frameworks,
                [],
                null,
                discovered);
        }

        PackageCompileAsset[] frameworkAssets =
        [
            .. discovered.Where(
                asset => asset.TargetFramework.Equals(
                    selectedFramework,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        PackageCompileAssetKind preferredKind = frameworkAssets.Any(
            asset => asset.Kind == PackageCompileAssetKind.Reference)
                ? PackageCompileAssetKind.Reference
                : PackageCompileAssetKind.Library;
        PackageCompileAsset[] selected =
        [
            .. frameworkAssets
                .Where(asset => asset.Kind == preferredKind)
                .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.Ordinal),
        ];

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
            discovered);
    }

    static PackageCompileAsset? Parse(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return null;

        if (entry.Contains('\\'))
            return null;

        string path = entry;
        string[] parts = path.Split('/');
        if (parts.Length != 3
            || parts.Any(part => part is "." or "..")
            || !parts[2].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
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
}
