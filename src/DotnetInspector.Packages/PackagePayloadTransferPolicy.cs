using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One package response a host may reserve before its body is materialized.
/// </summary>
public sealed record PackagePayloadTransfer(
    PackageSourceCoordinate Coordinate,
    string ProducerKey,
    long? AdvertisedLength);

/// <summary>
/// Host policy for reserving package-payload memory or cache capacity after response headers
/// arrive and before the response body is read.
/// </summary>
/// <remarks>
/// <para>
/// The shared acquisition owner still validates transport bytes and archive content. This policy
/// controls only host capacity: a Browser/Wasm host can require <c>Content-Length</c>, evict
/// session entries, and reserve its aggregate memory budget without duplicating download or
/// archive-admission logic.
/// </para>
/// <para>
/// Gated by
/// <c>PackagePayloadAcquisitionTests.TransferPolicy_ReservesBeforeBodyReadAndCompletesAfterCommit</c>,
/// <c>TransferPolicy_AwaitsCapacityBeforeReadingPayload</c>,
/// <c>TransferPolicy_CancellationWhileAwaitingCapacityClosesPayload</c>,
/// <c>TransferPolicy_AsyncRefusalClosesUnreadPayload</c>,
/// <c>TransferPolicy_CancellationAtCapacityHandoffReleasesReservation</c>,
/// <c>TransferPolicy_RejectedPayloadDisposesWithoutCompleting</c>, and
/// <c>TransferPolicy_CanRequireContentLengthBeforeBodyRead</c>.
/// </para>
/// </remarks>
public interface IPackagePayloadTransferPolicy
{
    /// <summary>
    /// Awaits capacity for <paramref name="transfer"/>, or throws a visible host-policy failure
    /// before the body is read. The policy owns any pending capacity work until it returns or
    /// throws, including releasing provisional capacity when canceled.
    /// </summary>
    ValueTask<IPackagePayloadReservation> ReserveAsync(
        PackagePayloadTransfer transfer,
        CancellationToken cancellationToken = default);
}

/// <summary>A host capacity reservation held until validated content is published or abandoned.</summary>
public interface IPackagePayloadReservation : IDisposable
{
    /// <summary>Marks the validated, published payload as the reservation's committed content.</summary>
    void Complete();
}
