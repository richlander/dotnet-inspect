using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;

namespace DotnetInspector.Sections;

/// <summary>
/// One House acquisition an operation made: the coordinate, the authority
/// that supplied it, the payload origin, and what the acquisition transferred.
/// Owned by <c>docs/design/package-transfer-receipt.md#delivery</c>.
/// </summary>
public sealed record PackageAcquisitionEvidenceEntry(
    string PackageId,
    string Version,
    string Authority,
    PackagePayloadOrigin Origin,
    PackageTransferReceipt Transfer)
{
    internal static PackageAcquisitionEvidenceEntry From(
        PackageHouseAcquisitionReceipt acquisition) =>
        new(
            acquisition.Candidate.Coordinate.PackageId,
            acquisition.Candidate.Coordinate.Version,
            PackageSourceDisplay.ForDiagnostics(acquisition.Authority.Source)
                .ToString(),
            acquisition.Origin,
            acquisition.Transfer);
}

/// <summary>
/// The House acquisitions one operation made, in the order they settled.
/// Two entries for one coordinate are two acquisitions of it.
/// </summary>
public sealed record PackageAcquisitionEvidence(
    ImmutableArray<PackageAcquisitionEvidenceEntry> Acquisitions);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PackageAcquisitionEvidence))]
public partial class PackageAcquisitionEvidenceJsonContext
    : JsonSerializerContext;

/// <summary>
/// Records the House acquisition each cell settlement carries while passing
/// every settlement through unchanged.
/// </summary>
internal sealed class PackageAcquisitionEvidenceRecorder(
    IPackageHouseVersionPopulationCellExecutor inner)
    : IPackageHouseVersionPopulationCellExecutor
{
    private readonly List<PackageAcquisitionEvidenceEntry> _entries = [];

    public async Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseVersionPopulationCellExecution execution,
        CancellationToken cancellationToken = default)
    {
        PackageHouseSettlement settlement =
            await inner.ExecuteAsync(execution, cancellationToken)
                .ConfigureAwait(false);
        if (settlement.Result.Evidence.Acquisition is { } acquisition)
        {
            lock (_entries)
                _entries.Add(PackageAcquisitionEvidenceEntry.From(acquisition));
        }
        return settlement;
    }

    public PackageAcquisitionEvidence ToEvidence()
    {
        lock (_entries)
            return new PackageAcquisitionEvidence([.. _entries]);
    }
}
