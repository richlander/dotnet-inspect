using System.Collections.Immutable;
using DotnetInspector.Platforms;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Package-owned evidence binding one policy-issued platform-supply receipt to
/// the exact House request, package coordinate, and platform target.
/// </summary>
public sealed class PackageHousePruningReceipt
{
    /// <summary>
    /// Evaluates the exact demand coordinate against one target-bound platform
    /// inventory and retains the resulting correspondence.
    /// </summary>
    public static PackageHousePruningReceipt Evaluate(
        PackageHouseRequest request,
        PlatformPruneInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(inventory);
        PackageSourceCoordinate coordinate =
            RequireCoordinate(request);
        PackageHouseTargetContext target =
            request.TargetContext
            ?? throw new ArgumentException(
                "Pruning requires an exact PackageHouse target context.",
                nameof(request));
        if (target.Mode != PackageHouseTargetSelectionMode.Exact)
        {
            throw new ArgumentException(
                "Pruning requires an exact PackageHouse target context.",
                nameof(request));
        }

        return new(
            request,
            PlatformPrunePolicy.Evaluate(
                inventory,
                new PackageCoordinate(
                    coordinate.PackageId,
                    coordinate.Version,
                    target.RequestedFramework,
                    target.RuntimeIdentifier)));
    }

    internal PackageHousePruningReceipt(
        PackageHouseRequest request,
        PlatformSupplyReceipt policy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        PackageSourceCoordinate coordinate =
            RequireCoordinate(request);
        if (request.TargetContext
                is not
                {
                    Mode: PackageHouseTargetSelectionMode.Exact,
                    PlatformTarget: { } target,
                })
        {
            throw new ArgumentException(
                "A pruning receipt requires the request's exact platform correspondence.",
                nameof(request));
        }
        if (!PolicyCoordinateMatches(
                policy.Coordinate,
                coordinate,
                request.TargetContext)
            || !policy.Inventory.TargetFramework.Equals(
                target.TargetFramework.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The policy receipt must describe the request's exact package and framework comparison.",
                nameof(policy));
        }

        PlatformPruneFamily? family =
            policy.Inventory.Families.FirstOrDefault(candidate =>
                candidate.Name.Equals(
                    SharedFrameworkName(target.Family),
                    StringComparison.OrdinalIgnoreCase));
        if (family is null
            || !family.TargetPackVersion.ToNormalizedString().Equals(
                target.Version.Value,
                StringComparison.Ordinal)
            || (policy.Supply.DelegatesToPlatform
                && !SharedFrameworkName(target.Family).Equals(
                    policy.Supply.Family,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The policy inventory and any delegating supplier must match the exact platform family version.",
                nameof(policy));
        }

        Request = request;
        Policy = policy;
    }

    public PackageHouseRequest Request { get; }

    public PackageSourceCoordinate Coordinate =>
        RequireCoordinate(Request);

    public PlatformFamilyTarget Target =>
        Request.TargetContext!.PlatformTarget!;

    public PlatformSupplyReceipt Policy { get; }

    public PlatformSupply Supply => Policy.Supply;

    private static PackageSourceCoordinate RequireCoordinate(
        PackageHouseRequest request) =>
        request.Demand switch
        {
            PackageHouseDemand.Exact exact => exact.Coordinate,
            PackageHouseDemand.Candidate candidate =>
                candidate.Value.Coordinate,
            _ => throw new ArgumentException(
                "Pruning requires an exact or candidate-bound package demand.",
                nameof(request)),
        };

    private static bool PolicyCoordinateMatches(
        PackageCoordinate policy,
        PackageSourceCoordinate exact,
        PackageHouseTargetContext? target)
    {
        if (policy.Version is null
            || PackageSourceCoordinate.Create(
                policy.PackageId,
                policy.Version) != exact)
        {
            return false;
        }

        return target?.RequestedFramework is not { } framework
            ? policy.Framework is null
            : framework.Equals(
                policy.Framework,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string SharedFrameworkName(PlatformFamily family) =>
        family switch
        {
            PlatformFamily.DotNetRuntime => "Microsoft.NETCore.App",
            PlatformFamily.AspNetCore => "Microsoft.AspNetCore.App",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
}

/// <summary>How PackageHouse disposed the package side of one operation.</summary>
public enum PackageHouseDecision
{
    RetainPackage,
    DelegateToPlatform,
    Stop,
}

/// <summary>Resource-free evidence for one package settlement decision.</summary>
public sealed class PackageHouseDecisionReceipt
{
    private PackageHouseDecisionReceipt(
        PackageHouseRequest request,
        PackageHouseDecision decision,
        PackageSourceCoordinate? coordinate,
        PackageAcquisitionCandidate? candidate,
        PackageHousePruningReceipt? pruning,
        PackageVersionResolutionReceipt? versionResolution)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(nameof(decision));
        PackageHouseContractValidation.RequireVersionResolutionMatchesDemand(
            request.Demand,
            decision,
            coordinate,
            candidate,
            pruning,
            versionResolution);
        if (candidate is not null
            && candidate.Coordinate != coordinate)
        {
            throw new ArgumentException(
                "The acquisition candidate must describe the settled coordinate.",
                nameof(candidate));
        }
        if (pruning is not null
            && (!ReferenceEquals(pruning.Request, request)
                || pruning.Coordinate != coordinate))
        {
            throw new ArgumentException(
                "The pruning receipt must describe the same request and coordinate.",
                nameof(pruning));
        }

        bool delegates =
            decision == PackageHouseDecision.DelegateToPlatform;
        if (delegates
            != (coordinate is not null
                && pruning?.Supply.DelegatesToPlatform is true))
        {
            throw new ArgumentException(
                "A platform decision requires one exact Subsumed pruning receipt.",
                nameof(pruning));
        }
        if (decision == PackageHouseDecision.RetainPackage
            && coordinate is null)
        {
            throw new ArgumentException(
                "A retained package decision requires one settled coordinate.",
                nameof(coordinate));
        }
        if (decision != PackageHouseDecision.DelegateToPlatform
            && pruning?.Supply.DelegatesToPlatform is true)
        {
            throw new ArgumentException(
                "A Subsumed pruning receipt can only produce platform delegation.",
                nameof(pruning));
        }

        Request = request;
        Decision = decision;
        Coordinate = coordinate;
        Candidate = candidate;
        Pruning = pruning;
        VersionResolution = versionResolution;
    }

    public PackageHouseRequest Request { get; }

    public PackageHouseDecision Decision { get; }

    public PackageSourceCoordinate? Coordinate { get; }

    public PackageAcquisitionCandidate? Candidate { get; }

    public PackageHousePruningReceipt? Pruning { get; }

    public PackageVersionResolutionReceipt? VersionResolution { get; }

    internal static PackageHouseDecisionReceipt RetainPackage(
        PackageHouseRequest request,
        PackageSourceCoordinate coordinate,
        PackageAcquisitionCandidate? candidate = null,
        PackageHousePruningReceipt? pruning = null) =>
        new(
            request,
            PackageHouseDecision.RetainPackage,
            coordinate,
            candidate,
            pruning,
            versionResolution: null);

    internal static PackageHouseDecisionReceipt RetainSelectedPackage(
        PackageHouseRequest request,
        PackageVersionResolutionReceipt.Resolved versionResolution) =>
        new(
            request,
            PackageHouseDecision.RetainPackage,
            versionResolution.Coordinate,
            versionResolution.Candidate,
            pruning: null,
            versionResolution: versionResolution);

    internal static PackageHouseDecisionReceipt RetainPriorPackage(
        PackageHouseRequest request,
        PackageVersionResolutionReceipt.Prior versionResolution) =>
        new(
            request,
            PackageHouseDecision.RetainPackage,
            versionResolution.Coordinate,
            versionResolution.Candidate,
            pruning: null,
            versionResolution: versionResolution);

    internal static PackageHouseDecisionReceipt DelegateToPlatform(
        PackageHouseRequest request,
        PackageSourceCoordinate coordinate,
        PackageAcquisitionCandidate? candidate,
        PackageHousePruningReceipt pruning) =>
        new(
            request,
            PackageHouseDecision.DelegateToPlatform,
            coordinate,
            candidate,
            pruning,
            versionResolution: null);

    internal static PackageHouseDecisionReceipt Stop(
        PackageHouseRequest request,
        PackageSourceCoordinate? coordinate = null,
        PackageAcquisitionCandidate? candidate = null,
        PackageHousePruningReceipt? pruning = null,
        PackageVersionResolutionReceipt? versionResolution = null) =>
        new(
            request,
            PackageHouseDecision.Stop,
            coordinate,
            candidate,
            pruning,
            versionResolution);
}

/// <summary>
/// Resource-free evidence binding an authorized payload generation to its
/// decision, candidate, authority, source result, producer, and origin.
/// </summary>
public sealed class PackageHouseAcquisitionReceipt
{
    internal PackageHouseAcquisitionReceipt(
        PackageHouseDecisionReceipt decision,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity source,
        PackagePayloadOrigin origin,
        PackageContentGenerationIdentity generation)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(generation);
        if (!Enum.IsDefined(origin))
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (decision.Request.Operation.Profile
            == PackageHouseOperationProfile.Settle)
        {
            throw new ArgumentException(
                "A Settle operation does not authorize payload acquisition.",
                nameof(decision));
        }
        if (decision.Decision != PackageHouseDecision.RetainPackage
            || decision.Candidate is not { } candidate
            || decision.Coordinate != candidate.Coordinate)
        {
            throw new ArgumentException(
                "Acquisition requires the decision's exact retained candidate.",
                nameof(decision));
        }
        if (!candidate.Authorities.Any(evidence =>
                ReferenceEquals(evidence.Authority, authority)))
        {
            throw new ArgumentException(
                "The payload authority must belong to the acquisition candidate.",
                nameof(authority));
        }
        if (!ReferenceEquals(source.Association, authority.Association))
        {
            throw new ArgumentException(
                "The payload source result must belong to the selected authority.",
                nameof(source));
        }

        Decision = decision;
        Authority = authority;
        Source = source;
        Origin = origin;
        Generation = generation;
    }

    public PackageHouseDecisionReceipt Decision { get; }

    public PackageAcquisitionCandidate Candidate => Decision.Candidate!;

    public ConfiguredPackageAuthority Authority { get; }

    public PackageSourceResultIdentity Source { get; }

    public PackageProducerIdentity Producer => Source.Producer;

    public PackagePayloadOrigin Origin { get; }

    public PackageContentGenerationIdentity Generation { get; }
}

/// <summary>
/// Resource-free package-to-library evidence retaining the complete
/// acquisition and selector-issued correspondence.
/// </summary>
public abstract class PackageHouseLibraryHandoff
{
    private PackageHouseLibraryHandoff(
        PackageHouseAcquisitionReceipt acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        Acquisition = acquisition;
    }

    public PackageHouseAcquisitionReceipt Acquisition { get; }

    public PackageHouseDecisionReceipt Decision => Acquisition.Decision;

    public PackageSourceCoordinate Coordinate => Decision.Coordinate!;

    public PackageHouseTargetContext? TargetContext =>
        Decision.Request.TargetContext;

    public sealed class Compile : PackageHouseLibraryHandoff
    {
        internal Compile(
            PackageHouseAcquisitionReceipt acquisition,
            PackageCompileAssetSelectionReceipt receipt,
            PackageCompileAsset asset,
            PackageCompileAsset? implementationAsset)
            : base(acquisition)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            ArgumentNullException.ThrowIfNull(asset);
            PackageHouseRealizationCorrespondence.RequireCompile(
                acquisition,
                receipt);
            if (!receipt.Selection.Assets.Any(candidate =>
                    ReferenceEquals(candidate, asset))
                || !ReferenceEquals(
                    receipt.Selection.FindImplementationAsset(asset),
                    implementationAsset))
            {
                throw new ArgumentException(
                    "A compile handoff must use one exact selected asset and its paired implementation asset.",
                    nameof(asset));
            }

            Receipt = receipt;
            Asset = asset;
            ImplementationAsset = implementationAsset;
        }

        public PackageCompileAssetSelectionReceipt Receipt { get; }

        public PackageCompileAsset Asset { get; }

        public PackageCompileAsset? ImplementationAsset { get; }
    }

    public sealed class Runtime : PackageHouseLibraryHandoff
    {
        internal Runtime(
            PackageHouseAcquisitionReceipt acquisition,
            PackageAssetSelectionReceipt receipt,
            PackageAssetEntry asset)
            : base(acquisition)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            ArgumentNullException.ThrowIfNull(asset);
            PackageHouseRealizationCorrespondence.RequireRuntime(
                acquisition,
                receipt);
            if (receipt.Selection
                    is not PackageAssetSelection.Selected selected
                || !selected.Universe.Assets.Any(candidate =>
                    ReferenceEquals(candidate, asset)))
            {
                throw new ArgumentException(
                    "A runtime handoff must use one exact selected asset.",
                    nameof(asset));
            }

            Receipt = receipt;
            Asset = asset;
        }

        public PackageAssetSelectionReceipt Receipt { get; }

        public PackageAssetEntry Asset { get; }
    }
}

/// <summary>
/// Resource-free evidence binding one existing package-owner asset-selection
/// outcome and any library handoffs to the exact acquisition and request.
/// </summary>
public abstract class PackageHouseRealizationReceipt
{
    private PackageHouseRealizationReceipt(
        PackageHouseAcquisitionReceipt acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (acquisition.Decision.Request.Operation.Profile
            != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Only a Realize operation can produce realization evidence.",
                nameof(acquisition));
        }

        Acquisition = acquisition;
    }

    public PackageHouseAcquisitionReceipt Acquisition { get; }

    internal abstract PackageHouseRealizationCompletion Completion { get; }

    public abstract ImmutableArray<PackageHouseLibraryHandoff>
        LibraryHandoffs { get; }

    public sealed class Compile : PackageHouseRealizationReceipt
    {
        internal Compile(
            PackageHouseAcquisitionReceipt acquisition,
            PackageCompileAssetSelectionReceipt receipt)
            : base(acquisition)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            PackageHouseRealizationCorrespondence.RequireCompile(
                acquisition,
                receipt);
            PackageHouseRequest request = acquisition.Decision.Request;

            Receipt = receipt;
            Completion = receipt.Selection.Status switch
            {
                PackageCompileAssetSelectionStatus.Selected
                    or PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                    PackageHouseRealizationCompletion.Settled,
                PackageCompileAssetSelectionStatus.NoCompileAssets
                    or PackageCompileAssetSelectionStatus
                        .NoMatchingTargetFramework =>
                    PackageHouseRealizationCompletion.NoMatch,
                _ => PackageHouseRealizationCompletion.Rejected,
            };
            LibraryHandoffs =
                request.LibraryHandoff
                    == PackageHouseLibraryHandoffMode.SelectedLibraries
                && receipt.Selection.IsSelected
                    ? [
                        .. receipt.Selection.Assets.Select(asset =>
                            new PackageHouseLibraryHandoff.Compile(
                                acquisition,
                                receipt,
                                asset,
                                receipt.Selection
                                    .FindImplementationAsset(asset))),
                    ]
                    : [];
        }

        public PackageCompileAssetSelectionReceipt Receipt { get; }

        public PackageCompileAssetSelection Selection => Receipt.Selection;

        internal override PackageHouseRealizationCompletion Completion
            { get; }

        public override ImmutableArray<PackageHouseLibraryHandoff>
            LibraryHandoffs { get; }
    }

    public sealed class Runtime : PackageHouseRealizationReceipt
    {
        internal Runtime(
            PackageHouseAcquisitionReceipt acquisition,
            PackageAssetSelectionReceipt receipt)
            : base(acquisition)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            PackageHouseRealizationCorrespondence.RequireRuntime(
                acquisition,
                receipt);

            Receipt = receipt;
            Completion = receipt.Selection switch
            {
                PackageAssetSelection.Selected =>
                    PackageHouseRealizationCompletion.Settled,
                PackageAssetSelection.NoMatch =>
                    PackageHouseRealizationCompletion.NoMatch,
                PackageAssetSelection.Ambiguous =>
                    PackageHouseRealizationCompletion.Ambiguous,
                _ => PackageHouseRealizationCompletion.Rejected,
            };
            LibraryHandoffs =
                acquisition.Decision.Request.LibraryHandoff
                    == PackageHouseLibraryHandoffMode.SelectedLibraries
                && receipt.Selection
                    is PackageAssetSelection.Selected selected
                    ? [
                        .. selected.Universe.Assets.Select(asset =>
                            new PackageHouseLibraryHandoff.Runtime(
                                acquisition,
                                receipt,
                                asset)),
                    ]
                    : [];
        }

        public PackageAssetSelectionReceipt Receipt { get; }

        public PackageAssetSelection Selection => Receipt.Selection;

        internal override PackageHouseRealizationCompletion Completion
            { get; }

        public override ImmutableArray<PackageHouseLibraryHandoff>
            LibraryHandoffs { get; }
    }
}

internal static class PackageHouseRealizationCorrespondence
{
    internal static void RequireCompile(
        PackageHouseAcquisitionReceipt acquisition,
        PackageCompileAssetSelectionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        ArgumentNullException.ThrowIfNull(receipt);
        if (acquisition.Decision.Request.AssetSelection
            != PackageHouseAssetSelectionKind.Compile)
        {
            throw new ArgumentException(
                "A compile selection requires a compile realization request.",
                nameof(acquisition));
        }
        if (!ReferenceEquals(
                receipt.Generation,
                acquisition.Generation)
            || !receipt.PackageId.Equals(
                acquisition.Decision.Coordinate!.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !PolicyMatches(
                acquisition.Decision.Request.TargetContext,
                receipt.Policy)
            || !RequestMatches(
                acquisition.Decision.Request.TargetContext,
                receipt.RequestedTargetFramework,
                receipt.RequestedRuntimeIdentifier))
        {
            throw new ArgumentException(
                "The compile selection receipt must describe the acquired generation and exact House selection request.",
                nameof(receipt));
        }
        if (receipt.Selection.Status
                == PackageCompileAssetSelectionStatus.Selected
            && !receipt.Selection.IsSelected)
        {
            throw new ArgumentException(
                "A selected compile outcome must carry its selected assets and default asset.",
                nameof(receipt));
        }
    }

    internal static void RequireRuntime(
        PackageHouseAcquisitionReceipt acquisition,
        PackageAssetSelectionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        ArgumentNullException.ThrowIfNull(receipt);
        if (acquisition.Decision.Request.AssetSelection
            != PackageHouseAssetSelectionKind.Runtime)
        {
            throw new ArgumentException(
                "A runtime selection requires a runtime realization request.",
                nameof(acquisition));
        }
        if (!ReferenceEquals(
                receipt.Generation,
                acquisition.Generation)
            || !RequestMatches(
                acquisition.Decision.Request.TargetContext,
                receipt.RequestedTargetFramework,
                receipt.RequestedRuntimeIdentifier))
        {
            throw new ArgumentException(
                "The runtime selection receipt must describe the acquired generation and exact House selection request.",
                nameof(receipt));
        }
    }

    private static bool PolicyMatches(
        PackageHouseTargetContext? target,
        PackageCompileAssetSelectionPolicy policy) =>
        target?.Mode switch
        {
            PackageHouseTargetSelectionMode.Exact =>
                policy == PackageCompileAssetSelectionPolicy.ExplicitTarget,
            PackageHouseTargetSelectionMode.OwnerDefault or null =>
                policy == PackageCompileAssetSelectionPolicy.HighestAvailable,
            _ => false,
        };

    private static bool RequestMatches(
        PackageHouseTargetContext? target,
        string? framework,
        string? runtimeIdentifier)
    {
        bool frameworkMatches = target?.Mode switch
        {
            PackageHouseTargetSelectionMode.Exact =>
                target.RequestedFramework!.Equals(
                    framework,
                    StringComparison.OrdinalIgnoreCase),
            PackageHouseTargetSelectionMode.OwnerDefault =>
                framework is null,
            null => framework is null,
            _ => false,
        };
        return frameworkMatches
            && string.Equals(
                target?.RuntimeIdentifier,
                runtimeIdentifier,
                StringComparison.Ordinal);
    }
}

internal enum PackageHouseRealizationCompletion
{
    Settled,
    NoMatch,
    Ambiguous,
    Rejected,
}

/// <summary>The PackageHouse stage that reported a typed failure.</summary>
public enum PackageHouseFailureStage
{
    Settlement,
    Pruning,
    Acquisition,
    Selection,
    Handoff,
    Operation,
}

/// <summary>The deadline that ended a PackageHouse operation.</summary>
public enum PackageHouseTimeoutKind
{
    Request,
    Operation,
}

/// <summary>One resource-free typed failure retained by a House result.</summary>
public abstract class PackageHouseFailure
{
    private PackageHouseFailure()
    {
    }

    public sealed class Source : PackageHouseFailure
    {
        internal Source(PackageSourceFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public PackageSourceFailure Failure { get; }
    }

    public sealed class Authority : PackageHouseFailure
    {
        internal Authority(
            PackageHouseOperationIdentity operation,
            PackageAuthorityFailure failure)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(failure);
            Operation = operation;
            Failure = failure;
        }

        public PackageHouseOperationIdentity Operation { get; }

        public PackageAuthorityFailure Failure { get; }
    }

    public sealed class Timeout : PackageHouseFailure
    {
        internal Timeout(
            PackageHouseOperationIdentity operation,
            PackageHouseTimeoutKind kind,
            TimeSpan duration)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    duration,
                    "A retained timeout duration must be positive.");
            }

            Operation = operation;
            Kind = kind;
            Duration = duration;
        }

        public PackageHouseOperationIdentity Operation { get; }

        public PackageHouseTimeoutKind Kind { get; }

        public TimeSpan Duration { get; }
    }

    public sealed class Stage : PackageHouseFailure
    {
        internal Stage(
            PackageHouseFailureStage stage,
            InertString reason)
        {
            if (!Enum.IsDefined(stage))
                throw new ArgumentOutOfRangeException(nameof(stage));
            StageKind = stage;
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public PackageHouseFailureStage StageKind { get; }

        public InertString Reason { get; }
    }
}

/// <summary>
/// The immutable evidence envelope retained by every terminal House result.
/// </summary>
public sealed class PackageHouseEvidence
{
    internal PackageHouseEvidence(
        PackageHouseRequest request,
        PackageHouseDecisionReceipt? decision = null,
        PackageHouseAcquisitionReceipt? acquisition = null,
        PackageHouseRealizationReceipt? realization = null,
        IEnumerable<PackageHouseFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (decision is not null
            && !ReferenceEquals(decision.Request, request))
        {
            throw new ArgumentException(
                "The decision receipt must retain the evidence request.",
                nameof(decision));
        }
        if (acquisition is not null
            && !ReferenceEquals(acquisition.Decision, decision))
        {
            throw new ArgumentException(
                "The acquisition receipt must retain the evidence decision.",
                nameof(acquisition));
        }
        if (realization is not null
            && !ReferenceEquals(realization.Acquisition, acquisition))
        {
            throw new ArgumentException(
                "The realization receipt must retain the evidence acquisition.",
                nameof(realization));
        }
        if (request.Operation.Profile == PackageHouseOperationProfile.Settle
            && (acquisition is not null || realization is not null))
        {
            throw new ArgumentException(
                "A Settle operation cannot retain acquisition or realization evidence.",
                nameof(acquisition));
        }
        if (request.Operation.Profile != PackageHouseOperationProfile.Realize
            && realization is not null)
        {
            throw new ArgumentException(
                "Only a Realize operation can retain realization evidence.",
                nameof(realization));
        }

        Request = request;
        Decision = decision;
        Acquisition = acquisition;
        Realization = realization;
        Failures = failures is null
            ? []
            : [.. failures];
        if (Failures.Any(failure => failure is null))
        {
            throw new ArgumentException(
                "A House evidence envelope cannot retain a null failure.",
                nameof(failures));
        }
        PackageHouseContractValidation.RequireFailuresMatchOperation(
            request.Operation,
            Failures,
            nameof(failures));
    }

    public PackageHouseRequest Request { get; }

    public PackageHouseDecisionReceipt? Decision { get; }

    public PackageHouseAcquisitionReceipt? Acquisition { get; }

    public PackageHouseRealizationReceipt? Realization { get; }

    public ImmutableArray<PackageHouseFailure> Failures { get; }

    public bool HasOperationTimeout =>
        Failures.OfType<PackageHouseFailure.Timeout>().Any(timeout =>
            timeout.Kind == PackageHouseTimeoutKind.Operation)
        || Failures.OfType<PackageHouseFailure.Authority>().Any(authority =>
            authority.Failure.Timeout?.Kind
                == PackageSourceTimeoutKind.Operation);
}

/// <summary>
/// A package-owned request for application orchestration to settle one
/// corresponding platform target.
/// </summary>
public sealed class PlatformDelegation
{
    internal PlatformDelegation(PackageHouseDecisionReceipt decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Decision != PackageHouseDecision.DelegateToPlatform
            || decision.Coordinate is null
            || decision.Pruning is not
                { Supply.DelegatesToPlatform: true })
        {
            throw new ArgumentException(
                "Platform delegation requires one complete PackageHouse pruning decision.",
                nameof(decision));
        }

        Decision = decision;
    }

    public PackageHouseDecisionReceipt Decision { get; }

    public PackageSourceCoordinate Coordinate => Decision.Coordinate!;

    public PackageHousePruningReceipt Pruning => Decision.Pruning!;

    public PlatformFamilyTarget Target => Pruning.Target;

    public PlatformSupplyReceipt Policy => Pruning.Policy;

    public PlatformSupply Supply => Pruning.Supply;
}

/// <summary>The closed resource-free result of one PackageHouse operation.</summary>
public abstract class PackageHouseResult
{
    private PackageHouseResult(PackageHouseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence = evidence;
    }

    public PackageHouseEvidence Evidence { get; }

    public PackageHouseRequest Request => Evidence.Request;

    public PackageHouseDecisionReceipt? Decision => Evidence.Decision;

    public sealed class Settled : PackageHouseResult
    {
        internal Settled(PackageHouseEvidence evidence)
            : base(evidence)
        {
            if (evidence.Decision?.Decision
                != PackageHouseDecision.RetainPackage)
            {
                throw new ArgumentException(
                    "A settled package result requires a retained package decision.",
                    nameof(evidence));
            }
            if (evidence.HasOperationTimeout)
            {
                throw new ArgumentException(
                    "An operation timeout is terminal and cannot produce a settled result.",
                    nameof(evidence));
            }

            bool valid = evidence.Request.Operation.Profile switch
            {
                PackageHouseOperationProfile.Settle =>
                    evidence.Acquisition is null
                    && evidence.Realization is null,
                PackageHouseOperationProfile.Acquire =>
                    evidence.Acquisition is not null
                    && evidence.Realization is null,
                PackageHouseOperationProfile.Realize =>
                    evidence.Acquisition is not null
                    && evidence.Realization?.Completion
                        == PackageHouseRealizationCompletion.Settled,
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException(
                    "Decision, acquisition, and realization evidence must match the operation profile.",
                    nameof(evidence));
            }
        }
    }

    public sealed class Delegated : PackageHouseResult
    {
        internal Delegated(
            PackageHouseEvidence evidence,
            PlatformDelegation delegation)
            : base(evidence)
        {
            ArgumentNullException.ThrowIfNull(delegation);
            if (!ReferenceEquals(evidence.Decision, delegation.Decision)
                || evidence.Acquisition is not null
                || evidence.Realization is not null
                || evidence.HasOperationTimeout)
            {
                throw new ArgumentException(
                    "A delegated result must retain only its matching package decision.",
                    nameof(evidence));
            }

            Delegation = delegation;
        }

        public PlatformDelegation Delegation { get; }
    }

    public sealed class NotFound : PackageHouseResult
    {
        internal NotFound(PackageHouseEvidence evidence, InertString reason)
            : base(evidence)
        {
            RequireNoCompletedRealization(evidence, nameof(evidence));
            RequireVersionResolutionOutcome<
                PackageVersionResolutionReceipt.NotFound>(
                    evidence,
                    nameof(evidence));
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class NoMatch : PackageHouseResult
    {
        internal NoMatch(PackageHouseEvidence evidence, InertString reason)
            : base(evidence)
        {
            RequireRealizationCompletionWhenPresent(
                evidence,
                PackageHouseRealizationCompletion.NoMatch,
                nameof(evidence));
            RequireVersionResolutionOutcome<
                PackageVersionResolutionReceipt.NoMatch>(
                    evidence,
                    nameof(evidence));
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Ambiguous : PackageHouseResult
    {
        internal Ambiguous(PackageHouseEvidence evidence, InertString reason)
            : base(evidence)
        {
            RequireRealizationCompletionWhenPresent(
                evidence,
                PackageHouseRealizationCompletion.Ambiguous,
                nameof(evidence));
            RequireVersionResolutionOutcome<
                PackageVersionResolutionReceipt.Ambiguous>(
                    evidence,
                    nameof(evidence));
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Rejected : PackageHouseResult
    {
        internal Rejected(PackageHouseEvidence evidence, InertString reason)
            : base(evidence)
        {
            RequireRealizationCompletionWhenPresent(
                evidence,
                PackageHouseRealizationCompletion.Rejected,
                nameof(evidence));
            RequireVersionResolutionOutcome<
                PackageVersionResolutionReceipt.Rejected>(
                    evidence,
                    nameof(evidence));
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Unavailable : PackageHouseResult
    {
        internal Unavailable(
            PackageHouseEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            RequireNoCompletedRealization(evidence, nameof(evidence));
            RequireVersionResolutionOutcome<
                PackageVersionResolutionReceipt.Unavailable>(
                    evidence,
                    nameof(evidence));
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Incomplete : PackageHouseResult
    {
        internal Incomplete(
            PackageHouseEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            RequireNoCompletedRealization(evidence, nameof(evidence));
            RequireVersionResolutionOutcome<
                PackageVersionResolutionReceipt.Incomplete>(
                    evidence,
                    nameof(evidence));
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Failed : PackageHouseResult
    {
        internal Failed(PackageHouseEvidence evidence, InertString reason)
            : base(evidence)
        {
            if (evidence.Realization is not null
                && !evidence.HasOperationTimeout)
            {
                throw new ArgumentException(
                    "A completed asset-selection outcome requires its corresponding House terminal result unless operation timeout takes precedence.",
                    nameof(evidence));
            }
            RequireVersionResolutionOutcome<
                PackageVersionResolutionReceipt.Failed>(
                    evidence,
                    nameof(evidence));
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    private static void RequireRealizationCompletionWhenPresent(
        PackageHouseEvidence evidence,
        PackageHouseRealizationCompletion expected,
        string parameterName)
    {
        if (evidence.Realization is { Completion: var actual }
            && actual != expected)
        {
            throw new ArgumentException(
                "The terminal result must match the retained asset-selection outcome.",
                parameterName);
        }
    }

    private static void RequireNoCompletedRealization(
        PackageHouseEvidence evidence,
        string parameterName)
    {
        if (evidence.Realization is not null)
        {
            throw new ArgumentException(
                "A completed asset-selection outcome requires its corresponding House terminal result.",
                parameterName);
        }
    }

    private static void RequireVersionResolutionOutcome<TReceipt>(
        PackageHouseEvidence evidence,
        string parameterName)
        where TReceipt : PackageVersionResolutionReceipt
    {
        if (evidence.Request.Demand
                is not PackageHouseDemand.Selecting)
        {
            return;
        }

        PackageVersionResolutionReceipt? resolution =
            evidence.Decision?.VersionResolution;
        if (typeof(TReceipt)
                == typeof(PackageVersionResolutionReceipt.Failed)
            && evidence.HasOperationTimeout)
        {
            return;
        }
        if (resolution is null)
        {
            throw new ArgumentException(
                "A selecting package result must retain its version-resolution receipt.",
                parameterName);
        }
        // A prior settlement is a settled arm exactly like Resolved: every
        // post-acquisition result, success or typed failure, may carry it.
        if (resolution
                is not PackageVersionResolutionReceipt.Resolved
            && resolution
                is not PackageVersionResolutionReceipt.Prior
            && resolution is not TReceipt)
        {
            throw new ArgumentException(
                "The PackageHouse terminal result must preserve the version-resolution terminal outcome.",
                parameterName);
        }
    }
}

internal static class PackageHouseContractValidation
{
    internal static void RequireFailuresMatchOperation(
        PackageHouseOperation operation,
        IEnumerable<PackageHouseFailure> failures,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(failures);
        foreach (PackageHouseFailure.Timeout timeout
            in failures.OfType<PackageHouseFailure.Timeout>())
        {
            TimeSpan expected = timeout.Kind switch
            {
                PackageHouseTimeoutKind.Request =>
                    operation.RequestTimeout,
                PackageHouseTimeoutKind.Operation =>
                    operation.OperationTimeout,
                _ => throw new ArgumentOutOfRangeException(parameterName),
            };
            if (!ReferenceEquals(
                    timeout.Operation,
                    operation.Identity)
                || timeout.Duration != expected)
            {
                throw new ArgumentException(
                    "A retained timeout must match the request's operation identity and configured duration.",
                    parameterName);
            }
        }
        foreach (PackageHouseFailure.Authority authority
            in failures.OfType<PackageHouseFailure.Authority>())
        {
            if (!ReferenceEquals(
                    authority.Operation,
                    operation.Identity))
            {
                throw new ArgumentException(
                    "A retained authority failure must match the request's operation identity.",
                    parameterName);
            }
            if (authority.Failure.Timeout is { } timeout)
            {
                TimeSpan expected = timeout.Kind switch
                {
                    PackageSourceTimeoutKind.Request
                        or PackageSourceTimeoutKind.MetadataBody =>
                        operation.RequestTimeout,
                    PackageSourceTimeoutKind.Operation =>
                        operation.OperationTimeout,
                    _ => throw new ArgumentOutOfRangeException(
                        parameterName),
                };
                if (timeout.Duration != expected)
                {
                    throw new ArgumentException(
                        "A retained authority timeout must match the request's configured duration.",
                        parameterName);
                }
            }
        }
    }

    internal static void RequireVersionResolutionMatchesDemand(
        PackageHouseDemand demand,
        PackageHouseDecision decision,
        PackageSourceCoordinate? coordinate,
        PackageAcquisitionCandidate? candidate,
        PackageHousePruningReceipt? pruning,
        PackageVersionResolutionReceipt? versionResolution)
    {
        ArgumentNullException.ThrowIfNull(demand);
        switch (demand)
        {
            case PackageHouseDemand.Exact exact:
                if (versionResolution is not null
                    || coordinate is not null
                        && exact.Coordinate != coordinate)
                {
                    throw new ArgumentException(
                        "An exact package demand accepts only its exact coordinate and no version-selection receipt.",
                        nameof(coordinate));
                }
                return;

            case PackageHouseDemand.Candidate candidateDemand:
                if (versionResolution is not null
                    || coordinate is not null
                        && candidateDemand.Value.Coordinate != coordinate
                    || (decision == PackageHouseDecision.Stop
                        ? candidate is not null
                        : !ReferenceEquals(
                            candidateDemand.Value,
                            candidate)))
                {
                    throw new ArgumentException(
                        "A candidate package demand accepts only its exact candidate and coordinate with no version-selection receipt.",
                        nameof(candidate));
                }
                return;

            case PackageHouseDemand.Selecting selecting:
                if (versionResolution is null
                    || !ReferenceEquals(
                        selecting.Request,
                        versionResolution.Request))
                {
                    throw new ArgumentException(
                        "A selecting package demand requires the resolution receipt for its exact request.",
                        nameof(versionResolution));
                }

                if (versionResolution
                    is PackageVersionResolutionReceipt.Resolved resolved)
                {
                    if (coordinate != resolved.Coordinate
                        || !ReferenceEquals(
                            candidate,
                            resolved.Candidate))
                    {
                        throw new ArgumentException(
                            "A selected package decision must retain the resolution receipt's exact coordinate and candidate.",
                            nameof(versionResolution));
                    }
                    if (pruning is not null)
                    {
                        throw new ArgumentException(
                            "Selecting-demand pruning is not part of this PackageHouse contract slice.",
                            nameof(pruning));
                    }
                    return;
                }

                if (versionResolution
                    is PackageVersionResolutionReceipt.Prior prior)
                {
                    if (coordinate != prior.Coordinate
                        || !ReferenceEquals(
                            candidate,
                            prior.Candidate))
                    {
                        throw new ArgumentException(
                            "A prior-settled package decision must retain the prior receipt's exact coordinate and candidate.",
                            nameof(versionResolution));
                    }
                    if (pruning is not null)
                    {
                        throw new ArgumentException(
                            "Selecting-demand pruning is not part of this PackageHouse contract slice.",
                            nameof(pruning));
                    }
                    return;
                }

                if (decision != PackageHouseDecision.Stop
                    || coordinate is not null
                    || candidate is not null
                    || pruning is not null)
                {
                    throw new ArgumentException(
                        "A non-success version resolution can only stop package settlement without an exact coordinate.",
                        nameof(versionResolution));
                }
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(demand));
        }
    }

    internal static InertString RequireReason(InertString reason)
    {
        if (reason.IsEmpty)
        {
            throw new ArgumentException(
                "A PackageHouse non-success requires a visible reason.",
                nameof(reason));
        }

        return reason;
    }
}
