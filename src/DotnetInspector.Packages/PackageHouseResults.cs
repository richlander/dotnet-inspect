using System.Collections.Immutable;
using DotnetInspector.Platforms;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>Opaque identity associating one terminal PackageHouse result.</summary>
public sealed class PackageHouseSettlementIdentity
{
    internal PackageHouseSettlementIdentity()
    {
    }

    public override string ToString() => nameof(PackageHouseSettlementIdentity);
}

/// <summary>
/// Package-owned evidence binding one policy-issued platform-supply receipt to
/// the exact House request, package coordinate, and platform target.
/// </summary>
public sealed class PackageHousePruningReceipt
{
    internal PackageHousePruningReceipt(
        PackageHouseRequest request,
        PlatformFamilyTarget target,
        PlatformSupplyReceipt policy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(policy);
        if (request.Demand is not PackageHouseDemand.Exact exact)
        {
            throw new ArgumentException(
                "This PackageHouse contract floor prunes only exact package demands.",
                nameof(request));
        }
        if (request.TargetContext?.PlatformTarget != target)
        {
            throw new ArgumentException(
                "A pruning receipt must use the request's exact platform correspondence.",
                nameof(target));
        }
        if (!PolicyCoordinateMatches(
                policy.Coordinate,
                exact.Coordinate,
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
        Coordinate = exact.Coordinate;
        Target = target;
        Policy = policy;
    }

    public PackageHouseRequest Request { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PlatformFamilyTarget Target { get; }

    public PlatformSupplyReceipt Policy { get; }

    public PlatformSupply Supply => Policy.Supply;

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
        PackageHousePruningReceipt? pruning)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(nameof(decision));
        if (coordinate is not null)
        {
            PackageHouseContractValidation.RequireCoordinateMatchesDemand(
                request.Demand,
                coordinate);
        }
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
    }

    public PackageHouseRequest Request { get; }

    public PackageHouseDecision Decision { get; }

    public PackageSourceCoordinate? Coordinate { get; }

    public PackageAcquisitionCandidate? Candidate { get; }

    public PackageHousePruningReceipt? Pruning { get; }

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
            pruning);

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
            pruning);

    internal static PackageHouseDecisionReceipt Stop(
        PackageHouseRequest request,
        PackageSourceCoordinate? coordinate = null,
        PackageAcquisitionCandidate? candidate = null,
        PackageHousePruningReceipt? pruning = null) =>
        new(
            request,
            PackageHouseDecision.Stop,
            coordinate,
            candidate,
            pruning);
}

/// <summary>
/// Resource-free evidence binding an authorized payload generation to its
/// decision, candidate, authority, source result, producer, and origin.
/// </summary>
public sealed class PackageHouseAcquisitionReceipt
{
    internal PackageHouseAcquisitionReceipt(
        PackageHouseDecisionReceipt decision,
        PackageAcquisitionCandidate candidate,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity source,
        PackagePayloadOrigin origin,
        PackageContentGenerationIdentity generation)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(candidate);
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
            || !ReferenceEquals(decision.Candidate, candidate)
            || decision.Coordinate != candidate.Coordinate)
        {
            throw new ArgumentException(
                "Acquisition requires the decision's exact retained candidate.",
                nameof(candidate));
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
        Candidate = candidate;
        Authority = authority;
        Source = source;
        Origin = origin;
        Generation = generation;
    }

    public PackageHouseDecisionReceipt Decision { get; }

    public PackageAcquisitionCandidate Candidate { get; }

    public ConfiguredPackageAuthority Authority { get; }

    public PackageSourceResultIdentity Source { get; }

    public PackageProducerIdentity Producer => Source.Producer;

    public PackagePayloadOrigin Origin { get; }

    public PackageContentGenerationIdentity Generation { get; }
}

/// <summary>
/// Resource-free evidence binding one existing package-owner asset-selection
/// outcome to the exact acquisition and request that produced it.
/// </summary>
public abstract class PackageHouseAssetSelectionReceipt
{
    private PackageHouseAssetSelectionReceipt(
        PackageHouseAcquisitionReceipt acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (acquisition.Decision.Request.Operation.Profile
            != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Only a Realize operation can produce asset-selection evidence.",
                nameof(acquisition));
        }

        Acquisition = acquisition;
    }

    public PackageHouseAcquisitionReceipt Acquisition { get; }

    public sealed class Compile : PackageHouseAssetSelectionReceipt
    {
        internal Compile(
            PackageHouseAcquisitionReceipt acquisition,
            PackageCompileAssetSelectionReceipt receipt)
            : base(acquisition)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            if (acquisition.Decision.Request.AssetSelection
                != PackageHouseAssetSelectionKind.Compile)
            {
                throw new ArgumentException(
                    "A compile selection requires a compile realization request.",
                    nameof(acquisition));
            }
            PackageHouseRequest request = acquisition.Decision.Request;
            if (!ReferenceEquals(
                    receipt.Generation,
                    acquisition.Generation)
                || !receipt.PackageId.Equals(
                    acquisition.Decision.Coordinate!.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                || !RequestMatches(
                    request.TargetContext,
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

            Receipt = receipt;
        }

        public PackageCompileAssetSelectionReceipt Receipt { get; }

        public PackageCompileAssetSelection Selection => Receipt.Selection;
    }

    public sealed class Runtime : PackageHouseAssetSelectionReceipt
    {
        internal Runtime(
            PackageHouseAcquisitionReceipt acquisition,
            PackageAssetSelectionReceipt receipt)
            : base(acquisition)
        {
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

            Receipt = receipt;
        }

        public PackageAssetSelectionReceipt Receipt { get; }

        public PackageAssetSelection Selection => Receipt.Selection;
    }

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

/// <summary>Opaque identity for one library handoff occurrence.</summary>
public sealed class PackageHouseLibraryHandoffIdentity
{
    internal PackageHouseLibraryHandoffIdentity()
    {
    }

    public override string ToString() =>
        nameof(PackageHouseLibraryHandoffIdentity);
}

/// <summary>
/// Resource-free package-to-library evidence retaining the complete
/// acquisition and selection chain.
/// </summary>
public abstract class PackageHouseLibraryHandoff
{
    private PackageHouseLibraryHandoff(
        PackageHouseAssetSelectionReceipt selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Identity = new PackageHouseLibraryHandoffIdentity();
        Selection = selection;
    }

    public PackageHouseLibraryHandoffIdentity Identity { get; }

    public PackageHouseAssetSelectionReceipt Selection { get; }

    public PackageHouseAcquisitionReceipt Acquisition =>
        Selection.Acquisition;

    public PackageHouseDecisionReceipt Decision => Acquisition.Decision;

    public PackageSourceCoordinate Coordinate => Decision.Coordinate!;

    public PackageHouseTargetContext? TargetContext =>
        Decision.Request.TargetContext;

    public sealed class Compile : PackageHouseLibraryHandoff
    {
        internal Compile(
            PackageHouseAssetSelectionReceipt.Compile selection,
            PackageCompileAsset asset,
            PackageCompileAsset? implementationAsset)
            : base(selection)
        {
            ArgumentNullException.ThrowIfNull(asset);
            Asset = asset;
            ImplementationAsset = implementationAsset;
        }

        public PackageCompileAsset Asset { get; }

        public PackageCompileAsset? ImplementationAsset { get; }
    }

    public sealed class Runtime : PackageHouseLibraryHandoff
    {
        internal Runtime(
            PackageHouseAssetSelectionReceipt.Runtime selection,
            PackageAssetEntry asset)
            : base(selection)
        {
            ArgumentNullException.ThrowIfNull(asset);
            Asset = asset;
        }

        public PackageAssetEntry Asset { get; }
    }
}

/// <summary>One package materialization plus typed asset-selection evidence.</summary>
public sealed class PackageHouseRealizationReceipt
{
    internal PackageHouseRealizationReceipt(
        PackageHouseAssetSelectionReceipt selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Selection = selection;
        Completion = GetCompletion(selection);
        LibraryHandoffs = CreateLibraryHandoffs(selection);
    }

    public PackageHouseAssetSelectionReceipt Selection { get; }

    public PackageHouseAcquisitionReceipt Acquisition =>
        Selection.Acquisition;

    internal PackageHouseRealizationCompletion Completion { get; }

    public ImmutableArray<PackageHouseLibraryHandoff> LibraryHandoffs { get; }

    private static PackageHouseRealizationCompletion GetCompletion(
        PackageHouseAssetSelectionReceipt selection) =>
        selection switch
        {
            PackageHouseAssetSelectionReceipt.Compile
                {
                    Selection.Status:
                        PackageCompileAssetSelectionStatus.Selected
                        or PackageCompileAssetSelectionStatus.EmptyCompileGroup,
                } => PackageHouseRealizationCompletion.Settled,
            PackageHouseAssetSelectionReceipt.Compile
                {
                    Selection.Status:
                        PackageCompileAssetSelectionStatus.NoCompileAssets
                        or PackageCompileAssetSelectionStatus
                            .NoMatchingTargetFramework,
                } => PackageHouseRealizationCompletion.NoMatch,
            PackageHouseAssetSelectionReceipt.Compile =>
                PackageHouseRealizationCompletion.Rejected,
            PackageHouseAssetSelectionReceipt.Runtime
                {
                    Selection: PackageAssetSelection.Selected,
                } => PackageHouseRealizationCompletion.Settled,
            PackageHouseAssetSelectionReceipt.Runtime
                {
                    Selection: PackageAssetSelection.NoMatch,
                } => PackageHouseRealizationCompletion.NoMatch,
            PackageHouseAssetSelectionReceipt.Runtime
                {
                    Selection: PackageAssetSelection.Ambiguous,
                } => PackageHouseRealizationCompletion.Ambiguous,
            PackageHouseAssetSelectionReceipt.Runtime =>
                PackageHouseRealizationCompletion.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };

    private static ImmutableArray<PackageHouseLibraryHandoff>
        CreateLibraryHandoffs(PackageHouseAssetSelectionReceipt selection)
    {
        if (selection.Acquisition.Decision.Request.LibraryHandoff
            == PackageHouseLibraryHandoffMode.PackageOnly)
        {
            return [];
        }

        return selection switch
        {
            PackageHouseAssetSelectionReceipt.Compile compile
                when compile.Selection.IsSelected =>
            [
                .. compile.Selection.Assets.Select(asset =>
                    new PackageHouseLibraryHandoff.Compile(
                        compile,
                        asset,
                        compile.Selection.FindImplementationAsset(asset))),
            ],
            PackageHouseAssetSelectionReceipt.Runtime
                {
                    Selection: PackageAssetSelection.Selected selected,
                } runtime =>
            [
                .. selected.Universe.Assets.Select(asset =>
                    new PackageHouseLibraryHandoff.Runtime(runtime, asset)),
            ],
            _ => [],
        };
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
        Identity = new PackageHouseSettlementIdentity();
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
        foreach (PackageHouseFailure.Timeout timeout
            in Failures.OfType<PackageHouseFailure.Timeout>())
        {
            TimeSpan expected = timeout.Kind switch
            {
                PackageHouseTimeoutKind.Request =>
                    request.Operation.RequestTimeout,
                PackageHouseTimeoutKind.Operation =>
                    request.Operation.OperationTimeout,
                _ => throw new ArgumentOutOfRangeException(nameof(failures)),
            };
            if (!ReferenceEquals(
                    timeout.Operation,
                    request.Operation.Identity)
                || timeout.Duration != expected)
            {
                throw new ArgumentException(
                    "A retained timeout must match the request's operation identity and configured duration.",
                    nameof(failures));
            }
        }
        foreach (PackageHouseFailure.Authority authority
            in Failures.OfType<PackageHouseFailure.Authority>())
        {
            if (!ReferenceEquals(
                        authority.Operation,
                        request.Operation.Identity))
            {
                    throw new ArgumentException(
                        "A retained authority failure must match the request's operation identity.",
                        nameof(failures));
            }
            if (authority.Failure.Timeout is { } timeout)
            {
                    TimeSpan expected = timeout.Kind switch
                    {
                        PackageSourceTimeoutKind.Request
                            or PackageSourceTimeoutKind.MetadataBody =>
                            request.Operation.RequestTimeout,
                        PackageSourceTimeoutKind.Operation =>
                            request.Operation.OperationTimeout,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(failures)),
                    };
                    if (timeout.Duration != expected)
                    {
                        throw new ArgumentException(
                            "A retained authority timeout must match the request's configured duration.",
                            nameof(failures));
                    }
            }
        }
    }

    public PackageHouseRequest Request { get; }

    public PackageHouseSettlementIdentity Identity { get; }

    public PackageHouseDecisionReceipt? Decision { get; }

    public PackageHouseAcquisitionReceipt? Acquisition { get; }

    public PackageHouseRealizationReceipt? Realization { get; }

    public ImmutableArray<PackageHouseFailure> Failures { get; }

    internal bool HasOperationTimeout =>
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
                { Supply.DelegatesToPlatform: true } pruning)
        {
            throw new ArgumentException(
                "Platform delegation requires one complete PackageHouse pruning decision.",
                nameof(decision));
        }

        Decision = decision;
        Coordinate = decision.Coordinate;
        Pruning = pruning;
    }

    public PackageHouseDecisionReceipt Decision { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageHousePruningReceipt Pruning { get; }

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
}

internal static class PackageHouseContractValidation
{
    internal static void RequireCoordinateMatchesDemand(
        PackageHouseDemand demand,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(coordinate);
        if (demand is not PackageHouseDemand.Exact exact
            || exact.Coordinate != coordinate)
        {
            throw new ArgumentException(
                "The settled coordinate must be the exact package demand.",
                nameof(coordinate));
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
