using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>The typed state of Package Info measurements.</summary>
public enum PackageInfoMeasurementStatus
{
    Measured,
    SelectedEmpty,
    NoCompileSlices,
    NoToolSlices,
    NoApplicableSlice,
    InvalidSelection,
    HouseFailure,
    Unavailable,
}

/// <summary>
/// Host-neutral Package Info measurements from one retained package
/// generation.
/// </summary>
public sealed record PackageInfoMeasurements
{
    internal PackageInfoMeasurements(
        PackageHouseCompileSliceMeasurementOutcome evidence,
        PackageInfoMeasurementStatus status,
        long? compressedPackageBytes,
        string? selectedTargetFramework,
        IReadOnlyList<InertString>? availableTargetFrameworks,
        IReadOnlyList<InertString>? selectedTargetFrameworkFolders,
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
            selectedTargetFramework is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    selectedTargetFramework),
            availableTargetFrameworks,
            selectedTargetFrameworkFolders,
            selectedLibraryPayloadBytes,
            selectedLibraryCount,
            detail,
            unavailableReason)
    {
        Evidence = evidence
            ?? throw new ArgumentNullException(nameof(evidence));
    }

    internal PackageInfoMeasurements(
        PackageToolSliceMeasurementOutcome evidence,
        PackageInfoMeasurementStatus status,
        long? compressedPackageBytes,
        string? selectedTargetFramework,
        IReadOnlyList<InertString>? availableTargetFrameworks,
        IReadOnlyList<InertString>? selectedTargetFrameworkFolders,
        long? selectedLibraryPayloadBytes,
        int? selectedLibraryCount,
        InertString? detail,
        PackageHouseCompileSliceMeasurementUnavailableReason?
            unavailableReason)
        : this(
            status,
            evidence?.Evidence.Coordinate.PackageId
                ?? throw new ArgumentNullException(nameof(evidence)),
            evidence.Evidence.Coordinate.Version,
            compressedPackageBytes,
            selectedTargetFramework is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    selectedTargetFramework),
            availableTargetFrameworks,
            selectedTargetFrameworkFolders,
            selectedLibraryPayloadBytes,
            selectedLibraryCount,
            detail,
            unavailableReason)
    {
        ToolEvidence = evidence;
    }

    internal PackageInfoMeasurements(
        PackageRootBinding root,
        PackageInfoMeasurementStatus status,
        long? compressedPackageBytes,
        string? selectedTargetFramework,
        IReadOnlyList<InertString>? availableTargetFrameworks,
        IReadOnlyList<InertString>? selectedTargetFrameworkFolders,
        long? selectedLibraryPayloadBytes,
        int? selectedLibraryCount,
        InertString? detail,
        PackageHouseCompileSliceMeasurementUnavailableReason?
            unavailableReason)
        : this(
            status,
            root?.Root.PackageId
                ?? throw new ArgumentNullException(nameof(root)),
            root.Root.PackageVersion,
            compressedPackageBytes,
            selectedTargetFramework is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    selectedTargetFramework),
            availableTargetFrameworks,
            selectedTargetFrameworkFolders,
            selectedLibraryPayloadBytes,
            selectedLibraryCount,
            detail,
            unavailableReason)
    {
        RootGeneration = root.ContentGenerationIdentity;
        RootSelectionIdentity = root.SelectionIdentity;
    }

    [JsonConstructor]
    public PackageInfoMeasurements(
        PackageInfoMeasurementStatus status,
        string packageId,
        string packageVersion,
        long? compressedPackageBytes,
        InertString? selectedTargetFramework,
        IReadOnlyList<InertString>? availableTargetFrameworks,
        IReadOnlyList<InertString>? selectedTargetFrameworkFolders,
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
            availableTargetFrameworks,
            selectedTargetFrameworkFolders,
            selectedLibraryPayloadBytes,
            selectedLibraryCount,
            detail,
            unavailableReason);

        Status = status;
        PackageId = packageId;
        PackageVersion = packageVersion;
        CompressedPackageBytes = compressedPackageBytes;
        SelectedTargetFramework = selectedTargetFramework;
        AvailableTargetFrameworks = availableTargetFrameworks?.ToArray();
        SelectedTargetFrameworkFolders =
            selectedTargetFrameworkFolders?.ToArray();
        SelectedLibraryPayloadBytes = selectedLibraryPayloadBytes;
        SelectedLibraryCount = selectedLibraryCount;
        Detail = detail;
        UnavailableReason = unavailableReason;
    }

    private static void ValidateShape(
        PackageInfoMeasurementStatus status,
        long? compressedPackageBytes,
        InertString? selectedTargetFramework,
        IReadOnlyList<InertString>? availableTargetFrameworks,
        IReadOnlyList<InertString>? selectedTargetFrameworkFolders,
        long? selectedLibraryPayloadBytes,
        int? selectedLibraryCount,
        InertString? detail,
        PackageHouseCompileSliceMeasurementUnavailableReason?
            unavailableReason)
    {
        bool hasValidAvailableTargetFrameworks =
            availableTargetFrameworks is not null
            && availableTargetFrameworks.All(
                static framework =>
                    !string.IsNullOrWhiteSpace(framework.ToString())
                    && InertString.IsPermitted(
                        TextPolicy.Field,
                        framework.ToString()))
            && availableTargetFrameworks
                .Select(static framework => framework.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == availableTargetFrameworks.Count;
        bool hasSelectedMeasurements =
            compressedPackageBytes.HasValue
            && selectedTargetFramework is not null
            && !string.IsNullOrWhiteSpace(
                selectedTargetFramework.ToString())
            && hasValidAvailableTargetFrameworks
            && availableTargetFrameworks!.Any(framework =>
                framework.ToString().Equals(
                    selectedTargetFramework.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            && selectedTargetFrameworkFolders is not null
            && selectedTargetFrameworkFolders.All(
                static folder =>
                    !string.IsNullOrWhiteSpace(folder.ToString())
                    && InertString.IsPermitted(
                        TextPolicy.Field,
                        folder.ToString()))
            && selectedTargetFrameworkFolders
                .Select(static folder => folder.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == selectedTargetFrameworkFolders.Count
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
            || selectedTargetFrameworkFolders is not null
            || selectedLibraryPayloadBytes is not null
            || selectedLibraryCount is not null
            || detail is null)
        {
            throw new ArgumentException(
                "A non-selected Package Info outcome cannot contain selected-slice measurements.");
        }
        if (status == PackageInfoMeasurementStatus.HouseFailure
            && (compressedPackageBytes is not null
                || availableTargetFrameworks is not null
                || unavailableReason is not null))
        {
            throw new ArgumentException(
                "A PackageHouse failure cannot contain measurement values.");
        }
        if (status == PackageInfoMeasurementStatus.Unavailable)
        {
            if (unavailableReason is null
                || compressedPackageBytes.HasValue
                    != (availableTargetFrameworks is not null)
                || availableTargetFrameworks is not null
                    && !hasValidAvailableTargetFrameworks)
            {
                throw new ArgumentException(
                    "An unavailable Package Info outcome requires its typed reason and coherent package measurements.");
            }
        }
        else if (status == PackageInfoMeasurementStatus.NoToolSlices
            && availableTargetFrameworks?.Count != 0)
        {
            throw new ArgumentException(
                "A no-tool-slices outcome requires an empty target-framework inventory.");
        }
        else if (!compressedPackageBytes.HasValue
            || !hasValidAvailableTargetFrameworks
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

    public InertString? SelectedTargetFramework { get; }

    public IReadOnlyList<InertString>? AvailableTargetFrameworks { get; }

    public IReadOnlyList<InertString>? SelectedTargetFrameworkFolders { get; }

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
    /// The PackageHouse evidence retaining the exact acquisition generation
    /// and compile-selection receipt.
    /// </summary>
    [JsonIgnore]
    public PackageHouseCompileSliceMeasurementOutcome? Evidence { get; }

    /// <summary>
    /// The resource-free declared-tool evidence retaining the exact acquired
    /// content generation.
    /// </summary>
    [JsonIgnore]
    public PackageToolSliceMeasurementOutcome? ToolEvidence { get; }

    [JsonIgnore]
    public PackageContentGenerationIdentity? Generation =>
        Evidence?.Realization.Acquisition.Generation
        ?? ToolEvidence?.Evidence.Generation
        ?? RootGeneration;

    [JsonIgnore]
    public PackageCompileAssetSelectionReceipt? SelectionReceipt =>
        Evidence?.Realization.Receipt;

    [JsonIgnore]
    public PackageRootSelectionIdentity? RootSelectionIdentity { get; }

    [JsonIgnore]
    private PackageContentGenerationIdentity? RootGeneration { get; }
}

/// <summary>
/// Projects retained Package Root, compile, or declared-tool measurements
/// through the shared inspection envelope boundary.
/// </summary>
public static class PackageInfoMeasurementInspection
{
    public static InspectionEnvelope<PackageInfoMeasurements> Project(
        PackageRootBinding root)
    {
        ArgumentNullException.ThrowIfNull(root);
        PackageInfoMeasurements content = root.Root.UseContent(
            packageContent => CreateContent(root, packageContent));
        return CreateRootEnvelope(content);
    }

    /// <summary>
    /// Projects one already-admitted Package Root using the same declared-tool
    /// versus compile-slice choice as ordinary Package inspection.
    /// </summary>
    public static InspectionEnvelope<PackageInfoMeasurements>
        ProjectAdmittedRoot(
            PackageRootBinding root,
            ReadOnlyMemory<byte>? admittedPackageManifest,
            string? requestedTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(
            root.Root.PackageId,
            root.Root.PackageVersion);
        PackageInfoMeasurements content = root.Root.UseContent(
            packageContent =>
            {
                PackageToolDeclarationEvidence? declaration =
                    admittedPackageManifest is { } manifest
                        ? PackageToolDeclarationEvidence.TryCreate(
                            coordinate,
                            packageContent,
                            manifest)
                        : null;
                return declaration is null
                    ? CreateContent(root, packageContent)
                    : CreateContent(
                        PackageToolSliceMeasurementProjection.Project(
                            coordinate,
                            packageContent,
                            declaration,
                            requestedTargetFramework));
            });
        return CreateRootEnvelope(content);
    }

    private static InspectionEnvelope<PackageInfoMeasurements>
        CreateRootEnvelope(PackageInfoMeasurements content)
    {
        var diagnostics = ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        if (content.Detail is { } detail)
        {
            diagnostics.Add(
                new InspectionDiagnostic(
                    $"package-info-measurements.{StatusCode(content.Status)}",
                    InspectionDiagnosticSeverity.Warning,
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

    /// <summary>
    /// Projects one nuspec-declared DotnetTool payload through the shared
    /// Package Info envelope.
    /// </summary>
    public static InspectionEnvelope<PackageInfoMeasurements>
        ProjectDeclaredTool(
            PackageHouseSettlement.Acquired settlement,
            PackageToolDeclarationEvidence declaration,
            string? requestedTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(declaration);
        PackageToolSliceMeasurementOutcome outcome =
            PackageToolSliceMeasurementProjection.Project(
                settlement.Payload,
                declaration,
                requestedTargetFramework);
        PackageInfoMeasurements content = CreateContent(outcome);
        var diagnostics = ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        diagnostics.AddRange(
            settlement.Result.Evidence.Failures
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
                    InspectionDiagnosticSeverity.Warning,
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
                    availableTargetFrameworks: null,
                    selectedTargetFrameworkFolders: null,
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
                        ?.AvailableTargetFrameworks
                        .Select(static framework =>
                            new InertString(TextPolicy.Field, framework))
                        .ToArray(),
                    selectedTargetFrameworkFolders: null,
                    selectedLibraryPayloadBytes: null,
                    selectedLibraryCount: null,
                    UnavailableDetail(unavailable),
                    unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown PackageHouse compile measurement outcome."),
        };

    private static PackageInfoMeasurements CreateContent(
        PackageRootBinding root,
        IPackageContent content)
    {
        PackageCompileAssetSelection selection = root.Root.AssetSelection;
        if (!PackageHouseCompileSliceMeasurementProjection
                .TryGetArchiveLength(
                    content,
                    out long compressedPackageBytes,
                    out PackageHouseCompileSliceMeasurementUnavailableReason
                        archiveFailure))
        {
            return Unavailable(
                root,
                compressedPackageBytes: null,
                availableTargetFrameworks: null,
                archiveFailure);
        }

        InertString[] availableTargetFrameworks =
        [
            .. selection.AvailableTargetFrameworks.Select(
                static framework =>
                    new InertString(TextPolicy.Field, framework)),
        ];
        return selection.Status switch
        {
            PackageCompileAssetSelectionStatus.Selected =>
                MeasureSelectedRoot(
                    root,
                    content,
                    selection,
                    compressedPackageBytes,
                    availableTargetFrameworks,
                    selectedEmpty: false),
            PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                MeasureSelectedRoot(
                    root,
                    content,
                    selection,
                    compressedPackageBytes,
                    availableTargetFrameworks,
                    selectedEmpty: true),
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                RootPackageOnly(
                    root,
                    PackageInfoMeasurementStatus.NoCompileSlices,
                    compressedPackageBytes,
                    availableTargetFrameworks,
                    "The package contains no compile asset slices."),
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                RootPackageOnly(
                    root,
                    PackageInfoMeasurementStatus.NoApplicableSlice,
                    compressedPackageBytes,
                    availableTargetFrameworks,
                    selection.Message
                        ?? "The requested target framework has no applicable compile slice."),
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                RootPackageOnly(
                    root,
                    PackageInfoMeasurementStatus.InvalidSelection,
                    compressedPackageBytes,
                    availableTargetFrameworks,
                    selection.Message
                        ?? "The selected implementation assets are invalid."),
            _ => throw new InvalidOperationException(
                "Unknown compile asset-selection status."),
        };
    }

    private static PackageInfoMeasurements MeasureSelectedRoot(
        PackageRootBinding root,
        IPackageContent content,
        PackageCompileAssetSelection selection,
        long compressedPackageBytes,
        IReadOnlyList<InertString> availableTargetFrameworks,
        bool selectedEmpty)
    {
        string selectedTargetFramework =
            selection.TargetFramework
            ?? throw new InvalidOperationException(
                "A selected compile slice requires a target framework.");
        ImmutableArray<string> selectedFolders =
            PackageHouseCompileSliceMeasurementProjection
                .SelectTargetFrameworkFolders(
                    content,
                    selectedTargetFramework);
        long selectedPayloadBytes = 0;
        if (!selectedEmpty)
        {
            if (content is not IPackageContentEntryManifest manifest)
            {
                return Unavailable(
                    root,
                    compressedPackageBytes,
                    availableTargetFrameworks,
                    PackageHouseCompileSliceMeasurementUnavailableReason
                        .EntryManifestUnavailable);
            }

            foreach (PackageCompileAsset compileAsset in selection.Assets)
            {
                PackageCompileAsset payloadAsset =
                    selection.FindImplementationAsset(compileAsset)
                    ?? compileAsset;
                if (!manifest.TryGetEntryLength(
                        payloadAsset.Path,
                        out long uncompressedBytes))
                {
                    return Unavailable(
                        root,
                        compressedPackageBytes,
                        availableTargetFrameworks,
                        PackageHouseCompileSliceMeasurementUnavailableReason
                            .SelectedEntryUnavailable,
                        payloadAsset.Path);
                }
                if (uncompressedBytes < 0)
                {
                    return Unavailable(
                        root,
                        compressedPackageBytes,
                        availableTargetFrameworks,
                        PackageHouseCompileSliceMeasurementUnavailableReason
                            .SelectedEntryLengthInvalid,
                        payloadAsset.Path);
                }

                try
                {
                    selectedPayloadBytes = checked(
                        selectedPayloadBytes + uncompressedBytes);
                }
                catch (OverflowException)
                {
                    return Unavailable(
                        root,
                        compressedPackageBytes,
                        availableTargetFrameworks,
                        PackageHouseCompileSliceMeasurementUnavailableReason
                            .SelectedPayloadBytesOverflow,
                        payloadAsset.Path);
                }
            }
        }

        return new(
            root,
            selectedEmpty
                ? PackageInfoMeasurementStatus.SelectedEmpty
                : PackageInfoMeasurementStatus.Measured,
            compressedPackageBytes,
            selectedTargetFramework,
            availableTargetFrameworks,
            selectedFolders.Select(
                    static folder =>
                        new InertString(TextPolicy.Field, folder))
                .ToArray(),
            selectedPayloadBytes,
            selection.Assets.Count,
            detail: null,
            unavailableReason: null);
    }

    private static PackageInfoMeasurements RootPackageOnly(
        PackageRootBinding root,
        PackageInfoMeasurementStatus status,
        long compressedPackageBytes,
        IReadOnlyList<InertString> availableTargetFrameworks,
        string detail) =>
        new(
            root,
            status,
            compressedPackageBytes,
            selectedTargetFramework: null,
            availableTargetFrameworks,
            selectedTargetFrameworkFolders: null,
            selectedLibraryPayloadBytes: null,
            selectedLibraryCount: null,
            Field(detail),
            unavailableReason: null);

    private static PackageInfoMeasurements Unavailable(
        PackageRootBinding root,
        long? compressedPackageBytes,
        IReadOnlyList<InertString>? availableTargetFrameworks,
        PackageHouseCompileSliceMeasurementUnavailableReason reason,
        string? selectedEntry = null) =>
        new(
            root,
            PackageInfoMeasurementStatus.Unavailable,
            compressedPackageBytes,
            selectedTargetFramework: null,
            availableTargetFrameworks,
            selectedTargetFrameworkFolders: null,
            selectedLibraryPayloadBytes: null,
            selectedLibraryCount: null,
            UnavailableDetail(reason, selectedEntry),
            reason);

    private static PackageInfoMeasurements CreateContent(
        PackageToolSliceMeasurementOutcome outcome) =>
        outcome switch
        {
            PackageToolSliceMeasurementOutcome.Measured measured =>
                Selected(
                    outcome,
                    PackageInfoMeasurementStatus.Measured,
                    measured.Measurements),
            PackageToolSliceMeasurementOutcome.SelectedEmpty empty =>
                Selected(
                    outcome,
                    PackageInfoMeasurementStatus.SelectedEmpty,
                    empty.Measurements),
            PackageToolSliceMeasurementOutcome.NoToolSlices noSlices =>
                PackageOnly(
                    outcome,
                    PackageInfoMeasurementStatus.NoToolSlices,
                    noSlices.Measurements,
                    Field(
                        "The package contains no tool target-framework Library slice.")),
            PackageToolSliceMeasurementOutcome.NoApplicableSlice
                noApplicable =>
                PackageOnly(
                    outcome,
                    PackageInfoMeasurementStatus.NoApplicableSlice,
                    noApplicable.Measurements,
                    Field(
                        "The requested target framework has no applicable tool slice.")),
            PackageToolSliceMeasurementOutcome.InvalidSelection invalid =>
                PackageOnly(
                    outcome,
                    PackageInfoMeasurementStatus.InvalidSelection,
                    invalid.Measurements,
                    Field(invalid.Reason)),
            PackageToolSliceMeasurementOutcome.Unavailable unavailable =>
                new(
                    outcome,
                    PackageInfoMeasurementStatus.Unavailable,
                    unavailable.PackageMeasurements?.CompressedPackageBytes,
                    selectedTargetFramework: null,
                    unavailable.PackageMeasurements
                        ?.AvailableTargetFrameworks
                        .Select(static framework =>
                            new InertString(TextPolicy.Field, framework))
                        .ToArray(),
                    selectedTargetFrameworkFolders: null,
                    selectedLibraryPayloadBytes: null,
                    selectedLibraryCount: null,
                    UnavailableDetail(unavailable),
                    Map(unavailable.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown tool-slice measurement outcome."),
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
            measurements.AvailableTargetFrameworks
                .Select(static framework =>
                    new InertString(TextPolicy.Field, framework))
                .ToArray(),
            measurements.SelectedTargetFrameworkFolders
                .Select(static folder =>
                    new InertString(TextPolicy.Field, folder))
                .ToArray(),
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
            measurements.AvailableTargetFrameworks
                .Select(static framework =>
                    new InertString(TextPolicy.Field, framework))
                .ToArray(),
            selectedTargetFrameworkFolders: null,
            selectedLibraryPayloadBytes: null,
            selectedLibraryCount: null,
            detail,
            unavailableReason: null);

    private static PackageInfoMeasurements Selected(
        PackageToolSliceMeasurementOutcome outcome,
        PackageInfoMeasurementStatus status,
        PackageToolSliceMeasurements measurements) =>
        new(
            outcome,
            status,
            measurements.CompressedPackageBytes,
            measurements.SelectedTargetFramework,
            measurements.AvailableTargetFrameworks
                .Select(static framework =>
                    new InertString(TextPolicy.Field, framework))
                .ToArray(),
            measurements.SelectedTargetFrameworkFolders
                .Select(static folder =>
                    new InertString(TextPolicy.Field, folder))
                .ToArray(),
            measurements.SelectedLibraryPayloadBytes,
            measurements.SelectedLibraryCount,
            detail: null,
            unavailableReason: null);

    private static PackageInfoMeasurements PackageOnly(
        PackageToolSliceMeasurementOutcome outcome,
        PackageInfoMeasurementStatus status,
        PackageToolPackageMeasurements measurements,
        InertString detail) =>
        new(
            outcome,
            status,
            measurements.CompressedPackageBytes,
            selectedTargetFramework: null,
            measurements.AvailableTargetFrameworks
                .Select(static framework =>
                    new InertString(TextPolicy.Field, framework))
                .ToArray(),
            selectedTargetFrameworkFolders: null,
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
        UnavailableDetail(
            unavailable.Reason,
            unavailable.PayloadAsset?.Path);

    private static InertString UnavailableDetail(
        PackageHouseCompileSliceMeasurementUnavailableReason reason,
        string? selectedEntry) =>
        Field(reason switch
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
                $"Selected package entry '{selectedEntry}' is unavailable.",
            PackageHouseCompileSliceMeasurementUnavailableReason
                .SelectedEntryLengthInvalid =>
                $"Selected package entry '{selectedEntry}' has an invalid length.",
            PackageHouseCompileSliceMeasurementUnavailableReason
                .SelectedPayloadBytesOverflow =>
                "The selected package payload byte total overflowed.",
            _ => "Package Info measurements are unavailable.",
        });

    private static InertString UnavailableDetail(
        PackageToolSliceMeasurementOutcome.Unavailable unavailable) =>
        Field(unavailable.Reason switch
        {
            PackageToolSliceMeasurementUnavailableReason.ArchiveUnavailable =>
                "The retained package archive is unavailable.",
            PackageToolSliceMeasurementUnavailableReason
                .ArchiveLengthUnavailable =>
                "The retained package archive length is unavailable.",
            PackageToolSliceMeasurementUnavailableReason
                .EntryManifestUnavailable =>
                "The retained package entry manifest is unavailable.",
            PackageToolSliceMeasurementUnavailableReason
                .SelectedEntryUnavailable =>
                $"Selected package entry '{unavailable.SelectedEntry}' is unavailable.",
            PackageToolSliceMeasurementUnavailableReason
                .SelectedEntryLengthInvalid =>
                $"Selected package entry '{unavailable.SelectedEntry}' has an invalid length.",
            PackageToolSliceMeasurementUnavailableReason
                .SelectedPayloadBytesOverflow =>
                "The selected package payload byte total overflowed.",
            _ => "Package Info measurements are unavailable.",
        });

    private static PackageHouseCompileSliceMeasurementUnavailableReason Map(
        PackageToolSliceMeasurementUnavailableReason reason) =>
        reason switch
        {
            PackageToolSliceMeasurementUnavailableReason.ArchiveUnavailable =>
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .ArchiveUnavailable,
            PackageToolSliceMeasurementUnavailableReason
                .ArchiveLengthUnavailable =>
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .ArchiveLengthUnavailable,
            PackageToolSliceMeasurementUnavailableReason
                .EntryManifestUnavailable =>
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .EntryManifestUnavailable,
            PackageToolSliceMeasurementUnavailableReason
                .SelectedEntryUnavailable =>
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .SelectedEntryUnavailable,
            PackageToolSliceMeasurementUnavailableReason
                .SelectedEntryLengthInvalid =>
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .SelectedEntryLengthInvalid,
            PackageToolSliceMeasurementUnavailableReason
                .SelectedPayloadBytesOverflow =>
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .SelectedPayloadBytesOverflow,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static string StatusCode(PackageInfoMeasurementStatus status) =>
        status switch
        {
            PackageInfoMeasurementStatus.NoCompileSlices =>
                "no-compile-slices",
            PackageInfoMeasurementStatus.NoToolSlices =>
                "no-tool-slices",
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
