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

        if (input.Subject is PackageHouseDependencySubject.Declaration declaration)
        {
            PackageHouseDependencyPruningApplicability applicability =
                PackageHouseDependencyPruningApplicabilityQuery.Execute(
                    input.Root,
                    declaration.Evidence,
                    input.Request.TargetContext);
            return applicability.State switch
            {
                PackageHouseDependencyPruningApplicabilityState
                        .ApplicationAuthoredExemption =>
                    new PackageHouseDependencyPruningResult
                        .ApplicationAuthoredExemption(input),
                PackageHouseDependencyPruningApplicabilityState
                        .UnattributedAuthorship =>
                    new PackageHouseDependencyPruningResult
                        .UnattributedAuthorship(input),
                PackageHouseDependencyPruningApplicabilityState
                        .ProcessingIncomplete =>
                    new PackageHouseDependencyPruningResult
                        .ProcessingIncomplete(
                            input,
                            (PackageDependencyEvidenceProcessingResult
                                .Available)applicability.Processing!),
                PackageHouseDependencyPruningApplicabilityState
                        .ProcessingUnavailable =>
                    new PackageHouseDependencyPruningResult
                        .ProcessingUnavailable(
                            input,
                            (PackageDependencyEvidenceProcessingResult
                                .Unavailable)applicability.Processing!),
                PackageHouseDependencyPruningApplicabilityState
                        .ProcessingFailed =>
                    new PackageHouseDependencyPruningResult
                        .ProcessingFailed(
                            input,
                            (PackageDependencyEvidenceProcessingResult
                                .Failed)applicability.Processing!),
                PackageHouseDependencyPruningApplicabilityState
                        .RuntimeProjected =>
                    new PackageHouseDependencyPruningResult
                        .RuntimeProjected(
                            input,
                            (PackageDependencyEvidenceProcessingResult
                                .Available)applicability.Processing!),
                PackageHouseDependencyPruningApplicabilityState
                        .PreviouslyEvaluated =>
                    new PackageHouseDependencyPruningResult
                        .PreviouslyEvaluated(
                            input,
                            (PackageDependencyEvidenceProcessingResult
                                .Available)applicability.Processing!),
                PackageHouseDependencyPruningApplicabilityState
                        .ProcessingNotEvidenced =>
                    new PackageHouseDependencyPruningResult
                        .ProcessingNotEvidenced(
                            input,
                            (PackageDependencyEvidenceProcessingResult
                                .Available)applicability.Processing!),
                PackageHouseDependencyPruningApplicabilityState
                        .TargetUnavailable =>
                    TargetUnavailable(
                        input,
                        applicability.TargetUnavailableReason!.Value),
                PackageHouseDependencyPruningApplicabilityState
                        .CandidateRequired =>
                    Evaluate(input, inventory),
                _ => throw new InvalidOperationException(
                    "Unknown package dependency pruning applicability."),
            };
        }

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

        return TargetUnavailable(
            input,
            PackageHouseDependencyPruningTargetUnavailableReason
                .RelationshipTargetCorrespondenceUnavailable);
    }

    private static PackageHouseDependencyPruningResult Evaluate(
        PackageHouseDependencyInput input,
        PlatformPruneInventory? inventory)
    {
        if (input.Request.TargetContext?.PlatformTarget is null)
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

/// <summary>
/// Determines whether one normalized declaration requires candidate-bound
/// PackageHouse pruning evaluation without acquiring a candidate or platform
/// inventory.
/// </summary>
public static class PackageHouseDependencyPruningApplicabilityQuery
{
    public static PackageHouseDependencyPruningApplicability Execute(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration,
        PackageHouseTargetContext? targetContext)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(declaration);
        PackageDependencyEvidenceDeclaration retained =
            PackageHouseDependencyInputAdapter.RequireDeclaration(
                root,
                declaration);

        switch (retained.Authorship)
        {
            case PackageDependencyEvidenceAuthorship.ApplicationAuthored:
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .ApplicationAuthoredExemption);

            case PackageDependencyEvidenceAuthorship.Unattributed:
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .UnattributedAuthorship);
        }

        switch (root.Processing)
        {
            case PackageDependencyEvidenceProcessingResult.Available
            {
                IsComplete: false,
            } incomplete:
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .ProcessingIncomplete,
                    incomplete);

            case PackageDependencyEvidenceProcessingResult.Unavailable
                unavailable:
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .ProcessingUnavailable,
                    unavailable);

            case PackageDependencyEvidenceProcessingResult.Failed failed:
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .ProcessingFailed,
                    failed);

            case PackageDependencyEvidenceProcessingResult.Available available
                when available.Observations.Any(
                    observation =>
                        observation
                        == PackageDependencyEvidenceProcessingObservation
                            .RuntimeDependencyProjection):
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .RuntimeProjected,
                    available);

            case PackageDependencyEvidenceProcessingResult.Available available
                when available.Observations.Any(
                    observation =>
                        observation
                        == PackageDependencyEvidenceProcessingObservation
                            .PackagePruningEvaluation):
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .PreviouslyEvaluated,
                    available);

            case PackageDependencyEvidenceProcessingResult.Available available:
                return Result(
                    PackageHouseDependencyPruningApplicabilityState
                        .ProcessingNotEvidenced,
                    available);
        }

        PackageDependencyEvidenceSelection selection = root.Selection;
        if (selection.Status
                != PackageDependencyEvidenceSelectionStatus.Selected
            || selection.SelectedGroup is null
            || selection.RequestedFramework is null)
        {
            return TargetUnavailable(
                PackageHouseDependencyPruningTargetUnavailableReason
                    .SelectionUnavailable);
        }

        if (selection.SelectedGroup != retained.Identity.Group)
        {
            return TargetUnavailable(
                PackageHouseDependencyPruningTargetUnavailableReason
                    .DeclarationNotSelected);
        }

        if (targetContext is null
            || targetContext.Mode != PackageHouseTargetSelectionMode.Exact)
        {
            return TargetUnavailable(
                PackageHouseDependencyPruningTargetUnavailableReason
                    .MissingExactPackageTarget);
        }

        if (!string.Equals(
                selection.RequestedFramework.Value.ToString(),
                targetContext.RequestedFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            return TargetUnavailable(
                PackageHouseDependencyPruningTargetUnavailableReason
                    .RequestedFrameworkMismatch);
        }

        return Result(
            PackageHouseDependencyPruningApplicabilityState
                .CandidateRequired);

        PackageHouseDependencyPruningApplicability Result(
            PackageHouseDependencyPruningApplicabilityState state,
            PackageDependencyEvidenceProcessingResult? processing = null) =>
            new(
                root,
                retained,
                state,
                processing,
                TargetUnavailableReason: null);

        PackageHouseDependencyPruningApplicability TargetUnavailable(
            PackageHouseDependencyPruningTargetUnavailableReason reason) =>
            new(
                root,
                retained,
                PackageHouseDependencyPruningApplicabilityState
                    .TargetUnavailable,
                Processing: null,
                reason);
    }
}

/// <summary>The candidate-free applicability state of one normalized declaration.</summary>
public enum PackageHouseDependencyPruningApplicabilityState
{
    CandidateRequired,
    ApplicationAuthoredExemption,
    UnattributedAuthorship,
    ProcessingIncomplete,
    ProcessingUnavailable,
    ProcessingFailed,
    RuntimeProjected,
    PreviouslyEvaluated,
    ProcessingNotEvidenced,
    TargetUnavailable,
}

/// <summary>
/// One candidate- and inventory-free PackageHouse pruning applicability result.
/// </summary>
public sealed record PackageHouseDependencyPruningApplicability(
    PackageDependencyEvidenceRoot Root,
    PackageDependencyEvidenceDeclaration Declaration,
    PackageHouseDependencyPruningApplicabilityState State,
    PackageDependencyEvidenceProcessingResult? Processing,
    PackageHouseDependencyPruningTargetUnavailableReason?
        TargetUnavailableReason);

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
