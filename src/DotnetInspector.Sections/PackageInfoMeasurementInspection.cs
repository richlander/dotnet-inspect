using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>The typed state of Package Info measurements.</summary>
public enum PackageInfoMeasurementStatus
{
    Measured,
    SelectedEmpty,
    NoCompileSlices,
    NoApplicableSlice,
    InvalidSelection,
    HouseFailure,
    Unavailable,
}

/// <summary>
/// Host-neutral Package Info measurements from one PackageHouse compile
/// realization.
/// </summary>
public sealed record PackageInfoMeasurements
{
    internal PackageInfoMeasurements(
        PackageHouseCompileSliceMeasurementOutcome evidence,
        PackageInfoMeasurementStatus status,
        long? compressedPackageBytes,
        string? selectedTargetFramework,
        int? availableTargetFrameworkCount,
        long? selectedLibraryPayloadBytes,
        int? selectedLibraryCount,
        InertString? detail,
        PackageHouseCompileSliceMeasurementUnavailableReason?
            unavailableReason)
        : this(
            status,
            evidence?.Realization.Acquisition.Candidate.Coordinate.PackageId
                ?? throw new ArgumentNullException(nameof(evidence)),
            evidence.Realization.Acquisition.Candidate.Coordinate.Version,
            compressedPackageBytes,
            selectedTargetFramework,
            availableTargetFrameworkCount,
            selectedLibraryPayloadBytes,
            selectedLibraryCount,
            detail,
            unavailableReason)
    {
        Evidence = evidence
            ?? throw new ArgumentNullException(nameof(evidence));
    }

    [JsonConstructor]
    public PackageInfoMeasurements(
        PackageInfoMeasurementStatus status,
        string packageId,
        string packageVersion,
        long? compressedPackageBytes,
        string? selectedTargetFramework,
        int? availableTargetFrameworkCount,
        long? selectedLibraryPayloadBytes,
        int? selectedLibraryCount,
        InertString? detail,
        PackageHouseCompileSliceMeasurementUnavailableReason?
            unavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (compressedPackageBytes is < 0
            || availableTargetFrameworkCount is < 0
            || selectedLibraryPayloadBytes is < 0
            || selectedLibraryCount is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compressedPackageBytes),
                "Package Info measurement values cannot be negative.");
        }
        ValidateShape(
            status,
            compressedPackageBytes,
            selectedTargetFramework,
            availableTargetFrameworkCount,
            selectedLibraryPayloadBytes,
            selectedLibraryCount,
            detail,
            unavailableReason);

        Status = status;
        PackageId = packageId;
        PackageVersion = packageVersion;
        CompressedPackageBytes = compressedPackageBytes;
        SelectedTargetFramework = selectedTargetFramework;
        AvailableTargetFrameworkCount = availableTargetFrameworkCount;
        SelectedLibraryPayloadBytes = selectedLibraryPayloadBytes;
        SelectedLibraryCount = selectedLibraryCount;
        Detail = detail;
        UnavailableReason = unavailableReason;
    }

    private static void ValidateShape(
        PackageInfoMeasurementStatus status,
        long? compressedPackageBytes,
        string? selectedTargetFramework,
        int? availableTargetFrameworkCount,
        long? selectedLibraryPayloadBytes,
        int? selectedLibraryCount,
        InertString? detail,
        PackageHouseCompileSliceMeasurementUnavailableReason?
            unavailableReason)
    {
        bool hasSelectedMeasurements =
            compressedPackageBytes.HasValue
            && !string.IsNullOrWhiteSpace(selectedTargetFramework)
            && availableTargetFrameworkCount.HasValue
            && selectedLibraryPayloadBytes.HasValue
            && selectedLibraryCount.HasValue
            && detail is null
            && unavailableReason is null;
        if (status is PackageInfoMeasurementStatus.Measured
                or PackageInfoMeasurementStatus.SelectedEmpty)
        {
            if (!hasSelectedMeasurements
                || status == PackageInfoMeasurementStatus.SelectedEmpty
                    && (selectedLibraryPayloadBytes != 0
                        || selectedLibraryCount != 0))
            {
                throw new ArgumentException(
                    "A selected Package Info outcome requires complete selected-slice measurements.");
            }
            return;
        }

        if (selectedTargetFramework is not null
            || selectedLibraryPayloadBytes is not null
            || selectedLibraryCount is not null
            || detail is null)
        {
            throw new ArgumentException(
                "A non-selected Package Info outcome cannot contain selected-slice measurements.");
        }
        if (status == PackageInfoMeasurementStatus.HouseFailure
            && (compressedPackageBytes is not null
                || availableTargetFrameworkCount is not null
                || unavailableReason is not null))
        {
            throw new ArgumentException(
                "A PackageHouse failure cannot contain measurement values.");
        }
        if (status == PackageInfoMeasurementStatus.Unavailable)
        {
            if (unavailableReason is null
                || compressedPackageBytes.HasValue
                    != availableTargetFrameworkCount.HasValue)
            {
                throw new ArgumentException(
                    "An unavailable Package Info outcome requires its typed reason and coherent package measurements.");
            }
        }
        else if (!compressedPackageBytes.HasValue
            || !availableTargetFrameworkCount.HasValue
            || unavailableReason is not null)
        {
            throw new ArgumentException(
                "A selection non-success requires package measurements without an unavailable reason.");
        }
    }

    public PackageInfoMeasurementStatus Status { get; }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public long? CompressedPackageBytes { get; }

    public string? SelectedTargetFramework { get; }

    public int? AvailableTargetFrameworkCount { get; }

    public long? SelectedLibraryPayloadBytes { get; }

    public int? SelectedLibraryCount { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Detail { get; }

    public PackageHouseCompileSliceMeasurementUnavailableReason?
        UnavailableReason { get; }

    public bool HasSelectedSlice =>
        Status is PackageInfoMeasurementStatus.Measured
            or PackageInfoMeasurementStatus.SelectedEmpty;

    /// <summary>
    /// The resource-free owner evidence retaining the exact acquisition
    /// generation and compile-selection receipt.
    /// </summary>
    [JsonIgnore]
    public PackageHouseCompileSliceMeasurementOutcome? Evidence { get; }

    [JsonIgnore]
    public PackageContentGenerationIdentity? Generation =>
        Evidence?.Realization.Acquisition.Generation;

    [JsonIgnore]
    public PackageCompileAssetSelectionReceipt? SelectionReceipt =>
        Evidence?.Realization.Receipt;
}

/// <summary>
/// Projects PackageHouse compile measurements through the shared inspection
/// envelope boundary.
/// </summary>
public static class PackageInfoMeasurementInspection
{
    public static InspectionEnvelope<PackageInfoMeasurements> Project(
        PackageHouseSettlement.Acquired settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        PackageHouseCompileSliceMeasurementOutcome outcome =
            PackageHouseCompileSliceMeasurementProjection.Project(settlement);
        PackageInfoMeasurements content = CreateContent(outcome);

        var diagnostics = ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        diagnostics.AddRange(
            outcome.Result.Evidence.Failures
                .OfType<PackageHouseFailure.Authority>()
                .Select(value => new InspectionDiagnostic(
                    "package-info-measurements.source-failure",
                    InspectionDiagnosticSeverity.Warning,
                    value.Failure.Message)));
        if (content.Detail is { } detail)
        {
            diagnostics.Add(
                new InspectionDiagnostic(
                    $"package-info-measurements.{StatusCode(content.Status)}",
                    content.Status == PackageInfoMeasurementStatus.HouseFailure
                        ? InspectionDiagnosticSeverity.Error
                        : InspectionDiagnosticSeverity.Warning,
                    detail,
                    Field(
                        $"{content.PackageId}@{content.PackageVersion}")));
        }

        return new(
            content,
            new InspectionShare.NonProjectable(
                "package-info-measurements/share",
                "Package Info measurements do not yet have a canonical Workspace Share projection."),
            diagnostics.ToImmutable());
    }

    private static PackageInfoMeasurements CreateContent(
        PackageHouseCompileSliceMeasurementOutcome outcome) =>
        outcome switch
        {
            PackageHouseCompileSliceMeasurementOutcome.Measured measured =>
                Selected(
                    outcome,
                    PackageInfoMeasurementStatus.Measured,
                    measured.Measurements),
            PackageHouseCompileSliceMeasurementOutcome.SelectedEmpty empty =>
                Selected(
                    outcome,
                    PackageInfoMeasurementStatus.SelectedEmpty,
                    empty.Measurements),
            PackageHouseCompileSliceMeasurementOutcome.NoCompileSlices
                noSlices =>
                PackageOnly(
                    outcome,
                    PackageInfoMeasurementStatus.NoCompileSlices,
                    noSlices.Measurements,
                    ResultReason(outcome.Result)),
            PackageHouseCompileSliceMeasurementOutcome.NoApplicableSlice
                noApplicable =>
                PackageOnly(
                    outcome,
                    PackageInfoMeasurementStatus.NoApplicableSlice,
                    noApplicable.Measurements,
                    ResultReason(outcome.Result)),
            PackageHouseCompileSliceMeasurementOutcome.InvalidSelection
                invalid =>
                PackageOnly(
                    outcome,
                    PackageInfoMeasurementStatus.InvalidSelection,
                    invalid.Measurements,
                    ResultReason(outcome.Result)),
            PackageHouseCompileSliceMeasurementOutcome.HouseFailure =>
                new(
                    outcome,
                    PackageInfoMeasurementStatus.HouseFailure,
                    compressedPackageBytes: null,
                    selectedTargetFramework: null,
                    availableTargetFrameworkCount: null,
                    selectedLibraryPayloadBytes: null,
                    selectedLibraryCount: null,
                    ResultReason(outcome.Result),
                    unavailableReason: null),
            PackageHouseCompileSliceMeasurementOutcome.Unavailable unavailable =>
                new(
                    outcome,
                    PackageInfoMeasurementStatus.Unavailable,
                    unavailable.PackageMeasurements?.CompressedPackageBytes,
                    selectedTargetFramework: null,
                    unavailable.PackageMeasurements
                        ?.AvailableTargetFrameworkCount,
                    selectedLibraryPayloadBytes: null,
                    selectedLibraryCount: null,
                    UnavailableDetail(unavailable),
                    unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown PackageHouse compile measurement outcome."),
        };

    private static PackageInfoMeasurements Selected(
        PackageHouseCompileSliceMeasurementOutcome outcome,
        PackageInfoMeasurementStatus status,
        PackageHouseCompileSliceMeasurements measurements) =>
        new(
            outcome,
            status,
            measurements.CompressedPackageBytes,
            measurements.SelectedTargetFramework,
            measurements.AvailableTargetFrameworkCount,
            measurements.SelectedLibraryPayloadBytes,
            measurements.SelectedLibraryCount,
            detail: null,
            unavailableReason: null);

    private static PackageInfoMeasurements PackageOnly(
        PackageHouseCompileSliceMeasurementOutcome outcome,
        PackageInfoMeasurementStatus status,
        PackageHouseCompilePackageMeasurements measurements,
        InertString detail) =>
        new(
            outcome,
            status,
            measurements.CompressedPackageBytes,
            selectedTargetFramework: null,
            measurements.AvailableTargetFrameworkCount,
            selectedLibraryPayloadBytes: null,
            selectedLibraryCount: null,
            detail,
            unavailableReason: null);

    private static InertString ResultReason(PackageHouseResult result) =>
        result switch
        {
            PackageHouseResult.NoMatch value => value.Reason,
            PackageHouseResult.Ambiguous value => value.Reason,
            PackageHouseResult.Rejected value => value.Reason,
            PackageHouseResult.Unavailable value => value.Reason,
            PackageHouseResult.Incomplete value => value.Reason,
            PackageHouseResult.Failed value => value.Reason,
            _ => Field("PackageHouse did not produce selected-slice measurements."),
        };

    private static InertString UnavailableDetail(
        PackageHouseCompileSliceMeasurementOutcome.Unavailable unavailable) =>
        Field(unavailable.Reason switch
        {
            PackageHouseCompileSliceMeasurementUnavailableReason
                .ArchiveUnavailable =>
                "The retained package archive is unavailable.",
            PackageHouseCompileSliceMeasurementUnavailableReason
                .ArchiveLengthUnavailable =>
                "The retained package archive length is unavailable.",
            PackageHouseCompileSliceMeasurementUnavailableReason
                .EntryManifestUnavailable =>
                "The retained package entry manifest is unavailable.",
            PackageHouseCompileSliceMeasurementUnavailableReason
                .SelectedEntryUnavailable =>
                $"Selected package entry '{unavailable.PayloadAsset?.Path}' is unavailable.",
            PackageHouseCompileSliceMeasurementUnavailableReason
                .SelectedEntryLengthInvalid =>
                $"Selected package entry '{unavailable.PayloadAsset?.Path}' has an invalid length.",
            PackageHouseCompileSliceMeasurementUnavailableReason
                .SelectedPayloadBytesOverflow =>
                "The selected package payload byte total overflowed.",
            _ => "Package Info measurements are unavailable.",
        });

    private static string StatusCode(PackageInfoMeasurementStatus status) =>
        status switch
        {
            PackageInfoMeasurementStatus.NoCompileSlices =>
                "no-compile-slices",
            PackageInfoMeasurementStatus.NoApplicableSlice =>
                "no-applicable-slice",
            PackageInfoMeasurementStatus.InvalidSelection =>
                "invalid-selection",
            PackageInfoMeasurementStatus.HouseFailure =>
                "house-failure",
            PackageInfoMeasurementStatus.Unavailable =>
                "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
