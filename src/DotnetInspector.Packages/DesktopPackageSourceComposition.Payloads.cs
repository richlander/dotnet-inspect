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
        string? requiredProducerKey = null) =>
        PackageSourceSettlementCompatibility.RunAsync(
            _sourceAuthorization, cancellationToken, operationContext,
            (generation, operation) => AcquirePinnedCoreAsync(
                generation, packageId, version, createStore, sourceOptions, log,
                operation, limits, transferPolicy, requiredProducerKey),
            _options.RequestTimeout, _options.OperationTimeout);

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
            [
                .. candidate.Authorities
                    .Select(evidence => evidence.Authority)
                    .Where(authority =>
                        _authoritiesByAssociation.TryGetValue(
                            authority.Association,
                            out AuthorityEntry? entry)
                        && entry.Client.Source.Producer.Key.Equals(
                            requiredProducerKey,
                            StringComparison.Ordinal)),
            ];
            if (matchingAuthorities.Length == 0)
            {
                failures.Add(RequiredProducerUnavailable());
                return new(null, null, null, failures);
            }

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
