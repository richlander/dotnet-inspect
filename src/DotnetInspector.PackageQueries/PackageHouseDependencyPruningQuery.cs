using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Determines whether one normalized dependency input authorizes a new
/// PackageHouse pruning evaluation.
/// </summary>
public static class PackageHouseDependencyPruningQuery
{
    public static PackageHouseDependencyPruningResult Execute(
        PackageHouseDependencyInput input,
        PlatformPruneInventory? inventory)
    {
        ArgumentNullException.ThrowIfNull(input);

        switch (input.Subject.Authorship)
        {
            case PackageDependencyEvidenceAuthorship.ApplicationAuthored:
                return new PackageHouseDependencyPruningResult
                    .ApplicationAuthoredExemption(input);

            case PackageDependencyEvidenceAuthorship.Unattributed:
                return new PackageHouseDependencyPruningResult
                    .UnattributedAuthorship(input);
        }

        switch (input.Root.Processing)
        {
            case PackageDependencyEvidenceProcessingResult.Available
            {
                IsComplete: false,
            } incomplete:
                return new PackageHouseDependencyPruningResult
                    .ProcessingIncomplete(input, incomplete);

            case PackageDependencyEvidenceProcessingResult.Unavailable
                unavailable:
                return new PackageHouseDependencyPruningResult
                    .ProcessingUnavailable(input, unavailable);

            case PackageDependencyEvidenceProcessingResult.Failed failed:
                return new PackageHouseDependencyPruningResult
                    .ProcessingFailed(input, failed);

            case PackageDependencyEvidenceProcessingResult.Available available
                when available.Observations.Any(
                    observation =>
                        observation
                        == PackageDependencyEvidenceProcessingObservation
                            .RuntimeDependencyProjection):
                return new PackageHouseDependencyPruningResult
                    .RuntimeProjected(input, available);

            case PackageDependencyEvidenceProcessingResult.Available available
                when available.Observations.Any(
                    observation =>
                        observation
                        == PackageDependencyEvidenceProcessingObservation
                            .PackagePruningEvaluation):
                return new PackageHouseDependencyPruningResult
                    .PreviouslyEvaluated(input, available);

            case PackageDependencyEvidenceProcessingResult.Available available:
                return new PackageHouseDependencyPruningResult
                    .ProcessingNotEvidenced(input, available);
        }

        if (input.Subject
            is not PackageHouseDependencySubject.Declaration declaration)
        {
            return TargetUnavailable(
                input,
                PackageHouseDependencyPruningTargetUnavailableReason
                    .RelationshipTargetCorrespondenceUnavailable);
        }

        PackageDependencyEvidenceSelection selection =
            input.Root.Selection;
        if (selection.Status
                != PackageDependencyEvidenceSelectionStatus.Selected
            || selection.SelectedGroup is null
            || selection.RequestedFramework is null)
        {
            return TargetUnavailable(
                input,
                PackageHouseDependencyPruningTargetUnavailableReason
                    .SelectionUnavailable);
        }

        if (selection.SelectedGroup != declaration.Evidence.Identity.Group)
        {
            return TargetUnavailable(
                input,
                PackageHouseDependencyPruningTargetUnavailableReason
                    .DeclarationNotSelected);
        }

        PackageHouseTargetContext? target = input.Request.TargetContext;
        if (target is null
            || target.Mode != PackageHouseTargetSelectionMode.Exact)
        {
            return TargetUnavailable(
                input,
                PackageHouseDependencyPruningTargetUnavailableReason
                    .MissingExactPackageTarget);
        }

        if (!string.Equals(
                selection.RequestedFramework.Value.ToString(),
                target.RequestedFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            return TargetUnavailable(
                input,
                PackageHouseDependencyPruningTargetUnavailableReason
                    .RequestedFrameworkMismatch);
        }

        if (target.PlatformTarget is null)
        {
            return TargetUnavailable(
                input,
                PackageHouseDependencyPruningTargetUnavailableReason
                    .MissingPlatformTarget);
        }

        if (inventory is null)
        {
            return TargetUnavailable(
                input,
                PackageHouseDependencyPruningTargetUnavailableReason
                    .InventoryUnavailable);
        }

        return new PackageHouseDependencyPruningResult.Evaluated(
            input,
            PackageHousePruningReceipt.Evaluate(
                input.Request,
                inventory));
    }

    private static PackageHouseDependencyPruningResult.TargetUnavailable
        TargetUnavailable(
            PackageHouseDependencyInput input,
            PackageHouseDependencyPruningTargetUnavailableReason reason) =>
        new(input, reason);
}

public abstract record PackageHouseDependencyPruningResult(
    PackageHouseDependencyInput Input)
{
    public sealed record Evaluated(
        PackageHouseDependencyInput Value,
        PackageHousePruningReceipt Pruning)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record ApplicationAuthoredExemption(
        PackageHouseDependencyInput Value)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record UnattributedAuthorship(
        PackageHouseDependencyInput Value)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record ProcessingIncomplete(
        PackageHouseDependencyInput Value,
        PackageDependencyEvidenceProcessingResult.Available Processing)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record ProcessingUnavailable(
        PackageHouseDependencyInput Value,
        PackageDependencyEvidenceProcessingResult.Unavailable Processing)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record ProcessingFailed(
        PackageHouseDependencyInput Value,
        PackageDependencyEvidenceProcessingResult.Failed Processing)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record RuntimeProjected(
        PackageHouseDependencyInput Value,
        PackageDependencyEvidenceProcessingResult.Available Processing)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record PreviouslyEvaluated(
        PackageHouseDependencyInput Value,
        PackageDependencyEvidenceProcessingResult.Available Processing)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record ProcessingNotEvidenced(
        PackageHouseDependencyInput Value,
        PackageDependencyEvidenceProcessingResult.Available Processing)
        : PackageHouseDependencyPruningResult(Value);

    public sealed record TargetUnavailable(
        PackageHouseDependencyInput Value,
        PackageHouseDependencyPruningTargetUnavailableReason Reason)
        : PackageHouseDependencyPruningResult(Value);
}

/// <summary>
/// Why exact target correspondence could not authorize a pruning evaluation.
/// </summary>
public enum PackageHouseDependencyPruningTargetUnavailableReason
{
    RelationshipTargetCorrespondenceUnavailable,
    SelectionUnavailable,
    DeclarationNotSelected,
    MissingExactPackageTarget,
    RequestedFrameworkMismatch,
    MissingPlatformTarget,
    InventoryUnavailable,
}
