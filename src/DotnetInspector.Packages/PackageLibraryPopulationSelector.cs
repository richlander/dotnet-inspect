using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One compile, runtime implementation, or declared tool population selected
/// for Package Library navigation.
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

    /// <summary>
    /// The selected package-relative role-population root.
    /// </summary>
    public string? PopulationPath { get; protected init; }

    public sealed record Compile :
        PackageLibraryPopulationSelection
    {
        internal Compile(
            PackageCompileAssetSelectionReceipt compileReceipt)
            : base(compileReceipt)
        {
            PackageCompileAsset asset =
                compileReceipt.Selection.Assets[0];
            PopulationPath = asset.PopulationPath;
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
            var selected =
                (PackageAssetSelection.Selected)runtimeReceipt.Selection;
            PopulationPath = selected.Universe.PopulationPath;
        }

        public PackageAssetSelectionReceipt RuntimeReceipt { get; }
    }

    public sealed record Tool :
        PackageLibraryPopulationSelection
    {
        internal Tool(
            PackageCompileAssetSelectionReceipt compileReceipt,
            PackageAssetSelectionReceipt runtimeReceipt,
            PackageToolSliceMeasurementEvidence toolEvidence,
            PackageToolSliceSelection toolSelection)
            : base(compileReceipt)
        {
            ArgumentNullException.ThrowIfNull(runtimeReceipt);
            ArgumentNullException.ThrowIfNull(toolEvidence);
            ArgumentNullException.ThrowIfNull(toolSelection);
            if (runtimeReceipt.Selection
                    is not PackageAssetSelection.NoMatch
                || toolSelection.Status
                    != PackageToolSliceSelectionStatus.Selected
                || toolSelection.SelectedEntries.IsEmpty)
            {
                throw new ArgumentException(
                    "A tool Library population requires runtime no-match and one non-empty selected tool slice.",
                    nameof(toolSelection));
            }

            RuntimeReceipt = runtimeReceipt;
            ToolEvidence = toolEvidence;
            ToolSelection = toolSelection;
            PopulationPath = toolSelection.PopulationPath;
        }

        public PackageAssetSelectionReceipt RuntimeReceipt { get; }

        public PackageToolSliceMeasurementEvidence ToolEvidence { get; }

        public PackageToolSliceSelection ToolSelection { get; }
    }

    public sealed record NonSuccess :
        PackageLibraryPopulationSelection
    {
        internal NonSuccess(
            PackageCompileAssetSelectionReceipt compileReceipt,
            PackageAssetSelectionReceipt? runtimeReceipt,
            PackageToolSliceMeasurementEvidence? toolEvidence = null,
            PackageToolSliceSelection? toolSelection = null,
            bool toolDeclarationUnavailable = false)
            : base(compileReceipt)
        {
            if ((toolEvidence is null) != (toolSelection is null))
            {
                throw new ArgumentException(
                    "Tool evidence and selection must be retained together.");
            }
            if (toolDeclarationUnavailable && toolEvidence is not null)
            {
                throw new ArgumentException(
                    "Unavailable tool declaration cannot retain tool evidence.");
            }

            RuntimeReceipt = runtimeReceipt;
            ToolEvidence = toolEvidence;
            ToolSelection = toolSelection;
            ToolDeclarationUnavailable = toolDeclarationUnavailable;
        }

        public PackageAssetSelectionReceipt? RuntimeReceipt { get; }

        public PackageToolSliceMeasurementEvidence? ToolEvidence { get; }

        public PackageToolSliceSelection? ToolSelection { get; }

        public bool ToolDeclarationUnavailable { get; }
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
        bool includesNestedCompileAssets = true,
        PackageSourceCoordinate? coordinate = null,
        ReadOnlyMemory<byte>? nuspecBytes = null,
        bool readManifestFromContent = true)
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
        return Evaluate(
            content,
            compileReceipt,
            coordinate,
            nuspecBytes,
            readManifestFromContent);
    }

    public static PackageLibraryPopulationSelection Evaluate(
        IPackageContent content,
        PackageCompileAssetSelectionReceipt compileReceipt,
        PackageSourceCoordinate? coordinate = null,
        ReadOnlyMemory<byte>? nuspecBytes = null,
        bool readManifestFromContent = true)
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
        if (runtimeReceipt.Selection
            is PackageAssetSelection.Selected)
        {
            return new PackageLibraryPopulationSelection.Runtime(
                compileReceipt,
                runtimeReceipt);
        }
        if (runtimeReceipt.Selection
            is not PackageAssetSelection.NoMatch)
        {
            return new PackageLibraryPopulationSelection.NonSuccess(
                compileReceipt,
                runtimeReceipt);
        }
        if (coordinate is null)
        {
            return new PackageLibraryPopulationSelection.NonSuccess(
                compileReceipt,
                runtimeReceipt,
                toolDeclarationUnavailable: true);
        }

        PackageToolDeclarationEvidence? declaration =
            nuspecBytes is { } suppliedNuspec
                ? PackageToolDeclarationEvidence.TryCreate(
                    coordinate,
                    content,
                    suppliedNuspec)
                : readManifestFromContent
                    ? PackageToolDeclarationEvidence.TryCreate(
                    coordinate,
                    content)
                    : null;
        if (declaration is null)
        {
            return new PackageLibraryPopulationSelection.NonSuccess(
                compileReceipt,
                runtimeReceipt,
                toolDeclarationUnavailable:
                    nuspecBytes is null
                    && !readManifestFromContent);
        }

        PackageToolSliceSelection toolSelection =
            PackageToolSliceMeasurementProjection.SelectEntries(
                content.EnumerateEntries(),
                compileReceipt.RequestedTargetFramework);
        var toolEvidence = new PackageToolSliceMeasurementEvidence(
            declaration,
            compileReceipt.RequestedTargetFramework,
            toolSelection.AvailableTargetFrameworks);
        return toolSelection.Status
                == PackageToolSliceSelectionStatus.Selected
            && !toolSelection.SelectedEntries.IsEmpty
                ? new PackageLibraryPopulationSelection.Tool(
                    compileReceipt,
                    runtimeReceipt,
                    toolEvidence,
                    toolSelection)
                : new PackageLibraryPopulationSelection.NonSuccess(
                    compileReceipt,
                    runtimeReceipt,
                    toolEvidence,
                    toolSelection);
    }
}
