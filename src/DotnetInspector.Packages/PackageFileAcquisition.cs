using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Stable host capability and policy for acquiring one exact package file.
/// </summary>
public sealed class PackageFileAcquisitionPlan
{
    internal PackageFileAcquisitionPlan(
        PackagePayloadAcquisitionPlan payloadAcquisition)
    {
        PayloadAcquisition = payloadAcquisition
            ?? throw new ArgumentNullException(nameof(payloadAcquisition));
    }

    public PackageFileAcquisitionPlan(
        PackageStoreProvider getStore,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        Action<string>? log = null,
        long rangedSizeCut = PackageRangedRead.DefaultSizeCut)
        : this(
            PackagePayloadAcquisitionPlan.ForContentQueries(
                getStore,
                limits,
                transferPolicy,
                log,
                rangedSizeCut))
    {
    }

    internal PackagePayloadAcquisitionPlan PayloadAcquisition { get; }
}

/// <summary>
/// One exact package coordinate and relative file path to acquire.
/// </summary>
public sealed class PackageFileAcquisitionRequest
{
    public PackageFileAcquisitionRequest(
        PackageSourceCoordinate coordinate,
        string path,
        PackageHouseOperation operation)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Profile != PackageHouseOperationProfile.Acquire)
        {
            throw new ArgumentException(
                "Exact package-file acquisition requires an Acquire operation.",
                nameof(operation));
        }
        Coordinate = coordinate;
        ContentQuery = PackageHouseContentQuery.PackageFiles([path]);
        Path = ContentQuery.FilesTerminal!.Entries.Single();
        Operation = operation;
    }

    public PackageSourceCoordinate Coordinate { get; }

    public string Path { get; }

    public PackageHouseOperation Operation { get; }

    internal PackageHouseContentQuery ContentQuery { get; }
}

public enum PackageFileAcquisitionStatus
{
    Acquired,
    NotSettled,
    ManifestUnavailable,
    Missing,
    Ambiguous,
}

/// <summary>
/// The typed outcome of one exact package-file acquisition.
/// </summary>
public abstract class PackageFileAcquisitionResult
{
    private PackageFileAcquisitionResult(
        PackageFileAcquisitionStatus status,
        PackageHouseSettlement settlement)
    {
        Status = status;
        Settlement = settlement
            ?? throw new ArgumentNullException(nameof(settlement));
    }

    public PackageFileAcquisitionStatus Status { get; }

    public PackageHouseSettlement Settlement { get; }

    public sealed class Acquired : PackageFileAcquisitionResult
    {
        internal Acquired(
            PackageHouseSettlement.Acquired settlement,
            PackageContentEntry entry)
            : base(PackageFileAcquisitionStatus.Acquired, settlement)
        {
            Entry = entry;
        }

        public new PackageHouseSettlement.Acquired Settlement =>
            (PackageHouseSettlement.Acquired)base.Settlement;

        public PackageContentEntry Entry { get; }

        public PackageHousePayloadRead OpenRead() =>
            Settlement.OpenPayloadRead(
                Entry.Path,
                Entry.Length);
    }

    public sealed class Unavailable : PackageFileAcquisitionResult
    {
        internal Unavailable(
            PackageFileAcquisitionStatus status,
            PackageHouseSettlement settlement)
            : base(status, settlement)
        {
            if (status == PackageFileAcquisitionStatus.Acquired)
            {
                throw new ArgumentException(
                    "An unavailable package file cannot have acquired status.",
                    nameof(status));
            }
        }
    }
}

/// <summary>
/// Cache-aware acquisition and exact resolution of one package file.
/// </summary>
public static class PackageFileAcquisition
{
    public static async Task<PackageFileAcquisitionResult> ExecuteAsync(
        PackageFileAcquisitionRequest request,
        IPackageSourceAuthorization sourceAuthorization,
        PackageFileAcquisitionPlan plan,
        PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceOperation);

        var house = new PackageHouse(
            sourceAuthorization,
            plan.PayloadAcquisition,
            plan.PayloadAcquisition.Log);
        var houseRequest = new PackageHouseRequest(
            new PackageHouseDemand.Exact(request.Coordinate),
            request.Operation,
            contentQuery: request.ContentQuery);
        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                    houseRequest,
                    sourceOperation)
                .ConfigureAwait(false);
        if (settlement is not PackageHouseSettlement.Acquired acquired)
        {
            return new PackageFileAcquisitionResult.Unavailable(
                PackageFileAcquisitionStatus.NotSettled,
                settlement);
        }
        if (settlement.Result is not PackageHouseResult.Settled)
        {
            return new PackageFileAcquisitionResult.Unavailable(
                settlement.Result is PackageHouseResult.NoMatch
                    ? PackageFileAcquisitionStatus.Missing
                    : PackageFileAcquisitionStatus.NotSettled,
                settlement);
        }

        PackageFileEntryResolution resolution =
            PackageFileEntryResolver.Resolve(
                acquired,
                request.Path);
        return resolution.Status switch
        {
            PackageFileEntryResolutionStatus.Resolved =>
                new PackageFileAcquisitionResult.Acquired(
                    acquired,
                    resolution.Entry
                        ?? throw new InvalidOperationException(
                            "A resolved package file entry is required.")),
            PackageFileEntryResolutionStatus.ManifestUnavailable =>
                new PackageFileAcquisitionResult.Unavailable(
                    PackageFileAcquisitionStatus.ManifestUnavailable,
                    acquired),
            PackageFileEntryResolutionStatus.Missing =>
                new PackageFileAcquisitionResult.Unavailable(
                    PackageFileAcquisitionStatus.Missing,
                    acquired),
            PackageFileEntryResolutionStatus.Ambiguous =>
                new PackageFileAcquisitionResult.Unavailable(
                    PackageFileAcquisitionStatus.Ambiguous,
                    acquired),
            _ => throw new InvalidOperationException(
                "The package file entry resolution status is unsupported."),
        };
    }
}
