using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Supplies a settlement House using this composition's registered source clients.
    /// </summary>
    public PackageHouse CreateSettlementHouse(
        string packageId,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null) =>
        new(
            new SinglePackageAuthorization(
                packageId, AuthorizeSourcesFor(packageId, sourceOptions)),
            log: log,
            versionSettlement: _versionSettlement);

    /// <summary>
    /// Supplies a payload-realizing House whose per-package authorization is
    /// evaluated by this composition.
    /// </summary>
    public PackageHouse CreateDependencySettlementHouse(
        PackageStoreProvider createStore,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(createStore);
        return new PackageHouse(
            new CompositionAuthorization(this, sourceOptions),
            new PackagePayloadAcquisitionPlan(
                createStore,
                log: log),
            log,
            _versionSettlement);
    }

    /// <summary>Issues the source-owned operation consumed by a shared inspection.</summary>
    public PackageSourceOperationLease IssueSettlementOperation(
        CancellationToken cancellationToken = default) =>
        IssueHouseOperation(cancellationToken);

    /// <summary>
    /// Realizes one exact package compile selection with payload authority
    /// retained through the returned settlement.
    /// </summary>
    public ValueTask<PackageHouseSettlement> RealizePinnedCompileAsync(
        PackageSourceCoordinate coordinate,
        string targetFramework,
        PackageStoreProvider createStore,
        NuGetSourceOptions? sourceOptions = null,
        string? requiredProducerKey = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentNullException.ThrowIfNull(createStore);

        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(coordinate),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize,
                _options.RequestTimeout,
                _options.OperationTimeout),
            PackageHouseTargetContext.Exact(targetFramework),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries);
        return new(
            ExecuteHouseAsync(
                request,
                coordinate.PackageId,
                sourceOptions,
                new PackagePayloadAcquisitionPlan(
                    createStore,
                    log: log),
                cancellationToken,
                requiredProducerKey,
                log));
    }

    /// <summary>
    /// Settles one exact coordinate through PackageHouse when the composition
    /// owns the operation lifetime.
    /// </summary>
    public ValueTask<PackageAcquisitionCandidateResult>
        ResolvePinnedCandidateAsync(
            PackageSourceCoordinate coordinate,
            NuGetSourceOptions? sourceOptions = null,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return operationContext is null
            ? new(
                ResolvePinnedCandidateThroughHouseAsync(
                    coordinate,
                    sourceOptions,
                    cancellationToken))
            : ValueTask.FromResult(
                ResolvePinnedCandidate(
                    coordinate,
                    sourceOptions,
                    cancellationToken,
                    operationContext));
    }

    private Task<PackageAcquisitionCandidateResult>
        ResolvePinnedCandidateThroughHouseAsync(
            PackageSourceCoordinate coordinate,
            NuGetSourceOptions? sourceOptions,
            CancellationToken cancellationToken)
    {
        PackageHouseRequest request = CreateHouseRequest(
            new PackageHouseDemand.Exact(coordinate),
            PackageHouseOperationProfile.Settle);
        Task<PackageHouseSettlement> execution =
            ExecuteHouseAsync(
                request,
                coordinate.PackageId,
                sourceOptions,
                payloadAcquisition: null,
                cancellationToken,
                requiredProducerKey: null);
        return ProjectCandidateAsync(execution);
    }

    private static async Task<PackageAcquisitionCandidateResult>
        ProjectCandidateAsync(
            Task<PackageHouseSettlement> execution)
    {
        PackageHouseSettlement settlement =
            await execution.ConfigureAwait(false);
        IReadOnlyList<PackageAuthorityFailure> failures =
            ProjectAuthorityFailures(settlement.Result);
        PackageAcquisitionCandidate? candidate =
            settlement.Result.Decision?.Candidate;
        return new PackageAcquisitionCandidateResult(
            ProjectCandidateState(
                settlement.Result,
                candidate),
            candidate,
            failures);
    }

    private static PackageAcquisitionCandidateResultState
        ProjectCandidateState(
            PackageHouseResult result,
            PackageAcquisitionCandidate? candidate)
    {
        if (candidate is not null)
            return PackageAcquisitionCandidateResultState.Resolved;
        return result is PackageHouseResult.Incomplete
            || HasOperationTimeout(result)
                ? PackageAcquisitionCandidateResultState.Incomplete
                : PackageAcquisitionCandidateResultState.Denied;
    }

    private Task<ConfiguredPackagePayloadResult>
        AcquirePinnedThroughHouseAsync(
            PackageSourceCoordinate coordinate,
            Func<
                ConfiguredPackageAuthority,
                PackageProducerIdentity,
                IPackageStore> createStore,
            NuGetSourceOptions? sourceOptions,
            Action<string>? log,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadLimits? limits,
            IPackagePayloadTransferPolicy? transferPolicy,
            string? requiredProducerKey,
            PackageHouseTargetContext? compileTargetContext = null,
            PackagePayloadAccess access = PackagePayloadAccess.Complete,
            PackageAssetDemand assetDemand =
                PackageAssetDemand.SurfaceAndImplementation)
    {
        PackageHouseRequest request = CreateHouseRequest(
            new PackageHouseDemand.Exact(coordinate),
            compileTargetContext is null
                ? PackageHouseOperationProfile.Acquire
                : PackageHouseOperationProfile.Realize,
            compileTargetContext,
            assetDemand);
        return ExecuteAndProjectPayloadAsync(
            request,
            coordinate.PackageId,
            sourceOptions,
            new PackagePayloadAcquisitionPlan(
                (authority, producer) =>
                    createStore(authority, producer),
                limits,
                transferPolicy,
                log,
                access),
            sourceOperation,
            requiredProducerKey);
    }

    /// <summary>
    /// Ranged access is a Realize-time contract: the realization's selection
    /// bounds the read, so a caller that supplies no compile target has asked
    /// for an unbounded ranged read and is refused before any work starts.
    /// </summary>
    private static void RequireRealizationForRangedAccess(
        PackagePayloadAccess access,
        PackageHouseTargetContext? compileTargetContext)
    {
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access));
        if (access == PackagePayloadAccess.Ranged
            && compileTargetContext is null)
        {
            throw new ArgumentException(
                "Ranged payload access requires PackageHouse compile realization; supply a compile target context.",
                nameof(access));
        }
    }

    private Task<ConfiguredPackagePayloadResult>
        AcquireSelectedThroughHouseAsync(
            PackageVersionSelectionRequest selection,
            Func<
                ConfiguredPackageAuthority,
                PackageProducerIdentity,
                IPackageStore> createStore,
            NuGetSourceOptions? sourceOptions,
            Action<string>? log,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadLimits? limits,
            IPackagePayloadTransferPolicy? transferPolicy,
            PackageHouseTargetContext? compileTargetContext = null,
            PackagePayloadAccess access = PackagePayloadAccess.Complete)
    {
        PackageHouseRequest request = CreateHouseRequest(
            new PackageHouseDemand.Selecting(selection),
            compileTargetContext is null
                ? PackageHouseOperationProfile.Acquire
                : PackageHouseOperationProfile.Realize,
            compileTargetContext);
        return ExecuteAndProjectPayloadAsync(
            request,
            selection.PackageId,
            sourceOptions,
            new PackagePayloadAcquisitionPlan(
                (authority, producer) =>
                    createStore(authority, producer),
                limits,
                transferPolicy,
                log,
                access),
            sourceOperation,
            requiredProducerKey: null);
    }

    private Task<ConfiguredPackagePayloadResult>
        ExecuteAndProjectPayloadAsync(
            PackageHouseRequest request,
            string packageId,
            NuGetSourceOptions? sourceOptions,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            PackageSourceOperationLease sourceOperation,
            string? requiredProducerKey)
    {
        Task<PackageHouseSettlement> execution =
            ExecuteHouseCoreAsync(
                request,
                packageId,
                sourceOptions,
                payloadAcquisition,
                sourceOperation,
                requiredProducerKey);
        return ProjectPayloadAsync(execution);
    }

    private static async Task<ConfiguredPackagePayloadResult>
        ProjectPayloadAsync(
            Task<PackageHouseSettlement> execution)
    {
        PackageHouseSettlement settlement =
            await execution.ConfigureAwait(false);
        ConfiguredPackagePayloadResult? sourceResult =
            settlement.SourcePayloadResult;
        // The House settlement travels whole: an acquired one carries the
        // payload, a terminal one carries the typed evidence (including
        // stage failures such as a prior-settlement eviction) that the
        // authority-failure projection cannot express.
        return new ConfiguredPackagePayloadResult(
            sourceResult?.Authority,
            sourceResult?.Source,
            sourceResult?.Payload,
            ProjectAuthorityFailures(settlement.Result),
            sourceResult?.NotFoundAuthorities,
            sourceResult?.ReportingAuthorities,
            settlement.SelectionUsesOriginalSources,
            settlement);
    }

    private Task<PackageHouseSettlement> ExecuteHouseAsync(
        PackageHouseRequest request,
        string packageId,
        NuGetSourceOptions? sourceOptions,
        PackagePayloadAcquisitionPlan? payloadAcquisition,
        CancellationToken cancellationToken,
        string? requiredProducerKey,
        Action<string>? log = null)
    {
        PackageSourceOperationLease sourceOperation =
            IssueHouseOperation(cancellationToken);
        return ExecuteHouseCoreAsync(
            request,
            packageId,
            sourceOptions,
            payloadAcquisition,
            sourceOperation,
            requiredProducerKey,
            log);
    }

    private PackageSourceOperationLease IssueHouseOperation(
        CancellationToken cancellationToken) =>
        _sourceLease.IssueOperationLease(
            cancellationToken,
            _options.RequestTimeout,
            _options.OperationTimeout);

    private async Task<PackageHouseSettlement> ExecuteHouseCoreAsync(
        PackageHouseRequest request,
        string packageId,
        NuGetSourceOptions? sourceOptions,
        PackagePayloadAcquisitionPlan? payloadAcquisition,
        PackageSourceOperationLease sourceOperation,
        string? requiredProducerKey,
        Action<string>? log = null)
    {
        PackageSourceOperationLease? unsettledOperation =
            sourceOperation;
        try
        {
            var failures = new List<PackageAuthorityFailure>();
            PackageSourceAuthorization authorization =
                AuthorizeSourcesForCore(
                    packageId,
                    sourceOptions,
                    static () => { },
                    failures);
            if (requiredProducerKey is not null)
            {
                ConfiguredPackageAuthority[] matchingAuthorities =
                    MatchRequiredProducer(
                        authorization.Authorities,
                        requiredProducerKey,
                        failures);
                authorization =
                    PackageSourceAuthorization.ObserveAuthorities(
                        matchingAuthorities,
                        failures);
            }

            var house = new PackageHouse(
                new SinglePackageAuthorization(
                    packageId,
                    authorization),
                payloadAcquisition,
                log,
                _versionSettlement);
            Task<PackageHouseSettlement> execution =
                house.ExecuteAsync(
                    request,
                    unsettledOperation);
            unsettledOperation = null;
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            unsettledOperation?.Dispose();
        }
    }

    private PackageHouseRequest CreateHouseRequest(
        PackageHouseDemand demand,
        PackageHouseOperationProfile profile,
        PackageHouseTargetContext? targetContext = null,
        PackageAssetDemand assetDemand =
            PackageAssetDemand.SurfaceAndImplementation) =>
        new(
            demand,
            PackageHouseOperation.Create(
                profile,
                _options.RequestTimeout,
                _options.OperationTimeout),
            targetContext,
            profile == PackageHouseOperationProfile.Realize
                ? PackageHouseAssetSelectionKind.Compile
                : null,
            assetDemand: assetDemand);

    private static bool TryCreateSelectionRequest(
        string packageId,
        string? versionSelector,
        bool includePrerelease,
        string? rangeAddress,
        out PackageVersionSelectionRequest? selection,
        out ConfiguredPackagePayloadResult? failure)
    {
        selection = null;
        failure = null;
        if (!PackageExtractor.IsValidPackageId(packageId))
        {
            failure = InvalidSelection(
                "The package ID must use the NuGet package ID grammar.");
            return false;
        }

        if (versionSelector?.Contains(
                "..",
                StringComparison.Ordinal) == true)
        {
            if (!PackageVersionRange.TryParse(
                    $"{packageId}@{versionSelector}",
                    out PackageVersionRange? range,
                    out string? rangeError))
            {
                failure = InvalidSelection(
                    rangeError
                    ?? "A valid package version range is required.");
                return false;
            }
            if (range!.PackageId != packageId)
            {
                failure = InvalidSelection(
                    "The version selector must contain only the range endpoints.");
                return false;
            }
            if (!IsRangeAddressSyntaxValid(rangeAddress))
            {
                failure = InvalidSelection(
                    "A package range address must be an exact version, #N, first, or last.");
                return false;
            }

            PackageVersionRangeSelection address;
            try
            {
                address =
                    rangeAddress!.Equals(
                        "first",
                        StringComparison.OrdinalIgnoreCase)
                        ? new PackageVersionRangeSelection.First()
                        : rangeAddress.Equals(
                            "last",
                            StringComparison.OrdinalIgnoreCase)
                            ? new PackageVersionRangeSelection.Last()
                            : rangeAddress[0] == '#'
                                ? new PackageVersionRangeSelection.Ordinal(
                                    int.Parse(
                                        rangeAddress.AsSpan(1),
                                        System.Globalization
                                            .CultureInfo.InvariantCulture))
                                : new PackageVersionRangeSelection.Exact(
                                    rangeAddress);
            }
            catch (ArgumentException exception)
            {
                failure = InvalidSelection(exception.Message);
                return false;
            }
            selection = new PackageVersionSelectionRequest.Range(
                range,
                address,
                includePrerelease);
            return true;
        }

        if (rangeAddress is not null)
        {
            failure = InvalidSelection(
                "A range address requires a package version range.");
            return false;
        }
        if (string.IsNullOrEmpty(versionSelector))
        {
            // A bare name declares the Current requirement: a prior
            // settlement inside its window answers it.
            selection = includePrerelease
                ? new PackageVersionSelectionRequest.LatestPrerelease(
                    packageId)
                : new PackageVersionSelectionRequest.LatestStable(
                    packageId);
            return true;
        }
        if (versionSelector.Equals(
                "latest",
                StringComparison.OrdinalIgnoreCase))
        {
            // The explicit spelling is the always-check switch (see
            // docs/design/version-resolution.md, consistency principles).
            selection = new PackageVersionSelectionRequest.AlwaysLatest(
                packageId,
                includePrerelease);
            return true;
        }
        if (versionSelector.Contains('*'))
        {
            try
            {
                selection =
                    new PackageVersionSelectionRequest.Wildcard(
                        packageId,
                        versionSelector.Replace("*", ""));
                return true;
            }
            catch (ArgumentException exception)
            {
                failure = InvalidSelection(exception.Message);
                return false;
            }
        }

        failure = InvalidSelection(
            "Selected payload acquisition requires latest, a wildcard, or a range; use pinned acquisition for an exact version.");
        return false;
    }

    private static IReadOnlyList<PackageAuthorityFailure>
        ProjectAuthorityFailures(
            PackageHouseResult result)
    {
        List<PackageAuthorityFailure> failures =
        [
            .. result.Evidence.Failures
                .OfType<PackageHouseFailure.Authority>()
                .Select(failure => failure.Failure),
        ];
        if (HasOperationTimeout(result)
            && !failures.Any(failure =>
                failure.Timeout?.Kind
                    == PackageSourceTimeoutKind.Operation))
        {
            failures.Add(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Timeout,
                    "The package operation deadline expired before settlement completed.")
                {
                    Timeout = new(
                        PackageSourceTimeoutKind.Operation,
                        result.Request.Operation.OperationTimeout),
                });
        }
        if (result.Request.Demand
                is PackageHouseDemand.Selecting
                {
                    Request:
                        PackageVersionSelectionRequest.Range range,
                }
            && result.Decision?.VersionResolution
                is (PackageVersionResolutionReceipt.NoMatch
                    or PackageVersionResolutionReceipt.NotFound)
                    and PackageVersionResolutionReceipt.Discovered discovered)
        {
            failures.Add(
                ProjectRangeSelectionFailure(
                    range,
                    discovered));
        }

        return failures;
    }

    private static PackageAuthorityFailure ProjectRangeSelectionFailure(
        PackageVersionSelectionRequest.Range range,
        PackageVersionResolutionReceipt.Discovered resolution)
    {
        string message;
        try
        {
            PackageVersionVector vector = PackageVersionVector.Create(
                range.VersionRange,
                resolution.Discovery.Versions,
                range.Discovery.IncludePrerelease);
            string address = range.Selection switch
            {
                PackageVersionRangeSelection.First => "first",
                PackageVersionRangeSelection.Last => "last",
                PackageVersionRangeSelection.Ordinal ordinal =>
                    $"#{ordinal.Value}",
                PackageVersionRangeSelection.Exact exact =>
                    exact.Version,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(range)),
            };
            message = vector.TrySelect(
                    address,
                    out _,
                    out string? error)
                ? resolution switch
                {
                    PackageVersionResolutionReceipt.NoMatch noMatch =>
                        noMatch.Reason.ToString(),
                    PackageVersionResolutionReceipt.NotFound notFound =>
                        notFound.Reason.ToString(),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(resolution)),
                }
                : error!;
        }
        catch (ArgumentException exception)
        {
            message = exception.Message;
        }

        return new PackageAuthorityFailure(
            InertString.Empty,
            PackageAuthorityFailureKind.Input,
            message);
    }

    private static bool HasOperationTimeout(
        PackageHouseResult result) =>
        result.Evidence.Failures.Any(
            failure => failure
                is PackageHouseFailure.Timeout
                {
                    Kind: PackageHouseTimeoutKind.Operation,
                }
                or PackageHouseFailure.Authority
                {
                    Failure.Timeout.Kind:
                        PackageSourceTimeoutKind.Operation,
                });

    private sealed class SinglePackageAuthorization(
        string packageId,
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        private readonly string _packageId =
            packageId.ToLowerInvariant();
        private readonly PackageSourceAuthorization _authorization =
            authorization;

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            if (!packageId.Equals(
                    _packageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The desktop PackageHouse authorization belongs to another package ID.");
            }

            return _authorization;
        }
    }

    private sealed class CompositionAuthorization(
        DesktopPackageSourceComposition composition,
        NuGetSourceOptions? sourceOptions)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            composition.AuthorizeSourcesFor(
                packageId,
                sourceOptions);
    }
}
