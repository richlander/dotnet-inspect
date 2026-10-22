using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Supplies per-package authorization for dependency operations that use
    /// this composition's configured source policy.
    /// </summary>
    public IPackageSourceAuthorization CreateDependencySourceAuthorization(
        NuGetSourceOptions? sourceOptions = null) =>
        new CompositionAuthorization(
            this,
            sourceOptions,
            requiredProducerKey: null);

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
        return CreateRealizationHouse(
            new PackagePayloadAcquisitionPlan(
                createStore,
                log: log),
            sourceOptions,
            log);
    }

    /// <summary>
    /// Supplies a semantic-query House whose per-package authorization is
    /// evaluated by this composition.
    /// </summary>
    public PackageHouse CreateDependencyContentQueryHouse(
        PackageStoreProvider createStore,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(createStore);
        return CreateRealizationHouse(
            PackagePayloadAcquisitionPlan.ForContentQueries(
                createStore,
                log: log),
            sourceOptions,
            log);
    }

    /// <summary>
    /// Supplies a payload-realizing House under a host-chosen acquisition
    /// plan (access, store, and limits), with per-package authorization
    /// evaluated by this composition. Pair it with
    /// <see cref="IssueSettlementOperation"/>.
    /// </summary>
    public PackageHouse CreateRealizationHouse(
        PackagePayloadAcquisitionPlan payloadAcquisition,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        string? requiredProducerKey = null)
    {
        ArgumentNullException.ThrowIfNull(payloadAcquisition);
        return new PackageHouse(
            new CompositionAuthorization(
                this,
                sourceOptions,
                requiredProducerKey),
            payloadAcquisition,
            log,
            _versionSettlement);
    }

    /// <summary>
    /// Creates a House operation declaration with this composition's exact
    /// request and operation deadlines.
    /// </summary>
    public PackageHouseOperation CreateHouseOperation(
        PackageHouseOperationProfile profile) =>
        PackageHouseOperation.Create(
            profile,
            _options.RequestTimeout,
            _options.OperationTimeout);

    /// <summary>Issues the source-owned operation consumed by a shared inspection.</summary>
    public PackageSourceOperationLease IssueSettlementOperation(
        CancellationToken cancellationToken = default) =>
        IssueHouseOperation(cancellationToken);

    /// <summary>
    /// Issues a source-owned operation capped by an enclosing House duration.
    /// </summary>
    public PackageSourceOperationLease IssueSettlementOperation(
        TimeSpan maximumOperationTimeout,
        CancellationToken cancellationToken)
    {
        if (maximumOperationTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOperationTimeout));
        }

        TimeSpan operationTimeout = TimeSpan.FromTicks(
            Math.Min(
                _options.OperationTimeout.Ticks,
                maximumOperationTimeout.Ticks));
        TimeSpan requestTimeout = TimeSpan.FromTicks(
            Math.Min(
                _options.RequestTimeout.Ticks,
                operationTimeout.Ticks));
        return _sourceLease.IssueOperationLease(
            cancellationToken,
            requestTimeout,
            operationTimeout);
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
                PackageAssetDemand.SurfaceAndImplementation,
            IEnumerable<string>? implementationNames = null,
            PackageFileDemand? fileDemand = null,
            PackageHouseLibraryHandoffMode libraryHandoff =
                PackageHouseLibraryHandoffMode.PackageOnly,
            PackageHouseLibraryCompanionDemand libraryCompanionDemand =
                PackageHouseLibraryCompanionDemand.None,
            PackageHouseAssetSelectionKind assetSelection =
                PackageHouseAssetSelectionKind.Compile)
    {
        PackageHouseRequest request = CreateHouseRequest(
            new PackageHouseDemand.Exact(coordinate),
            compileTargetContext is null
                ? PackageHouseOperationProfile.Acquire
                : PackageHouseOperationProfile.Realize,
            compileTargetContext,
            assetSelection,
            assetDemand,
            implementationNames,
            fileDemand,
            libraryHandoff,
            libraryCompanionDemand);
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
    /// Ranged access must be bounded: an asset realization's selection or a
    /// file demand bounds the read, so a caller that supplies neither has
    /// asked for an unbounded ranged read and is refused before any work
    /// starts (docs/design/package-read-demand.md#exact-file-demand).
    /// </summary>
    private static void RequireRealizationForRangedAccess(
        PackagePayloadAccess access,
        PackageHouseTargetContext? compileTargetContext,
        PackageFileDemand? fileDemand = null)
    {
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access));
        if (access == PackagePayloadAccess.Ranged
            && compileTargetContext is null
            && fileDemand is null)
        {
            throw new ArgumentException(
                "Ranged payload access requires PackageHouse asset realization or a file demand; supply a target context or a file demand.",
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
            PackagePayloadAccess access = PackagePayloadAccess.Complete,
            PackageAssetDemand assetDemand =
                PackageAssetDemand.SurfaceAndImplementation,
            IEnumerable<string>? implementationNames = null,
            PackageHouseLibraryHandoffMode libraryHandoff =
                PackageHouseLibraryHandoffMode.PackageOnly,
            PackageHouseLibraryCompanionDemand libraryCompanionDemand =
                PackageHouseLibraryCompanionDemand.None,
            PackageHouseAssetSelectionKind assetSelection =
                PackageHouseAssetSelectionKind.Compile)
    {
        PackageHouseRequest request = CreateHouseRequest(
            new PackageHouseDemand.Selecting(selection),
            compileTargetContext is null
                ? PackageHouseOperationProfile.Acquire
                : PackageHouseOperationProfile.Realize,
            compileTargetContext,
            assetSelection,
            assetDemand,
            implementationNames,
            fileDemand: null,
            libraryHandoff,
            libraryCompanionDemand);
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
        return PackageHousePayloadResultAdapter.Create(settlement);
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
            IPackageSourceAuthorization authorization =
                AuthorizeHouseSources(
                    packageId,
                    sourceOptions,
                    requiredProducerKey);

            var house = new PackageHouse(
                authorization,
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

    private IPackageSourceAuthorization AuthorizeHouseSources(
        string packageId,
        NuGetSourceOptions? sourceOptions,
        string? requiredProducerKey)
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

        return new SinglePackageAuthorization(
            packageId,
            authorization);
    }

    private PackageHouseRequest CreateHouseRequest(
        PackageHouseDemand demand,
        PackageHouseOperationProfile profile,
        PackageHouseTargetContext? targetContext = null,
        PackageHouseAssetSelectionKind assetSelection =
            PackageHouseAssetSelectionKind.Compile,
        PackageAssetDemand assetDemand =
            PackageAssetDemand.SurfaceAndImplementation,
        IEnumerable<string>? implementationNames = null,
        PackageFileDemand? fileDemand = null,
        PackageHouseLibraryHandoffMode libraryHandoff =
            PackageHouseLibraryHandoffMode.PackageOnly,
        PackageHouseLibraryCompanionDemand libraryCompanionDemand =
            PackageHouseLibraryCompanionDemand.None) =>
        new(
            demand,
            PackageHouseOperation.Create(
                profile,
                _options.RequestTimeout,
                _options.OperationTimeout),
            targetContext,
            profile == PackageHouseOperationProfile.Realize
                ? assetSelection
                : null,
            libraryHandoff,
            assetDemand: assetDemand,
            implementationNames: implementationNames,
            fileDemand: fileDemand,
            libraryCompanionDemand: libraryCompanionDemand);

    internal static bool TryCreateSelectionRequest(
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
        NuGetSourceOptions? sourceOptions,
        string? requiredProducerKey)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            requiredProducerKey is null
                ? composition.AuthorizeSourcesFor(
                    packageId,
                    sourceOptions)
                : composition.AuthorizeHouseSources(
                        packageId,
                        sourceOptions,
                        requiredProducerKey)
                    .AuthorizeSourcesFor(packageId);
    }
}
