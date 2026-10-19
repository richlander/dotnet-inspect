namespace DotnetInspector.Packages;

/// <summary>
/// One compile or runtime implementation population selected for Package
/// Library navigation.
/// </summary>
public abstract record PackageLibraryPopulationSelection
{
    private protected PackageLibraryPopulationSelection(
        PackageCompileAssetSelectionReceipt compileReceipt)
    {
        ArgumentNullException.ThrowIfNull(compileReceipt);
        CompileReceipt = compileReceipt;
    }

    public PackageCompileAssetSelectionReceipt CompileReceipt { get; }

    public sealed record Compile :
        PackageLibraryPopulationSelection
    {
        internal Compile(
            PackageCompileAssetSelectionReceipt compileReceipt)
            : base(compileReceipt)
        {
        }
    }

    public sealed record Runtime :
        PackageLibraryPopulationSelection
    {
        internal Runtime(
            PackageCompileAssetSelectionReceipt compileReceipt,
            PackageAssetSelectionReceipt runtimeReceipt)
            : base(compileReceipt)
        {
            ArgumentNullException.ThrowIfNull(runtimeReceipt);
            RuntimeReceipt = runtimeReceipt;
        }

        public PackageAssetSelectionReceipt RuntimeReceipt { get; }
    }

    public sealed record NonSuccess :
        PackageLibraryPopulationSelection
    {
        internal NonSuccess(
            PackageCompileAssetSelectionReceipt compileReceipt,
            PackageAssetSelectionReceipt? runtimeReceipt)
            : base(compileReceipt) =>
            RuntimeReceipt = runtimeReceipt;

        public PackageAssetSelectionReceipt? RuntimeReceipt { get; }
    }
}

/// <summary>
/// Selects the one role population in which Package Library navigation runs.
/// </summary>
public static class PackageLibraryPopulationSelector
{
    public static PackageLibraryPopulationSelection Evaluate(
        IPackageContent content,
        string packageId,
        PackageCompileAssetSelectionPolicy policy,
        string? targetFramework = null,
        string? runtimeIdentifier = null,
        bool includesNestedCompileAssets = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        PackageCompileAssetSelectionReceipt compileReceipt =
            PackageCompileAssetSelector.Evaluate(
                content,
                packageId,
                policy,
                targetFramework,
                runtimeIdentifier,
                includesNestedCompileAssets);
        return Evaluate(content, compileReceipt);
    }

    public static PackageLibraryPopulationSelection Evaluate(
        IPackageContent content,
        PackageCompileAssetSelectionReceipt compileReceipt)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(compileReceipt);
        if (!ReferenceEquals(
                content.GenerationIdentity,
                compileReceipt.Generation))
        {
            throw new ArgumentException(
                "The compile selection receipt must describe the supplied package-content generation.",
                nameof(compileReceipt));
        }

        PackageCompileAssetSelection compileSelection =
            compileReceipt.Selection;
        if (compileSelection.Status
            == PackageCompileAssetSelectionStatus.Selected)
        {
            return new PackageLibraryPopulationSelection.Compile(
                compileReceipt);
        }

        if (compileSelection.Status
            is not (
                PackageCompileAssetSelectionStatus.NoCompileAssets
                or PackageCompileAssetSelectionStatus.EmptyCompileGroup))
        {
            return new PackageLibraryPopulationSelection.NonSuccess(
                compileReceipt,
                runtimeReceipt: null);
        }

        PackageAssetSelectionReceipt runtimeReceipt =
            PackageAssetSelector.EvaluateSingleRuntime(
                content,
                compileReceipt.RequestedTargetFramework,
                compileReceipt.RequestedRuntimeIdentifier);
        return runtimeReceipt.Selection
            is PackageAssetSelection.Selected
            ? new PackageLibraryPopulationSelection.Runtime(
                compileReceipt,
                runtimeReceipt)
            : new PackageLibraryPopulationSelection.NonSuccess(
                compileReceipt,
                runtimeReceipt);
    }
}
