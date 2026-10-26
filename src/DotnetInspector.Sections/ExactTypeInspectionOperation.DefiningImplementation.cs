using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    /// <summary>
    /// Resolves ordered exact package Type candidates over aggregate compile
    /// surface, then lends the first available Type from only its defining
    /// implementation assembly.
    /// </summary>
    public static async Task<TResult>
        ExecuteFirstAvailableWithDefiningImplementationTargetAsync<TResult>(
            IReadOnlyList<ExactTypeInspectionRequest> requests,
            PackageHouse house,
            Func<CancellationToken, PackageSourceOperationLease>
                issueSourceOperation,
            ApiSurfaceScope scope,
            Func<
                InspectionEnvelope<ExactTypeInspectionResult>,
                SelectedContextExactTypeLiveTarget?,
                CancellationToken,
                ValueTask<TResult>> use,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(issueSourceOperation);
        ArgumentNullException.ThrowIfNull(use);

        ExactTypeInspectionRequest first = ValidateCandidateRequests(requests);
        PackageSourceOperationLease surfaceOperation =
            IssueSourceOperation(issueSourceOperation, cancellationToken);
        DefiningImplementationDiscovery discovery =
            await ExecuteFirstAvailableWithLiveTargetAsync(
                    requests,
                    house,
                    surfaceOperation,
                    scope,
                    (envelope, target, _) =>
                        ValueTask.FromResult(
                            DefiningImplementationDiscovery.Create(
                                requests,
                                envelope,
                                target)),
                    cancellationToken)
                .ConfigureAwait(false);

        if (!discovery.Envelope.Content.IsAvailable
            || discovery.Request is null)
        {
            return await use(
                    discovery.Envelope,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        if (discovery.SurfaceAssetPath is null)
        {
            return await use(
                    WithPriorDiagnostics(
                        Envelope(
                            ExactTypeInspectionResult.ContextUnavailable(
                                discovery.Request,
                                "The defining package surface did not publish "
                                    + "an asset path."),
                            discovery.Request),
                        discovery.Envelope.Diagnostics),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string implementationName =
            Path.GetFileName(discovery.SurfaceAssetPath);
        PackageEndpointScopeRequest endpointRequest =
            PackageEndpointScopeRequest.ForCompileLibraries(
                PackageSourceCoordinate.Create(
                    first.PackageId,
                    first.Version),
                first.TargetFramework,
                PackageAssetDemand.SurfaceAndImplementation,
                implementationNames: [implementationName]);

        PackageSourceOperationLease implementationOperation =
            IssueSourceOperation(issueSourceOperation, cancellationToken);
        PackageEndpointScopeOutcome opened =
            await PackageEndpointScope.OpenAsync(
                    house,
                    implementationOperation,
                    endpointRequest,
                    cancellationToken)
                .ConfigureAwait(false);
        if (opened is PackageEndpointScopeOutcome.Failed failed)
        {
            return await use(
                    WithPriorDiagnostics(
                        Envelope(
                            ExactTypeInspectionResult.ContextUnavailable(
                                discovery.Request,
                                $"{failed.Kind}: {failed.Message}"),
                            discovery.Request),
                        discovery.Envelope.Diagnostics),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await using PackageEndpointScope endpoint =
            ((PackageEndpointScopeOutcome.Opened)opened).Scope;
        PackageEndpointParticipant? implementation =
            endpoint.ImplementationParticipants.SingleOrDefault(
                participant => participant.SurfaceCorrespondence?.Provenance
                    .AssetPath.Equals(
                        discovery.SurfaceAssetPath,
                        StringComparison.OrdinalIgnoreCase) == true);
        if (implementation is null)
        {
            return await use(
                    WithPriorDiagnostics(
                        Envelope(
                            ExactTypeInspectionResult.ContextUnavailable(
                                discovery.Request,
                                "The defining package surface has no "
                                    + "corresponding implementation asset."),
                            discovery.Request),
                        discovery.Envelope.Diagnostics),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        ArtifactRootResult<TResult> execution =
            await endpoint.UsePackageAssemblyRolesAsync(
                    async (workspace, package, realization, token) =>
                    {
                        if (realization.ImplementationGroup is null
                            || implementation.Index
                                >= realization.ImplementationParticipants.Length)
                        {
                            return await use(
                                    WithPriorDiagnostics(
                                        Envelope(
                                            ExactTypeInspectionResult
                                                .ContextUnavailable(
                                                    discovery.Request,
                                                    "The defining "
                                                        + "implementation "
                                                        + "participant is "
                                                        + "unavailable."),
                                            discovery.Request),
                                        discovery.Envelope.Diagnostics),
                                    null,
                                    token)
                                .ConfigureAwait(false);
                        }

                        PackageAssemblyRoleParticipant role =
                            realization.ImplementationParticipants[
                                implementation.Index];
                        FirstAvailableExactTypeInspectionExecution retained =
                            ExecuteFirstAvailableExecutionCore(
                                workspace,
                                Loaded(
                                    workspace.Identity,
                                    realization.ImplementationGroup,
                                    [role.Participant],
                                    package,
                                    discovery.Request),
                                [discovery.Request],
                                scope,
                                projectionLimits: null);
                        InspectionEnvelope<ExactTypeInspectionResult> envelope =
                            WithPriorDiagnostics(
                                Envelope(
                                    MergePriorResult(
                                        retained.Execution.Result,
                                        discovery.Envelope.Content),
                                    retained.Request),
                                discovery.Envelope.Diagnostics);
                        return await use(
                                envelope,
                                LiveTarget(retained.Execution.Target),
                                token)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        return execution switch
        {
            ArtifactRootResult<TResult>.Available available =>
                available.Value,
            ArtifactRootResult<TResult>.Rejected rejected =>
                await use(
                        WithPriorDiagnostics(
                            Envelope(
                                ExactTypeInspectionResult.ContextUnavailable(
                                    discovery.Request,
                                    "The admitted package Root rejected "
                                        + "defining implementation "
                                        + $"inspection: {rejected.Failure}"),
                                discovery.Request),
                            discovery.Envelope.Diagnostics),
                        null,
                        cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Package endpoint execution returned an unknown result."),
        };
    }

    static PackageSourceOperationLease IssueSourceOperation(
        Func<CancellationToken, PackageSourceOperationLease>
            issueSourceOperation,
        CancellationToken cancellationToken) =>
        issueSourceOperation(cancellationToken)
        ?? throw new InvalidOperationException(
            "The Package Source operation issuer returned null.");

    static InspectionEnvelope<ExactTypeInspectionResult> WithPriorDiagnostics(
        InspectionEnvelope<ExactTypeInspectionResult> envelope,
        ImmutableArray<InspectionDiagnostic> priorDiagnostics) =>
        priorDiagnostics.IsEmpty
            ? envelope
            : new(
                envelope.Content,
                envelope.Share,
                priorDiagnostics
                    .Concat(envelope.Diagnostics)
                    .Distinct());

    static ExactTypeInspectionResult MergePriorResult(
        ExactTypeInspectionResult result,
        ExactTypeInspectionResult prior) =>
        result with
        {
            InspectionFailures =
            [
                .. prior.InspectionFailures,
                .. result.InspectionFailures,
            ],
            Failures =
            [
                .. prior.Failures,
                .. result.Failures,
            ],
        };

    sealed record DefiningImplementationDiscovery(
        InspectionEnvelope<ExactTypeInspectionResult> Envelope,
        ExactTypeInspectionRequest? Request,
        string? SurfaceAssetPath)
    {
        internal static DefiningImplementationDiscovery Create(
            IReadOnlyList<ExactTypeInspectionRequest> requests,
            InspectionEnvelope<ExactTypeInspectionResult> envelope,
            SelectedContextExactTypeLiveTarget? target)
        {
            ExactTypeInspectionRequest? request =
                envelope.Content.IsAvailable
                    ? requests.First(candidate =>
                        candidate.Type.Equals(
                            envelope.Content.RequestedType,
                            StringComparison.Ordinal))
                    : null;
            return new(
                envelope,
                request,
                target?.PackageAssetPath);
        }
    }
}
