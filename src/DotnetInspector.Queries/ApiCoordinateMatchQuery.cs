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
    public PackageRootPayloadResult.Unavailable? AcquisitionFailure { get; internal init; }
    public WorkspaceScopeOperationResult? ScopeFailure { get; internal init; }
    public CoordinateLibraryPairingFailure? ObservationFailure { get; internal init; }
    public CoordinatePackageObservation? Before { get; internal init; }
    public CoordinatePackageObservation? After { get; internal init; }
    public ApiCoordinateSourceSelectionResult? SourceSelection { get; internal init; }
    public ApiCoordinateCorrespondenceEvidence? Correspondence { get; internal init; }
}

/// <summary>
/// Acquires exactly two literal endpoints through a host capability, selects
/// the source once, and invokes the shared exact correspondence producer.
/// Hosts that already own observations use ApiCoordinateCorrespondenceQuery directly.
/// </summary>
public static class ApiCoordinateMatchQuery
{
    public static async Task<ApiCoordinateMatchQueryResult> ExecuteAsync(
        ApiCoordinateMatchRequest request,
        IPackageRootPayloadProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        PackageSourceCoordinate sourceCoordinate =
            PackageSourceCoordinate.Create(request.PackageId, request.SourceVersion);
        PackageSourceCoordinate destinationCoordinate =
            PackageSourceCoordinate.Create(request.PackageId, request.DestinationVersion);
        PackageRootPayloadResult sourcePayload = await provider.GetPayloadAsync(
            sourceCoordinate, null, PackagePayloadLimits.Default, cancellationToken).ConfigureAwait(false);
        if (sourcePayload is PackageRootPayloadResult.Unavailable sourceFailure)
            return AcquisitionFailed(ApiCoordinateMatchEndpointSide.Source, sourceFailure);
        var source = ((PackageRootPayloadResult.Available)sourcePayload).Payload;
        PackageRootPayloadResult destinationPayload = sourceCoordinate.Equals(destinationCoordinate)
            ? sourcePayload
            : await provider.GetPayloadAsync(
                destinationCoordinate, source.Producer?.PortableKey,
                PackagePayloadLimits.Default, cancellationToken).ConfigureAwait(false);
        if (destinationPayload is PackageRootPayloadResult.Unavailable destinationFailure)
            return AcquisitionFailed(ApiCoordinateMatchEndpointSide.Destination, destinationFailure);
        var destination = ((PackageRootPayloadResult.Available)destinationPayload).Payload;
        if (!source.Coordinate.Equals(sourceCoordinate) || !destination.Coordinate.Equals(destinationCoordinate))
        {
            return AcquisitionFailed(
                !source.Coordinate.Equals(sourceCoordinate)
                    ? ApiCoordinateMatchEndpointSide.Source
                    : ApiCoordinateMatchEndpointSide.Destination,
                new PackageRootPayloadResult.Unavailable(
                    new InertString(TextPolicy.Field, "Package Source"),
                    "Acquisition did not produce the exact requested endpoint.",
                    PackageRootAcquisitionFailureKind.InvalidCoordinate));
        }

        PackageRootBinding beforeBinding = PackageRootBinding.CreateFromSource(source, request.TargetFramework);
        PackageRootBinding afterBinding = ReferenceEquals(source, destination)
            ? beforeBinding
            : PackageRootBinding.CreateFromSource(destination, request.TargetFramework);
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
            ApiCoordinateMatchEndpointSide endpoint, PackageRootPayloadResult.Unavailable failure) =>
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

}
