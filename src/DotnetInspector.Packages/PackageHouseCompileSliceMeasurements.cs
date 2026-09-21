using System.Collections.Immutable;

namespace DotnetInspector.Packages;

/// <summary>
/// Why one selected compile-slice measurement could not be completed.
/// </summary>
public enum PackageHouseCompileSliceMeasurementUnavailableReason
{
    ArchiveUnavailable,
    ArchiveLengthUnavailable,
    EntryManifestUnavailable,
    SelectedEntryUnavailable,
    SelectedEntryLengthInvalid,
    SelectedPayloadBytesOverflow,
}

/// <summary>
/// The uncompressed payload selected for one compile-time Library.
/// </summary>
public sealed class PackageHouseCompileLibraryMeasurement
{
    internal PackageHouseCompileLibraryMeasurement(
        PackageCompileAsset compileAsset,
        PackageCompileAsset payloadAsset,
        long uncompressedBytes)
    {
        ArgumentNullException.ThrowIfNull(compileAsset);
        ArgumentNullException.ThrowIfNull(payloadAsset);
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedBytes);

        CompileAsset = compileAsset;
        PayloadAsset = payloadAsset;
        UncompressedBytes = uncompressedBytes;
    }

    public PackageCompileAsset CompileAsset { get; }

    /// <summary>
    /// The implementation counterpart when one exists; otherwise the compile
    /// asset itself.
    /// </summary>
    public PackageCompileAsset PayloadAsset { get; }

    public long UncompressedBytes { get; }
}

/// <summary>
/// Resource-free package measurements associated with one compile realization.
/// </summary>
public sealed class PackageHouseCompilePackageMeasurements
{
    internal PackageHouseCompilePackageMeasurements(
        PackageHouseRealizationReceipt.Compile realization,
        long compressedPackageBytes)
    {
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentOutOfRangeException.ThrowIfNegative(compressedPackageBytes);

        Realization = realization;
        CompressedPackageBytes = compressedPackageBytes;
    }

    public PackageHouseRealizationReceipt.Compile Realization { get; }

    public PackageHouseAcquisitionReceipt Acquisition =>
        Realization.Acquisition;

    public PackageCompileAssetSelectionReceipt SelectionReceipt =>
        Realization.Receipt;

    public PackageContentGenerationIdentity Generation =>
        Acquisition.Generation;

    public NuGetFetch.PackageSourceCoordinate Coordinate =>
        Acquisition.Candidate.Coordinate;

    public IReadOnlyList<string> AvailableTargetFrameworks =>
        Realization.Selection.AvailableTargetFrameworks;

    public long CompressedPackageBytes { get; }
}

/// <summary>
/// Resource-free measurements for one PackageHouse-selected compile slice.
/// </summary>
public sealed class PackageHouseCompileSliceMeasurements
{
    internal PackageHouseCompileSliceMeasurements(
        PackageHouseCompilePackageMeasurements package,
        ImmutableArray<PackageHouseCompileLibraryMeasurement> libraries,
        ImmutableArray<string> selectedTargetFrameworkFolders,
        long selectedLibraryPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentOutOfRangeException.ThrowIfNegative(
            selectedLibraryPayloadBytes);
        if (selectedTargetFrameworkFolders.IsDefault
            || selectedTargetFrameworkFolders.Any(string.IsNullOrWhiteSpace)
            || selectedTargetFrameworkFolders
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != selectedTargetFrameworkFolders.Length)
        {
            throw new ArgumentException(
                "Selected target-framework folders must be initialized, non-empty names without case-insensitive duplicates.",
                nameof(selectedTargetFrameworkFolders));
        }
        PackageHouseRealizationReceipt.Compile realization =
            package.Realization;
        if (realization.Selection.Status is not
                (PackageCompileAssetSelectionStatus.Selected
                or PackageCompileAssetSelectionStatus.EmptyCompileGroup)
            || realization.Selection.TargetFramework is null)
        {
            throw new ArgumentException(
                "Compile-slice measurements require one selected or explicitly empty slice.",
                nameof(realization));
        }
        if (realization.Selection.Status
                == PackageCompileAssetSelectionStatus.Selected
            && libraries.Length != realization.Selection.Assets.Count)
        {
            throw new ArgumentException(
                "Every selected compile asset requires one Library measurement.",
                nameof(libraries));
        }
        if (realization.Selection.Status
                == PackageCompileAssetSelectionStatus.EmptyCompileGroup
            && !libraries.IsEmpty)
        {
            throw new ArgumentException(
                "An explicitly empty compile slice cannot contain Library measurements.",
                nameof(libraries));
        }

        Package = package;
        Libraries = libraries;
        SelectedTargetFrameworkFolders = selectedTargetFrameworkFolders;
        SelectedLibraryPayloadBytes = selectedLibraryPayloadBytes;
    }

    public PackageHouseCompilePackageMeasurements Package { get; }

    public PackageHouseRealizationReceipt.Compile Realization =>
        Package.Realization;

    public PackageHouseAcquisitionReceipt Acquisition =>
        Package.Acquisition;

    public PackageCompileAssetSelectionReceipt SelectionReceipt =>
        Package.SelectionReceipt;

    public PackageContentGenerationIdentity Generation =>
        Package.Generation;

    public NuGetFetch.PackageSourceCoordinate Coordinate =>
        Package.Coordinate;

    public string SelectedTargetFramework =>
        Realization.Selection.TargetFramework!;

    public IReadOnlyList<string> AvailableTargetFrameworks =>
        Package.AvailableTargetFrameworks;

    public long CompressedPackageBytes => Package.CompressedPackageBytes;

    public ImmutableArray<PackageHouseCompileLibraryMeasurement> Libraries
    { get; }

    public ImmutableArray<string> SelectedTargetFrameworkFolders { get; }

    public int SelectedLibraryCount => Libraries.Length;

    public long SelectedLibraryPayloadBytes { get; }
}

/// <summary>
/// The typed result of projecting measurements from one acquired compile
/// realization.
/// </summary>
public abstract class PackageHouseCompileSliceMeasurementOutcome
{
    private protected PackageHouseCompileSliceMeasurementOutcome(
        PackageHouseResult result,
        PackageHouseRealizationReceipt.Compile realization)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(realization);
        if (!ReferenceEquals(result.Evidence.Realization, realization))
        {
            throw new ArgumentException(
                "The measurement outcome must retain the result's exact realization.",
                nameof(realization));
        }

        Result = result;
        Realization = realization;
    }

    public PackageHouseResult Result { get; }

    public PackageHouseRealizationReceipt.Compile Realization { get; }

    public sealed class Measured : PackageHouseCompileSliceMeasurementOutcome
    {
        internal Measured(
            PackageHouseResult result,
            PackageHouseRealizationReceipt.Compile realization,
            PackageHouseCompileSliceMeasurements measurements)
            : base(result, realization) =>
            Measurements = measurements
                ?? throw new ArgumentNullException(nameof(measurements));

        public PackageHouseCompileSliceMeasurements Measurements { get; }
    }

    public sealed class SelectedEmpty :
        PackageHouseCompileSliceMeasurementOutcome
    {
        internal SelectedEmpty(
            PackageHouseResult result,
            PackageHouseRealizationReceipt.Compile realization,
            PackageHouseCompileSliceMeasurements measurements)
            : base(result, realization) =>
            Measurements = measurements
                ?? throw new ArgumentNullException(nameof(measurements));

        public PackageHouseCompileSliceMeasurements Measurements { get; }
    }

    public sealed class NoCompileSlices :
        PackageHouseCompileSliceMeasurementOutcome
    {
        internal NoCompileSlices(
            PackageHouseResult result,
            PackageHouseRealizationReceipt.Compile realization,
            PackageHouseCompilePackageMeasurements measurements)
            : base(result, realization) =>
            Measurements = measurements
                ?? throw new ArgumentNullException(nameof(measurements));

        public PackageHouseCompilePackageMeasurements Measurements { get; }
    }

    public sealed class NoApplicableSlice :
        PackageHouseCompileSliceMeasurementOutcome
    {
        internal NoApplicableSlice(
            PackageHouseResult result,
            PackageHouseRealizationReceipt.Compile realization,
            PackageHouseCompilePackageMeasurements measurements)
            : base(result, realization) =>
            Measurements = measurements
                ?? throw new ArgumentNullException(nameof(measurements));

        public PackageHouseCompilePackageMeasurements Measurements { get; }
    }

    public sealed class InvalidSelection :
        PackageHouseCompileSliceMeasurementOutcome
    {
        internal InvalidSelection(
            PackageHouseResult result,
            PackageHouseRealizationReceipt.Compile realization,
            PackageHouseCompilePackageMeasurements measurements)
            : base(result, realization) =>
            Measurements = measurements
                ?? throw new ArgumentNullException(nameof(measurements));

        public PackageHouseCompilePackageMeasurements Measurements { get; }
    }

    public sealed class HouseFailure :
        PackageHouseCompileSliceMeasurementOutcome
    {
        internal HouseFailure(
            PackageHouseResult result,
            PackageHouseRealizationReceipt.Compile realization)
            : base(result, realization)
        {
        }
    }

    public sealed class Unavailable :
        PackageHouseCompileSliceMeasurementOutcome
    {
        internal Unavailable(
            PackageHouseResult result,
            PackageHouseRealizationReceipt.Compile realization,
            PackageHouseCompileSliceMeasurementUnavailableReason reason,
            PackageHouseCompilePackageMeasurements? packageMeasurements = null,
            PackageCompileAsset? payloadAsset = null)
            : base(result, realization)
        {
            Reason = reason;
            PackageMeasurements = packageMeasurements;
            PayloadAsset = payloadAsset;
        }

        public PackageHouseCompileSliceMeasurementUnavailableReason Reason
        { get; }

        public PackageHouseCompilePackageMeasurements? PackageMeasurements
        { get; }

        public PackageCompileAsset? PayloadAsset { get; }
    }
}

/// <summary>
/// Projects package and selected-Library sizes from one PackageHouse compile
/// realization without retaining the live payload.
/// </summary>
public static class PackageHouseCompileSliceMeasurementProjection
{
    public static PackageHouseCompileSliceMeasurementOutcome Project(
        PackageHouseSettlement.Acquired settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        PackageHouseResult result = settlement.Result;
        PackageHouseRealizationReceipt.Compile realization =
            result.Evidence.Realization
                as PackageHouseRealizationReceipt.Compile
            ?? throw new ArgumentException(
                "Compile-slice measurement requires a PackageHouse compile realization.",
                nameof(settlement));

        if (result is PackageHouseResult.Failed)
        {
            return new PackageHouseCompileSliceMeasurementOutcome.HouseFailure(
                result,
                realization);
        }

        if (!TryGetArchiveLength(
                settlement.Payload.Content,
                out long compressedPackageBytes,
                out PackageHouseCompileSliceMeasurementUnavailableReason
                    archiveFailure))
        {
            return new PackageHouseCompileSliceMeasurementOutcome.Unavailable(
                result,
                realization,
                archiveFailure);
        }
        var packageMeasurements =
            new PackageHouseCompilePackageMeasurements(
                realization,
                compressedPackageBytes);

        return realization.Selection.Status switch
        {
            PackageCompileAssetSelectionStatus.Selected =>
                Measure(
                    settlement,
                    realization,
                    packageMeasurements,
                    selectedEmpty: false),
            PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                Measure(
                    settlement,
                    realization,
                    packageMeasurements,
                    selectedEmpty: true),
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                new PackageHouseCompileSliceMeasurementOutcome.NoCompileSlices(
                    result,
                    realization,
                    packageMeasurements),
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                new PackageHouseCompileSliceMeasurementOutcome.NoApplicableSlice(
                    result,
                    realization,
                    packageMeasurements),
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                new PackageHouseCompileSliceMeasurementOutcome.InvalidSelection(
                    result,
                    realization,
                    packageMeasurements),
            _ => throw new InvalidOperationException(
                "Unknown compile asset-selection status."),
        };
    }

    private static PackageHouseCompileSliceMeasurementOutcome Measure(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseRealizationReceipt.Compile realization,
        PackageHouseCompilePackageMeasurements packageMeasurements,
        bool selectedEmpty)
    {
        if (settlement.Result is not PackageHouseResult.Settled)
        {
            throw new InvalidOperationException(
                "A selected compile realization requires a settled House result.");
        }

        ImmutableArray<string> selectedTargetFrameworkFolders =
            SelectTargetFrameworkFolders(
                settlement.Payload.Content,
                realization.Selection.TargetFramework!);
        var libraries =
            ImmutableArray.CreateBuilder<PackageHouseCompileLibraryMeasurement>(
                realization.Selection.Assets.Count);
        long selectedPayloadBytes = 0;
        if (!selectedEmpty)
        {
            if (settlement.Payload.Content
                is not IPackageContentEntryManifest manifest)
            {
                return new
                    PackageHouseCompileSliceMeasurementOutcome.Unavailable(
                        settlement.Result,
                        realization,
                        PackageHouseCompileSliceMeasurementUnavailableReason
                            .EntryManifestUnavailable,
                        packageMeasurements);
            }

            foreach (PackageCompileAsset compileAsset
                in realization.Selection.Assets)
            {
                PackageCompileAsset payloadAsset =
                    realization.Selection.FindImplementationAsset(compileAsset)
                    ?? compileAsset;
                if (!manifest.TryGetEntryLength(
                        payloadAsset.Path,
                        out long uncompressedBytes))
                {
                    return new
                        PackageHouseCompileSliceMeasurementOutcome.Unavailable(
                            settlement.Result,
                            realization,
                            PackageHouseCompileSliceMeasurementUnavailableReason
                                .SelectedEntryUnavailable,
                            packageMeasurements,
                            payloadAsset);
                }
                if (uncompressedBytes < 0)
                {
                    return new
                        PackageHouseCompileSliceMeasurementOutcome.Unavailable(
                            settlement.Result,
                            realization,
                            PackageHouseCompileSliceMeasurementUnavailableReason
                                .SelectedEntryLengthInvalid,
                            packageMeasurements,
                            payloadAsset);
                }

                try
                {
                    selectedPayloadBytes = checked(
                        selectedPayloadBytes + uncompressedBytes);
                }
                catch (OverflowException)
                {
                    return new
                        PackageHouseCompileSliceMeasurementOutcome.Unavailable(
                            settlement.Result,
                            realization,
                            PackageHouseCompileSliceMeasurementUnavailableReason
                                .SelectedPayloadBytesOverflow,
                            packageMeasurements,
                            payloadAsset);
                }

                libraries.Add(
                    new PackageHouseCompileLibraryMeasurement(
                        compileAsset,
                        payloadAsset,
                        uncompressedBytes));
            }
        }

        var measurements = new PackageHouseCompileSliceMeasurements(
            packageMeasurements,
            libraries.MoveToImmutable(),
            selectedTargetFrameworkFolders,
            selectedPayloadBytes);
        return selectedEmpty
            ? new PackageHouseCompileSliceMeasurementOutcome.SelectedEmpty(
                settlement.Result,
                realization,
                measurements)
            : new PackageHouseCompileSliceMeasurementOutcome.Measured(
                settlement.Result,
                realization,
                measurements);
    }

    /// <summary>
    /// Lists top-level package folders containing the selected target
    /// framework.
    /// </summary>
    public static ImmutableArray<string> SelectTargetFrameworkFolders(
        IPackageContent content,
        string selectedTargetFramework)
    {
        var folders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string entry in content.EnumerateEntries())
        {
            if (string.IsNullOrWhiteSpace(entry)
                || entry.Contains('\\'))
            {
                continue;
            }

            string[] segments = entry.Split('/');
            if (segments.Length < 3
                || segments.Any(string.IsNullOrEmpty)
                || segments[^1].Equals("_._", StringComparison.Ordinal))
            {
                continue;
            }

            for (int index = 1; index < segments.Length - 1; index++)
            {
                if (!segments[index].Equals(
                        selectedTargetFramework,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                folders.Add(segments[0]);
                break;
            }
        }

        return folders
            .OrderBy(static folder => folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static folder => folder, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Reads the retained package archive length without consuming content
    /// ownership.
    /// </summary>
    public static bool TryGetArchiveLength(
        IPackageContent content,
        out long length,
        out PackageHouseCompileSliceMeasurementUnavailableReason failure)
    {
        Stream? archive = null;
        try
        {
            if (!content.TryOpenArchive(out archive))
            {
                length = 0;
                failure =
                    PackageHouseCompileSliceMeasurementUnavailableReason
                        .ArchiveUnavailable;
                return false;
            }
            using (archive)
            {
                if (!archive.CanSeek)
                {
                    length = 0;
                    failure =
                        PackageHouseCompileSliceMeasurementUnavailableReason
                            .ArchiveLengthUnavailable;
                    return false;
                }

                length = archive.Length;
                if (length < 0)
                {
                    failure =
                        PackageHouseCompileSliceMeasurementUnavailableReason
                            .ArchiveLengthUnavailable;
                    return false;
                }
            }
        }
        catch (IOException)
        {
            length = 0;
            failure =
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .ArchiveUnavailable;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            length = 0;
            failure =
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .ArchiveUnavailable;
            return false;
        }
        catch (NotSupportedException)
        {
            length = 0;
            failure =
                PackageHouseCompileSliceMeasurementUnavailableReason
                    .ArchiveLengthUnavailable;
            return false;
        }

        failure = default;
        return true;
    }
}
