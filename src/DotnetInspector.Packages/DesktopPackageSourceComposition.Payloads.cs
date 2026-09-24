using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

public sealed partial class DesktopPackageSourceComposition
{
    /// <summary>
    /// Acquires a caller-pinned coordinate from its eligible authorities.
    /// The store factory must return a store scoped to the supplied authority.
    /// An external operation remains caller-owned through payload consumption.
    /// </summary>
    public Task<ConfiguredPackagePayloadResult> AcquirePinnedAsync(
        string packageId,
        string version,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        string? requiredProducerKey = null,
        PackageHouseTargetContext? compileTargetContext = null,
        PackagePayloadAccess access = PackagePayloadAccess.Complete,
        PackageAssetDemand assetDemand =
            PackageAssetDemand.SurfaceAndImplementation)
    {
        ArgumentNullException.ThrowIfNull(createStore);
        if (compileTargetContext is not null
            && operationContext is not null)
        {
            throw new ArgumentException(
                "PackageHouse compile realization cannot use a legacy operation context.",
                nameof(operationContext));
        }
        RequireRealizationForRangedAccess(access, compileTargetContext);
        if (operationContext is not null)
        {
            return PackageSourceSettlementCompatibility.RunAsync(
                _sourceLease,
                cancellationToken,
                operationContext,
                (generation, operation) => AcquirePinnedCoreAsync(
                    generation,
                    packageId,
                    version,
                    createStore,
                    sourceOptions,
                    log,
                    operation,
                    limits,
                    transferPolicy,
                    requiredProducerKey),
                _options.RequestTimeout,
                _options.OperationTimeout);
        }

        PackageSourceOperationLease? sourceOperation =
            IssueHouseOperation(cancellationToken);
        try
        {
            if (!PackageExtractor.IsValidPackageId(packageId)
                || !PackageExtractor.TryNormalizePackageVersion(
                    version,
                    out string normalizedVersion))
            {
                return Task.FromResult(
                    InvalidSelection(
                        "Payload acquisition requires a valid package ID and an exact version."));
            }

            Task<ConfiguredPackagePayloadResult> execution =
                AcquirePinnedThroughHouseAsync(
                    PackageSourceCoordinate.Create(
                        packageId,
                        normalizedVersion),
                    createStore,
                    sourceOptions,
                    log,
                    sourceOperation,
                    limits,
                    transferPolicy,
                    requiredProducerKey,
                    compileTargetContext,
                    access,
                    assetDemand);
            sourceOperation = null;
            return execution;
        }
        finally
        {
            sourceOperation?.Dispose();
        }
    }

    private async Task<ConfiguredPackagePayloadResult> AcquirePinnedCoreAsync(
        PackageSourceSettlementGeneration generation,
        string packageId,
        string version,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        NuGetSourceOptions? sourceOptions,
        Action<string>? log,
        NuGetOperationContext operation,
        PackagePayloadLimits? limits,
        IPackagePayloadTransferPolicy? transferPolicy,
        string? requiredProducerKey)
    {
        ArgumentNullException.ThrowIfNull(createStore);
        var failures = new List<PackageAuthorityFailure>();
        if (!PackageExtractor.IsValidPackageId(packageId)
            || !PackageExtractor.TryNormalizePackageVersion(version, out string normalizedVersion))
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty, PackageAuthorityFailureKind.Input,
                "Payload acquisition requires a valid package ID and an exact version."));
            return new(null, null, null, failures);
        }

        CancellationToken cancellationToken = operation.CancellationToken;
        PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(packageId, normalizedVersion);
        PackageAcquisitionCandidateResult resolution =
            ResolvePinnedCandidateCore(
                generation,
                coordinate,
                sourceOptions,
                cancellationToken,
                operation);
        failures.AddRange(resolution.Failures);
        if (resolution.Candidate is not { } candidate)
        {
            if (requiredProducerKey is not null
                && resolution.State
                    == PackageAcquisitionCandidateResultState.Denied)
            {
                failures.Add(RequiredProducerUnavailable());
            }
            return new(null, null, null, failures);
        }
        if (requiredProducerKey is not null)
        {
            ConfiguredPackageAuthority[] matchingAuthorities =
                MatchRequiredProducer(
                    candidate.Authorities.Select(
                        evidence => evidence.Authority),
                    requiredProducerKey,
                    failures);
            if (matchingAuthorities.Length == 0)
                return new(null, null, null, failures);

            candidate = generation.CreatePinnedCandidate(
                coordinate,
                matchingAuthorities);
        }

        return await AcquireCandidateAsync(
            generation,
            candidate,
            createStore,
            log,
            operation,
            limits,
            transferPolicy,
            failures).ConfigureAwait(false);
    }

    private static Task<ConfiguredPackagePayloadResult> AcquireCandidateAsync(
        PackageSourceSettlementGeneration generation,
        PackageAcquisitionCandidate candidate,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        Action<string>? log,
        NuGetOperationContext operation,
        PackagePayloadLimits? limits,
        IPackagePayloadTransferPolicy? transferPolicy,
        List<PackageAuthorityFailure> failures,
        bool selectionUsesOriginalSources = false)
        => generation.AcquireCandidatePayloadAsync(
            candidate,
            createStore,
            log,
            operation,
            limits,
            transferPolicy,
            failures,
            selectionUsesOriginalSources);

    private static PackageAuthorityFailure RequiredProducerUnavailable() =>
        new(
            InertString.Empty,
            PackageAuthorityFailureKind.Configuration,
            "The producer required by the exact package request is not authorized by the configured sources.")
        {
            IsRequiredProducerUnavailable = true,
        };

    private ConfiguredPackageAuthority[] MatchRequiredProducer(
        IEnumerable<ConfiguredPackageAuthority> authorities,
        string requiredProducer,
        List<PackageAuthorityFailure> failures)
    {
        (ConfiguredPackageAuthority Authority, PackageProducerIdentity Producer)[]
            matches =
            [
                .. authorities
                    .Select(authority => (
                        Authority: authority,
                        Producer: GetSourceClient(authority).Source.Producer))
                    .Where(candidate =>
                        MatchesRequiredProducer(
                            candidate.Authority.Source,
                            candidate.Producer,
                            requiredProducer)),
            ];
        if (matches.Length == 0)
        {
            failures.Add(RequiredProducerUnavailable());
            return [];
        }
        if (matches
            .Select(candidate => candidate.Producer.Key)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any())
        {
            failures.Add(
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Configuration,
                    "The producer required by the exact package request matches multiple configured producers.")
                {
                    IsRequiredProducerUnavailable = true,
                });
            return [];
        }

        return [.. matches.Select(candidate => candidate.Authority)];
    }

    private static bool MatchesRequiredProducer(
        PackageSource source,
        PackageProducerIdentity producer,
        string requiredProducer) =>
        producer.PortableKey.Equals(
            requiredProducer,
            StringComparison.Ordinal)
        || producer.Key.Equals(
            requiredProducer,
            StringComparison.Ordinal)
        || NuGetCache.GetSourceKey(source.Url).Equals(
            requiredProducer,
            StringComparison.Ordinal);

    private static ConfiguredPackagePayloadResult PayloadOperationTimedOut(
        NuGetOperationContext operation,
        List<PackageAuthorityFailure> failures)
    {
        failures.Add(new PackageAuthorityFailure(
            InertString.Empty,
            PackageAuthorityFailureKind.Timeout,
            "The package payload operation deadline expired before acquisition completed.")
        {
            Timeout = new(
                PackageSourceTimeoutKind.Operation,
                operation.OperationTimeout),
        });
        return new(null, null, null, failures);
    }
}
