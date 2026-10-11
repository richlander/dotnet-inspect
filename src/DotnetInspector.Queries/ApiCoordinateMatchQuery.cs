using DotnetInspector.Packages;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

public enum ApiCoordinateMatchQueryStage
{
    Acquisition,
    Scope,
    Observation,
    SourceSelection,
    Correspondence,
}

public enum ApiCoordinateMatchEndpointSide
{
    Source,
    Destination,
}

public sealed record ApiCoordinateMatchAcquisitionFailure(
    InertString Producer,
    string Message,
    PackageRootAcquisitionFailureKind FailureKind,
    PackageSourceFailureKind? SourceFailureKind = null);

/// <summary>
/// Host capability for executing one query-issued PackageHouse request,
/// optionally constrained to an already selected producer.
/// </summary>
public interface IApiCoordinateMatchHouseExecutor
{
    Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        PackageProducerIdentity? requiredProducer,
        CancellationToken cancellationToken = default);
}

/// <summary>One PackageHouse operation declaration and its host executor.</summary>
public sealed class ApiCoordinateMatchHouseExecution
{
    public ApiCoordinateMatchHouseExecution(
        PackageHouseOperation operation,
        IApiCoordinateMatchHouseExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(executor);
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "API coordinate matching requires a PackageHouse Realize operation.",
                nameof(operation));
        }

        Operation = operation;
        Executor = executor;
    }

    public PackageHouseOperation Operation { get; }

    public IApiCoordinateMatchHouseExecutor Executor { get; }
}

/// <summary>Native stage evidence for one standalone, direction-preserving match operation.</summary>
public sealed class ApiCoordinateMatchQueryResult
{
    internal ApiCoordinateMatchQueryResult(ApiCoordinateMatchRequest request, ApiCoordinateMatchQueryStage stage)
    {
        Request = request;
        Stage = stage;
    }

    public ApiCoordinateMatchRequest Request { get; }
    public ApiCoordinateMatchQueryStage Stage { get; }
    public ApiCoordinateMatchEndpointSide? FailedEndpoint { get; internal init; }
    public ApiCoordinateMatchAcquisitionFailure? AcquisitionFailure
    {
        get;
        internal init;
    }
    public WorkspaceScopeOperationResult? ScopeFailure { get; internal init; }
    public CoordinateLibraryPairingFailure? ObservationFailure { get; internal init; }
    public CoordinatePackageObservation? Before { get; internal init; }
    public CoordinatePackageObservation? After { get; internal init; }
    public ApiCoordinateSourceSelectionResult? SourceSelection { get; internal init; }
    public ApiCoordinateCorrespondenceEvidence? Correspondence { get; internal init; }
}

/// <summary>
/// Realizes exactly two literal endpoints through PackageHouse, selects the
/// source once, and invokes the shared exact correspondence producer.
/// Hosts that already own observations use ApiCoordinateCorrespondenceQuery directly.
/// </summary>
public static class ApiCoordinateMatchQuery
{
    public static async Task<ApiCoordinateMatchQueryResult> ExecuteAsync(
        ApiCoordinateMatchRequest request,
        ApiCoordinateMatchHouseExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(execution);
        cancellationToken.ThrowIfCancellationRequested();
        PackageSourceCoordinate sourceCoordinate =
            PackageSourceCoordinate.Create(request.PackageId, request.SourceVersion);
        PackageSourceCoordinate destinationCoordinate =
            PackageSourceCoordinate.Create(request.PackageId, request.DestinationVersion);
        EndpointAcquisition source = await AcquireAsync(
            sourceCoordinate,
            requiredProducer: null,
            request,
            execution,
            cancellationToken).ConfigureAwait(false);
        if (source.Failure is { } sourceFailure)
            return AcquisitionFailed(ApiCoordinateMatchEndpointSide.Source, sourceFailure);
        AcquiredEndpoint acquiredSource = source.Acquired
            ?? throw new InvalidOperationException(
                "Successful source acquisition omitted its Package Root.");
        PackageRootBinding beforeBinding = acquiredSource.Binding;

        PackageRootBinding afterBinding;
        if (sourceCoordinate.Equals(destinationCoordinate))
        {
            afterBinding = beforeBinding;
        }
        else
        {
            EndpointAcquisition destination = await AcquireAsync(
                destinationCoordinate,
                acquiredSource.Producer,
                request,
                execution,
                cancellationToken).ConfigureAwait(false);
            if (destination.Failure is { } destinationFailure)
            {
                return AcquisitionFailed(
                    ApiCoordinateMatchEndpointSide.Destination,
                    destinationFailure);
            }
            afterBinding = destination.Acquired?.Binding
                ?? throw new InvalidOperationException(
                    "Successful destination acquisition omitted its Package Root.");
        }

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeReadResult read = await workspace.GetScopeSnapshotAsync()
            .ConfigureAwait(false);
        if (read is not WorkspaceScopeReadResult.Available initial)
            throw new InvalidOperationException("A new Workspace could not expose its initial Scope.");
        WorkspaceScopeOperationResult scope = await workspace.AddPackagesAsync(
            initial.Snapshot.Revision,
            ReferenceEquals(beforeBinding, afterBinding) ? [beforeBinding] : [beforeBinding, afterBinding],
            DateTimeOffset.UtcNow.AddMinutes(5), cancellationToken).ConfigureAwait(false);
        WorkspaceScopeSnapshot? snapshot = scope switch
        {
            WorkspaceScopeOperationResult.Committed committed => committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect unchanged => unchanged.Snapshot,
            _ => null,
        };
        if (snapshot is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(request, ApiCoordinateMatchQueryStage.Scope) { ScopeFailure = scope };
        }

        CoordinatePackageObservationResult beforeResult = await CoordinateLibraryPairingQuery.ObserveAsync(
            workspace, beforeBinding,
            snapshot.FindPackageOccurrence(beforeBinding)
                ?? throw new InvalidOperationException("Committed Scope omitted the source Package."),
            cancellationToken).ConfigureAwait(false);
        if (beforeResult is CoordinatePackageObservationResult.Unavailable beforeFailure)
            return ObservationFailed(ApiCoordinateMatchEndpointSide.Source, beforeFailure.Failure);
        CoordinatePackageObservation before =
            ((CoordinatePackageObservationResult.Available)beforeResult).Observation;
        CoordinatePackageObservationResult afterResult = ReferenceEquals(beforeBinding, afterBinding)
            ? beforeResult
            : await CoordinateLibraryPairingQuery.ObserveAsync(
                workspace, afterBinding,
                snapshot.FindPackageOccurrence(afterBinding)
                    ?? throw new InvalidOperationException("Committed Scope omitted the destination Package."),
                cancellationToken).ConfigureAwait(false);
        if (afterResult is CoordinatePackageObservationResult.Unavailable afterFailure)
            return ObservationFailed(ApiCoordinateMatchEndpointSide.Destination, afterFailure.Failure, before);
        CoordinatePackageObservation after =
            ((CoordinatePackageObservationResult.Available)afterResult).Observation;
        ApiCoordinateSourceSelectionResult selected = await ApiCoordinateSourceSelectionQuery.ExecuteAsync(
            workspace, before, request, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (selected.Status != ApiCoordinateSourceSelectionStatus.Selected)
        {
            return new(request, ApiCoordinateMatchQueryStage.SourceSelection)
            {
                Before = before,
                After = after,
                SourceSelection = selected,
            };
        }
        ApiCoordinateCorrespondenceResult correspondence = selected.Subject switch
        {
            StructuralSubjectIdentity.TypeSubject type =>
                await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                    workspace, type, before, after, cancellationToken).ConfigureAwait(false),
            StructuralSubjectIdentity.MemberSubject member =>
                await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                    workspace, member,
                    ApiDeclarationKindClassifier.FromMemberTarget(selected.MemberKind),
                    before, after, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Source selection did not produce an API subject."),
        };
        return new(request, ApiCoordinateMatchQueryStage.Correspondence)
        {
            Correspondence = correspondence.Detach(),
        };

        ApiCoordinateMatchQueryResult AcquisitionFailed(
            ApiCoordinateMatchEndpointSide endpoint,
            ApiCoordinateMatchAcquisitionFailure failure) =>
            new(request, ApiCoordinateMatchQueryStage.Acquisition)
            {
                FailedEndpoint = endpoint,
                AcquisitionFailure = failure,
            };

        ApiCoordinateMatchQueryResult ObservationFailed(
            ApiCoordinateMatchEndpointSide endpoint, CoordinateLibraryPairingFailure failure,
            CoordinatePackageObservation? before = null) =>
            new(request, ApiCoordinateMatchQueryStage.Observation)
            {
                FailedEndpoint = endpoint,
                ObservationFailure = failure,
                Before = before,
            };
    }

    private static async Task<EndpointAcquisition> AcquireAsync(
        PackageSourceCoordinate coordinate,
        PackageProducerIdentity? requiredProducer,
        ApiCoordinateMatchRequest matchRequest,
        ApiCoordinateMatchHouseExecution execution,
        CancellationToken cancellationToken)
    {
        PackageHouseTargetContext target = matchRequest.TargetFramework is null
            ? PackageHouseTargetContext.OwnerDefault()
            : PackageHouseTargetContext.Exact(matchRequest.TargetFramework);
        var houseRequest = new PackageHouseRequest(
            new PackageHouseDemand.Exact(coordinate),
            execution.Operation,
            target,
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.PackageOnly,
            assetDemand: PackageAssetDemand.SurfaceAndImplementation);
        PackageHouseSettlement settlement =
            await execution.Executor.ExecuteAsync(
                houseRequest,
                requiredProducer,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The API coordinate-match House executor returned no settlement.");
        if (!ReferenceEquals(settlement.Result.Request, houseRequest))
        {
            throw new InvalidOperationException(
                "The API coordinate-match House settlement belongs to another request.");
        }

        if (settlement is not PackageHouseSettlement.Acquired acquired
            || settlement.Result.Evidence.Realization
                is not PackageHouseRealizationReceipt.Compile realization
            || settlement.Result
                is not (PackageHouseResult.Settled
                    or PackageHouseResult.NoMatch
                    or PackageHouseResult.Rejected))
        {
            return new(
                Acquired: null,
                Failure: ProjectFailure(
                    settlement,
                    "PackageHouse did not produce an exact compile realization."));
        }
        if (!PackageRootBinding.TryCreateFromSourceSelection(
                acquired.Payload,
                realization.Receipt,
                out PackageRootBinding? binding))
        {
            return new(
                Acquired: null,
                Failure: new ApiCoordinateMatchAcquisitionFailure(
                    realization.Acquisition.Producer.Display,
                    "PackageHouse produced a compile realization whose exact Package Root coordinate is not representable.",
                    PackageRootAcquisitionFailureKind.InvalidCoordinate));
        }

        return new(
            new AcquiredEndpoint(
                binding.WithAssetDemand(
                    PackageAssetDemand.SurfaceAndImplementation),
                realization.Acquisition.Producer),
            Failure: null);
    }

    private static ApiCoordinateMatchAcquisitionFailure ProjectFailure(
        PackageHouseSettlement settlement,
        string fallback)
    {
        ConfiguredPackagePayloadResult projected =
            PackageHousePayloadResultAdapter.Create(settlement);
        PackageAuthorityFailure? first =
            projected.Failures.FirstOrDefault();
        ConfiguredPackageAuthority? firstNotFound =
            projected.NotFoundAuthorities.FirstOrDefault();
        InertString producer = first?.Authority
            ?? settlement.Result.Evidence.Acquisition?.Producer.Display
            ?? (firstNotFound is null
                ? new InertString(TextPolicy.Field, "PackageHouse")
                : PackageSourceDisplay.ForDiagnostics(
                    firstNotFound.Source));
        string message = projected.Failures.Count == 0
            ? projected.NotFoundAuthorities.Count == 0
                ? fallback
                : "The selected package was not found on "
                    + string.Join(
                        ", ",
                        projected.NotFoundAuthorities.Select(authority =>
                            PackageSourceDisplay.ForDiagnostics(
                                authority.Source)))
                    + "."
            : string.Join(
                "; ",
                projected.Failures.Select(failure => failure.Message));
        PackageRootAcquisitionFailureKind failureKind =
            projected.Failures.Any(failure =>
                failure.IsRequiredProducerUnavailable)
                ? PackageRootAcquisitionFailureKind.ProducerNotAuthorized
                : PackageRootAcquisitionFailureKind.PackageUnavailable;
        return new ApiCoordinateMatchAcquisitionFailure(
            producer,
            message,
            failureKind,
            projected.Failures.Count == 1
                ? first?.SourceFailure?.Kind
                : projected.Failures.Count == 0
                    && projected.NotFoundAuthorities.Count > 0
                    ? PackageSourceFailureKind.NotFound
                    : null);
    }

    private sealed record AcquiredEndpoint(
        PackageRootBinding Binding,
        PackageProducerIdentity Producer);

    private sealed record EndpointAcquisition(
        AcquiredEndpoint? Acquired,
        ApiCoordinateMatchAcquisitionFailure? Failure);
}
