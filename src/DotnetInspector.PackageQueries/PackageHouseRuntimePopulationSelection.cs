using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One Package Root after applying the shared Selection RID policy when its
/// implementation population requires runtime qualification.
/// </summary>
public sealed class PackageHouseRuntimePopulationSelection
{
    internal PackageHouseRuntimePopulationSelection(
        PackageRootBinding binding,
        PackageRuntimePopulationChoiceResult? choice)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (choice is PackageRuntimePopulationChoiceResult.Selected selected
            && !string.Equals(
                binding.Coordinate.RuntimeIdentifier,
                selected.RuntimeIdentifier,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A selected runtime population must match the Package Root coordinate.",
                nameof(binding));
        }

        Binding = binding;
        Choice = choice;
    }

    public PackageRootBinding Binding { get; }

    /// <summary>
    /// The typed shared-policy result, or <see langword="null"/> when no RID
    /// implementation inventory exists.
    /// </summary>
    public PackageRuntimePopulationChoiceResult? Choice { get; }
}

/// <summary>
/// Applies package-local Selection RID policy to one exact House contribution
/// without repeating acquisition.
/// </summary>
public static class PackageHouseRuntimePopulationSelector
{
    public static PackageHouseRuntimePopulationSelection Select(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseRootContribution contribution,
        PackageRuntimePopulationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(authority);
        if (!ReferenceEquals(settlement.Result, contribution.Result))
        {
            throw new ArgumentException(
                "Runtime-population selection must consume the contribution's exact House settlement.",
                nameof(settlement));
        }

        PackageCompileAssetSelectionReceipt compileReceipt =
            contribution.SelectionReceipt;
        string? runtimeTargetFramework =
            compileReceipt.RequestedTargetFramework
            ?? compileReceipt.Selection.TargetFramework;
        PackageAssetSelectionReceipt runtimeReceipt =
            PackageAssetSelector.EvaluateSingleRuntime(
                settlement.Payload.Content,
                runtimeTargetFramework);
        if (runtimeReceipt.AvailableRuntimePopulations.Count == 0)
        {
            return new(
                contribution.Binding,
                choice: null);
        }

        PackageRuntimePopulationChoiceResult choice =
            PackageRuntimePopulationChoice.Evaluate(
                new PackageRuntimePopulationChoiceRequest(
                    PackageRuntimePopulationChoiceOperationAssociation
                        .Create(),
                    settlement.Payload.Coordinate,
                    runtimeReceipt,
                    authority));
        if (choice
            is not PackageRuntimePopulationChoiceResult.Selected selected)
        {
            return new(
                contribution.Binding,
                choice);
        }

        PackageCompileAssetSelectionReceipt selectedReceipt =
            PackageCompileAssetSelector.Evaluate(
                settlement.Payload.Content,
                compileReceipt.PackageId,
                compileReceipt.Policy,
                compileReceipt.RequestedTargetFramework,
                selected.RuntimeIdentifier);
        if (selectedReceipt.RequestedTargetFramework is null)
        {
            // Freeze the owner-selected highest framework for the RID-qualified
            // Root while retaining the original omitted-framework request.
            selectedReceipt =
                PackageCompileAssetSelector.RetainSelection(
                    settlement.Payload.Content,
                    compileReceipt.PackageId,
                    PackageCompileAssetSelectionPolicy.ExactTarget,
                    selectedReceipt.Selection,
                    selectedReceipt.Selection.TargetFramework
                        ?? selected.SelectedTargetFramework,
                    selected.RuntimeIdentifier);
        }
        if (!PackageRootBinding.TryCreateFromSourceSelection(
                settlement.Payload,
                selectedReceipt,
                OriginalRequestedFramework(
                    contribution.Result.Request.TargetContext),
                out PackageRootBinding? selectedBinding,
                runtimePopulationTargetFramework:
                    runtimeTargetFramework,
                retainsRuntimePopulationTargetFramework: true))
        {
            throw new InvalidOperationException(
                "The selected runtime population could not be represented by the existing Package Root coordinate.");
        }

        selectedBinding = selectedBinding.WithAssetDemand(
            contribution.Binding.Root.AssetDemand,
            contribution.Binding.Root.ImplementationNames);
        if (contribution.Binding.Root.LibrarySelector is { } selector)
            selectedBinding = selectedBinding.WithLibrarySelection(selector);

        return new(
            selectedBinding,
            choice);
    }

    private static string? OriginalRequestedFramework(
        PackageHouseTargetContext? target) =>
        target?.Mode is PackageHouseTargetSelectionMode.OwnerDefault
            or PackageHouseTargetSelectionMode.FrozenTarget
            or null
            ? null
            : target.RequestedFramework;
}
