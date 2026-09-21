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
    PackageCompileAssetKind Kind,
    string? RuntimeIdentifier = null);

/// <summary>One available compile slice and its complete candidate inventory.</summary>
public sealed record PackageCompileAssetSlice(
    string TargetFramework,
    IReadOnlyList<PackageCompileAsset> CandidateAssets,
    bool HasExplicitEmptyReferenceGroup);

/// <summary>The package-local policy that produced one compile selection.</summary>
public enum PackageCompileAssetSelectionPolicy
{
    /// <summary>Select the highest available package compile slice.</summary>
    HighestAvailable,

    /// <summary>
    /// Select the highest package compile slice applicable to an explicit target.
    /// </summary>
    ExplicitTarget,

    /// <summary>
    /// Select only a package compile slice that exactly matches the target.
    /// </summary>
    ExactTarget,
}

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
/// A deterministic package compile-asset selection. <see cref="Assets"/>
/// contains only one target framework and one preferred root;
/// <see cref="CandidateAssets"/> retains the complete discovered set on every
/// outcome.
/// </summary>
public sealed record PackageCompileAssetSelection(
    PackageCompileAssetSelectionStatus Status,
    string? TargetFramework,
    IReadOnlyList<string> AvailableTargetFrameworks,
    IReadOnlyList<PackageCompileAsset> Assets,
    PackageCompileAsset? DefaultAsset,
    IReadOnlyList<PackageCompileAsset> CandidateAssets,
    IReadOnlyList<PackageCompileAsset> ImplementationAssets,
    IReadOnlyList<string> ExplicitEmptyTargetFrameworks,
    IReadOnlyList<PackageCompileAssetSlice> AvailableSlices,
    string? Message = null)
{
    public bool IsSelected =>
        Status == PackageCompileAssetSelectionStatus.Selected
        && Assets.Count > 0
        && DefaultAsset is not null;

    /// <summary>
    /// The implementation universe selected independently from the compile
    /// slice, or <see langword="null"/> when no unique universe was selected.
    /// </summary>
    public string? ImplementationTargetFramework { get; init; }

    /// <summary>
    /// Whether implementation selection used compatibility relative to the
    /// requested compile target, including a compatible ambiguous outcome.
    /// </summary>
    public bool UsesCompatibleImplementationSelection { get; init; }

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
        PackageCompileAssetSelectionPolicy policy,
        string? requestedTargetFramework,
        string? requestedRuntimeIdentifier,
        PackageCompileAssetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(selection);
        Generation = generation;
        PackageId = packageId;
        Policy = policy;
        RequestedTargetFramework = requestedTargetFramework;
        RequestedRuntimeIdentifier = requestedRuntimeIdentifier;
        Selection = selection;
    }

    public PackageContentGenerationIdentity Generation { get; }

    public string PackageId { get; }

    public PackageCompileAssetSelectionPolicy Policy { get; }

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
        SelectCore(
            content,
            packageId,
            targetFramework,
            runtimeIdentifier,
            includeNestedCompileAssets: targetFramework is null,
            allowCompatibleFallback: false);

    /// <summary>
    /// Selects compile roles for an already-selected compatible implementation
    /// universe while independently reducing the compile slice against the
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
            requestedTargetFramework,
            runtimeIdentifier,
            implementationTargetFramework,
            includeNestedCompileAssets: false,
            allowCompatibleFallback: true);
    }

    /// <summary>
    /// Selects compile assets and retains the exact invocation correspondence
    /// without retaining package content.
    /// </summary>
    public static PackageCompileAssetSelectionReceipt Evaluate(
        IPackageContent content,
        string packageId,
        string? targetFramework = null,
        string? runtimeIdentifier = null) =>
        Evaluate(
            content,
            packageId,
            targetFramework is null
                ? PackageCompileAssetSelectionPolicy.HighestAvailable
                : PackageCompileAssetSelectionPolicy.ExactTarget,
            targetFramework,
            runtimeIdentifier);

    /// <summary>
    /// Applies one explicit package-local selection policy and retains its exact
    /// invocation correspondence without retaining package content.
    /// </summary>
    public static PackageCompileAssetSelectionReceipt Evaluate(
        IPackageContent content,
        string packageId,
        PackageCompileAssetSelectionPolicy policy,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy), policy, null);

        if (policy == PackageCompileAssetSelectionPolicy.HighestAvailable
            && targetFramework is not null)
        {
            throw new ArgumentException(
                "Highest-available compile selection does not accept an explicit target framework.",
                nameof(targetFramework));
        }
        if (policy is PackageCompileAssetSelectionPolicy.ExplicitTarget
                or PackageCompileAssetSelectionPolicy.ExactTarget
            && targetFramework is null)
        {
            throw new ArgumentException(
                "Explicit compile selection requires a target framework.",
                nameof(targetFramework));
        }

        return new PackageCompileAssetSelectionReceipt(
            content.GenerationIdentity,
            packageId,
            policy,
            targetFramework,
            runtimeIdentifier,
            SelectCore(
                content,
                packageId,
                targetFramework,
                runtimeIdentifier,
                includeNestedCompileAssets:
                    policy != PackageCompileAssetSelectionPolicy.ExactTarget,
                allowCompatibleFallback:
                    policy == PackageCompileAssetSelectionPolicy.ExplicitTarget));
    }

    /// <summary>
    /// Retains correspondence for an already-issued compile selection over the
    /// same package content generation.
    /// </summary>
    public static PackageCompileAssetSelectionReceipt RetainSelection(
        IPackageContent content,
        string packageId,
        PackageCompileAssetSelectionPolicy policy,
        PackageCompileAssetSelection selection,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy), policy, null);
        if (policy == PackageCompileAssetSelectionPolicy.HighestAvailable
            && targetFramework is not null)
        {
            throw new ArgumentException(
                "Highest-available compile selection does not accept an explicit target framework.",
                nameof(targetFramework));
        }
        if (policy is PackageCompileAssetSelectionPolicy.ExplicitTarget
                or PackageCompileAssetSelectionPolicy.ExactTarget
            && targetFramework is null)
        {
            throw new ArgumentException(
                "Explicit compile selection requires a target framework.",
                nameof(targetFramework));
        }

        return new(
            content.GenerationIdentity,
            packageId,
            policy,
            targetFramework,
            runtimeIdentifier,
            selection);
    }

    private static PackageCompileAssetSelection SelectCore(
        IPackageContent content,
        string packageId,
        string? targetFramework = null,
        string? runtimeIdentifier = null,
        string? implementationTargetFramework = null,
        bool includeNestedCompileAssets = false,
        bool allowCompatibleFallback = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        string[] entries = [.. content.EnumerateEntries()];
        PackageCompileAsset[] discovered =
        [
            .. entries
                .Select(Parse)
                .OfType<PackageCompileAsset>()
                .Where(asset => !IsSatelliteAsset(asset))
                .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.Ordinal),
        ];
        string[] emptyReferenceGroups =
        [
            .. entries
                .Select(ParseEmptyReferenceGroup)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(framework =>
                    TfmResolver.GetTfmPriority(framework.ToLowerInvariant()))
                .ThenBy(framework => framework, StringComparer.OrdinalIgnoreCase)
                .ThenBy(framework => framework, StringComparer.Ordinal),
        ];
        string[] frameworks =
        [
            .. discovered
                .Select(asset => asset.TargetFramework)
                .Concat(emptyReferenceGroups)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(framework =>
                    TfmResolver.GetTfmPriority(framework.ToLowerInvariant()))
                .ThenBy(framework => framework, StringComparer.OrdinalIgnoreCase)
                .ThenBy(framework => framework, StringComparer.Ordinal),
        ];
        if (frameworks.Length == 0)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.NoCompileAssets,
                null,
                [],
                [],
                null,
                [],
                [],
                [],
                []);
        }

        PackageCompileAssetSlice[] slices =
        [
            .. frameworks.Select(framework =>
                new PackageCompileAssetSlice(
                    framework,
                    discovered
                        .Where(asset => asset.TargetFramework.Equals(
                            framework,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray(),
                    emptyReferenceGroups.Contains(
                        framework,
                        StringComparer.OrdinalIgnoreCase))),
        ];
        HashSet<string> selectableFrameworks =
            new(StringComparer.OrdinalIgnoreCase);
        selectableFrameworks.UnionWith(
            discovered
                .Where(asset =>
                    includeNestedCompileAssets
                    || IsDirectCompileAsset(asset))
                .Select(asset => asset.TargetFramework)
                .Concat(emptyReferenceGroups));
        string[] selectionFrameworks =
        [
            .. frameworks.Where(selectableFrameworks.Contains),
        ];
        if (selectionFrameworks.Length == 0)
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.NoCompileAssets,
                null,
                frameworks,
                [],
                null,
                discovered,
                [],
                emptyReferenceGroups,
                slices);
        }

        string? selectedFramework;
        if (targetFramework is null)
        {
            selectedFramework = selectionFrameworks[0];
        }
        else
        {
            selectedFramework = allowCompatibleFallback
                ? SelectApplicableFramework(
                    selectionFrameworks,
                    targetFramework)
                : selectionFrameworks.FirstOrDefault(framework =>
                    framework.Equals(
                        targetFramework,
                        StringComparison.OrdinalIgnoreCase));
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
                [],
                emptyReferenceGroups,
                slices);
        }

        PackageCompileAsset[] frameworkAssets =
        [
            .. discovered.Where(
                asset =>
                    (includeNestedCompileAssets
                        || IsDirectCompileAsset(asset))
                    && asset.TargetFramework.Equals(
                        selectedFramework,
                        StringComparison.OrdinalIgnoreCase)),
        ];
        PackageCompileAsset[] referenceAssets =
        [
            .. discovered.Where(
                asset => (includeNestedCompileAssets
                        || IsDirectCompileAsset(asset))
                    && asset.Kind == PackageCompileAssetKind.Reference
                    && asset.TargetFramework.Equals(
                        selectedFramework,
                        StringComparison.OrdinalIgnoreCase)),
        ];
        PackageAssetSelection implementationSelection =
            PackageAssetSelector.Select(
                content,
                implementationTargetFramework
                    ?? targetFramework
                    ?? selectedFramework,
                runtimeIdentifier);
        string? selectedImplementationTargetFramework =
            implementationSelection
                is PackageAssetSelection.Selected selectedImplementation
                ? selectedImplementation.Universe.TargetFramework
                : null;
        bool usesCompatibleImplementationSelection =
            selectedImplementationTargetFramework is not null
                ? targetFramework is not null
                    && !selectedImplementationTargetFramework.Equals(
                        targetFramework,
                        StringComparison.OrdinalIgnoreCase)
                : (implementationTargetFramework is not null
                        && targetFramework is not null
                        && !implementationTargetFramework.Equals(
                            targetFramework,
                            StringComparison.OrdinalIgnoreCase))
                    || implementationSelection.UsesCompatibleTargetSelection
                    || (allowCompatibleFallback
                        && targetFramework is not null
                        && !selectedFramework.Equals(
                            targetFramework,
                            StringComparison.OrdinalIgnoreCase));
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
                emptyReferenceGroups,
                slices,
                ambiguous.Message)
            {
                ImplementationTargetFramework =
                    selectedImplementationTargetFramework,
                UsesCompatibleImplementationSelection =
                    usesCompatibleImplementationSelection,
            };
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
                emptyReferenceGroups,
                slices,
                invalid.Message)
            {
                ImplementationTargetFramework =
                    selectedImplementationTargetFramework,
                UsesCompatibleImplementationSelection =
                    usesCompatibleImplementationSelection,
            };
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
                            PackageCompileAssetKind.Library,
                            asset.RuntimeIdentifier)),
                ]
                : [];

        // An explicit empty compile group applies to its exact selected slice.
        // A marker in another compatible slice cannot suppress the selected
        // slice's compile assets.
        if (referenceAssets.Length == 0
            && emptyReferenceGroups.Contains(
                selectedFramework,
                StringComparer.OrdinalIgnoreCase))
        {
            return new PackageCompileAssetSelection(
                PackageCompileAssetSelectionStatus.EmptyCompileGroup,
                selectedFramework,
                frameworks,
                [],
                null,
                discovered,
                implementationAssets,
                emptyReferenceGroups,
                slices)
            {
                ImplementationTargetFramework =
                    selectedImplementationTargetFramework,
                UsesCompatibleImplementationSelection =
                    usesCompatibleImplementationSelection,
            };
        }

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
            .. (referenceAssets.Length > 0
                    ? referenceAssets
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
                implementationAssets,
                emptyReferenceGroups,
                slices)
            {
                ImplementationTargetFramework =
                    selectedImplementationTargetFramework,
                UsesCompatibleImplementationSelection =
                    usesCompatibleImplementationSelection,
            };
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
            implementationAssets,
            emptyReferenceGroups,
            slices)
        {
            ImplementationTargetFramework =
                selectedImplementationTargetFramework,
            UsesCompatibleImplementationSelection =
                usesCompatibleImplementationSelection,
        };
    }

    static PackageCompileAsset? Parse(string entry)
    {
        if (!TryParsePathParts(entry, out string[]? parts))
            return null;

        string path = entry;
        if (!parts![^1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(parts[^1]))
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
                parts[^1],
                parts[1],
                kind.Value);
    }

    static bool IsSatelliteAsset(PackageCompileAsset asset)
    {
        if (!asset.AssemblyName.EndsWith(
                ".resources.dll",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = asset.Path.Split('/');
        return segments.Length >= 4
            && TfmResolver.IsCultureFolderName(segments[^2]);
    }

    static bool IsDirectCompileAsset(PackageCompileAsset asset) =>
        asset.Path.Count(character => character == '/') == 2;

    /// <summary>
    /// The target framework of an explicit empty reference group (<c>ref/&lt;tfm&gt;/_._</c>), or
    /// null for any other entry. Only the exact marker name counts; a file that merely resembles
    /// it is an ordinary entry.
    /// </summary>
    static string? ParseEmptyReferenceGroup(string entry)
    {
        if (!TryParsePathParts(entry, out string[]? parts))
            return null;

        return parts!.Length == 3
            && parts[0].Equals("ref", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals(EmptyGroupMarker, StringComparison.Ordinal)
            && TfmResolver.IsTfmLike(parts[1])
                ? parts[1]
                : null;
    }

    internal static string? SelectApplicableFramework(
        IReadOnlyList<string> frameworks,
        string requestedFramework)
    {
        string? exact = frameworks.FirstOrDefault(framework =>
            framework.Equals(
                requestedFramework,
                StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        if (!TrySplitFramework(
                requestedFramework,
                out string? requestedBase,
                out string? requestedPlatform))
        {
            return null;
        }

        return frameworks
            .Select(framework =>
            {
                bool parsed = TrySplitFramework(
                    framework,
                    out string? candidateBase,
                    out string? candidatePlatform);
                bool platformApplicable = candidatePlatform is null
                    || requestedPlatform is not null
                        && candidatePlatform.Equals(
                            requestedPlatform,
                            StringComparison.OrdinalIgnoreCase);
                return new
                {
                    Framework = framework,
                    Base = candidateBase,
                    PlatformRank = candidatePlatform is null ? 0 : 1,
                    IsApplicable = parsed
                        && platformApplicable
                        && TfmResolver.IsFrameworkCompatible(
                            candidateBase!,
                            requestedBase!),
                };
            })
            .Where(candidate => candidate.IsApplicable)
            .OrderByDescending(candidate =>
                TfmResolver.GetFrameworkFallbackRank(
                    candidate.Base!,
                    requestedBase!))
            .ThenByDescending(candidate =>
                TfmResolver.GetTfmPriority(
                    candidate.Base!.ToLowerInvariant()))
            .ThenByDescending(candidate => candidate.PlatformRank)
            .ThenBy(
                candidate => candidate.Framework,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Framework, StringComparer.Ordinal)
            .Select(candidate => candidate.Framework)
            .FirstOrDefault();
    }

    static bool TrySplitFramework(
        string framework,
        out string? baseFramework,
        out string? platform)
    {
        baseFramework = null;
        platform = null;
        if (!TfmResolver.TryGetBaseFrameworkIdentity(framework, out _))
            return false;

        int separator = framework.IndexOf('-', StringComparison.Ordinal);
        baseFramework = separator < 0 ? framework : framework[..separator];
        platform = separator < 0 ? null : framework[(separator + 1)..];
        return true;
    }

    /// <summary>
    /// The <c>&lt;root&gt;/&lt;tfm&gt;/&lt;relative-path&gt;</c> segments of a
    /// package entry, or false for an entry that is not shaped like one —
    /// including traversal-shaped and backslash-separated spellings.
    /// </summary>
    internal static bool TryParsePathParts(
        string entry,
        out string[]? parts)
    {
        parts = null;
        if (string.IsNullOrWhiteSpace(entry) || entry.Contains('\\'))
            return false;

        string[] candidate = entry.Split('/');
        if (candidate.Length < 3
            || candidate.Any(part =>
                string.IsNullOrEmpty(part)
                || part is "." or ".."))
            return false;

        parts = candidate;
        return true;
    }
}
