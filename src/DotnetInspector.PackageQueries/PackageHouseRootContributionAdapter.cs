using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One PackageHouse compile result paired with the package Root constructed
/// from its exact acquired generation and selector-issued receipt.
/// </summary>
public sealed class PackageHouseRootContribution
{
    internal PackageHouseRootContribution(
        PackageHouseResult result,
        PackageHouseRealizationReceipt.Compile realization,
        PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(binding);
        if (!ReferenceEquals(
                result.Evidence.Realization,
                realization))
        {
            throw new ArgumentException(
                "A package Root contribution must retain the result's exact compile realization.",
                nameof(realization));
        }
        if (!ReferenceEquals(
                binding.ContentGenerationIdentity,
                realization.Receipt.Generation)
            || !binding.Coordinate.PackageId.Equals(
                realization.Acquisition.Candidate.Coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !binding.Coordinate.Version.Equals(
                realization.Acquisition.Candidate.Coordinate.Version,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The package Root binding must describe the House realization's exact package generation.",
                nameof(binding));
        }

        Result = result;
        Realization = realization;
        Binding = binding;
    }

    public PackageHouseResult Result { get; }

    public PackageHouseAcquisitionReceipt Acquisition =>
        Realization.Acquisition;

    public PackageHouseRealizationReceipt.Compile Realization { get; }

    public PackageCompileAssetSelectionReceipt SelectionReceipt =>
        Realization.Receipt;

    public PackageRootBinding Binding { get; }
}

/// <summary>Why one House settlement cannot issue a package Root contribution.</summary>
public enum PackageHouseRootNoContributionReason
{
    ResourceFreeSettlement,
    CompileRealizationUnavailable,
    OperationFailed,
    CoordinateNotRepresentable,
}

/// <summary>
/// The typed result of adapting one closed PackageHouse settlement to the
/// Workspace package Root currency.
/// </summary>
public abstract class PackageHouseRootContributionOutcome
{
    private PackageHouseRootContributionOutcome(
        PackageHouseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    public PackageHouseResult Result { get; }

    public sealed class Contributed
        : PackageHouseRootContributionOutcome
    {
        internal Contributed(
            PackageHouseRootContribution contribution)
            : base(contribution.Result)
        {
            Contribution = contribution;
        }

        public PackageHouseRootContribution Contribution { get; }
    }

    public sealed class NoContribution
        : PackageHouseRootContributionOutcome
    {
        internal NoContribution(
            PackageHouseResult result,
            PackageHouseRootNoContributionReason reason)
            : base(result)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }

        public PackageHouseRootNoContributionReason Reason { get; }
    }
}

/// <summary>
/// Adapts one PackageHouse settlement without repeating acquisition or compile
/// asset selection.
/// </summary>
public static class PackageHouseRootContributionAdapter
{
    public static PackageHouseRootContributionOutcome Create(
        PackageHouseSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        PackageHouseResult result = settlement.Result;
        if (settlement is not PackageHouseSettlement.Acquired acquired)
        {
            return new PackageHouseRootContributionOutcome.NoContribution(
                result,
                PackageHouseRootNoContributionReason
                    .ResourceFreeSettlement);
        }
        if (result.Evidence.Realization
            is not PackageHouseRealizationReceipt.Compile realization)
        {
            return new PackageHouseRootContributionOutcome.NoContribution(
                result,
                PackageHouseRootNoContributionReason
                    .CompileRealizationUnavailable);
        }
        if (result
            is not (PackageHouseResult.Settled
                or PackageHouseResult.NoMatch
                or PackageHouseResult.Rejected))
        {
            return new PackageHouseRootContributionOutcome.NoContribution(
                result,
                PackageHouseRootNoContributionReason.OperationFailed);
        }
        if (!PackageRootBinding.TryCreateFromSourceSelection(
                acquired.Payload,
                realization.Receipt,
                out PackageRootBinding? binding))
        {
            return new PackageHouseRootContributionOutcome.NoContribution(
                result,
                PackageHouseRootNoContributionReason
                    .CoordinateNotRepresentable);
        }

        // The Root records the demand it was realized with: a surface-only
        // request yields a surface-only Root, which prepares no
        // implementation role and so never opens an implementation entry a
        // ranged read did not fetch (docs/design/package-read-demand.md#asset-demand).
        return new PackageHouseRootContributionOutcome.Contributed(
            new PackageHouseRootContribution(
                result,
                realization,
                binding.WithAssetDemand(result.Request.AssetDemand)));
    }
}
