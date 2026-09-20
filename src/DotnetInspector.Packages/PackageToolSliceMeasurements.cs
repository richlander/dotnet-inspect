using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// The exact retained payload generation whose nuspec declares a supported
/// .NET tool package type.
/// </summary>
public sealed class PackageToolDeclarationEvidence
{
    private PackageToolDeclarationEvidence(
        PackageSourceCoordinate coordinate,
        PackageContentGenerationIdentity generation,
        string packageType)
    {
        Coordinate = coordinate;
        Generation = generation;
        PackageType = packageType;
    }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageContentGenerationIdentity Generation { get; }

    public string PackageType { get; }

    public static async ValueTask<PackageToolDeclarationEvidence?> TryCreateAsync(
        AcquiredPackageSourcePayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string[] nuspecEntries =
        [
            .. payload.Content.EnumerateEntries()
                .Where(static entry =>
                    !string.IsNullOrWhiteSpace(entry)
                    && !entry.Contains('/')
                    && !entry.Contains('\\')
                    && entry.EndsWith(
                        ".nuspec",
                        StringComparison.OrdinalIgnoreCase))
                .Take(2),
        ];
        if (nuspecEntries.Length != 1
            || !payload.Content.TryOpenEntry(
                nuspecEntries[0],
                PackageExtractor.MaxNuspecBytes,
                out Stream? stream))
        {
            return null;
        }

        await using (stream)
        {
            byte[]? bytes = await PackageContentAdmission.ReadBoundedAsync(
                    stream,
                    PackageExtractor.MaxNuspecBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            return bytes is null
                ? null
                : TryCreate(
                    payload.Coordinate,
                    payload.Content,
                    bytes);
        }
    }

    public static PackageToolDeclarationEvidence? TryCreate(
        PackageSourceCoordinate coordinate,
        IPackageContent content,
        ReadOnlyMemory<byte> nuspecBytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        string? packageType = ParseDeclaredToolPackageType(
            nuspecBytes,
            coordinate);
        return packageType is null
            ? null
            : new(coordinate, content.GenerationIdentity, packageType);
    }

    private static string? ParseDeclaredToolPackageType(
        ReadOnlyMemory<byte> bytes,
        PackageSourceCoordinate coordinate)
    {
        try
        {
            using var buffer = new MemoryStream(
                bytes.ToArray(),
                writable: false);
            using XmlReader reader = XmlReader.Create(
                buffer,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument =
                        PackageExtractor.MaxNuspecBytes,
                });
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            if (!PackageExtractor.TryGetExpectedNuspecMetadata(
                    document,
                    coordinate.PackageId,
                    coordinate.Version,
                    out XElement? metadata))
            {
                return null;
            }

            XNamespace nuspecNamespace = metadata.Name.Namespace;
            XElement[] packageTypeContainers =
            [
                .. metadata.Elements()
                    .Where(element =>
                        element.Name.Namespace == nuspecNamespace
                        && element.Name.LocalName == "packageTypes")
                    .Take(2),
            ];
            if (packageTypeContainers.Length != 1)
                return null;

            return packageTypeContainers[0].Elements()
                .Where(element =>
                    element.Name.Namespace == nuspecNamespace
                    && element.Name.LocalName == "packageType")
                .Select(element => element.Attribute("name")?.Value)
                .FirstOrDefault(IsDeclaredToolPackageType);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static bool IsDeclaredToolPackageType(string? packageType) =>
        packageType is not null
        && (packageType.Equals(
                "DotnetTool",
                StringComparison.OrdinalIgnoreCase)
            || packageType.Equals(
                "DotnetToolRidPackage",
                StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Why one selected tool-slice measurement could not be completed.
/// </summary>
public enum PackageToolSliceMeasurementUnavailableReason
{
    ArchiveUnavailable,
    ArchiveLengthUnavailable,
    EntryManifestUnavailable,
    SelectedEntryUnavailable,
    SelectedEntryLengthInvalid,
    SelectedPayloadBytesOverflow,
}

/// <summary>
/// Resource-free correspondence for one retained tool-package generation.
/// </summary>
public sealed class PackageToolSliceMeasurementEvidence
{
    internal PackageToolSliceMeasurementEvidence(
        PackageToolDeclarationEvidence declaration,
        string? requestedTargetFramework,
        ImmutableArray<string> availableTargetFrameworks)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (availableTargetFrameworks.IsDefault)
        {
            throw new ArgumentException(
                "Available tool target frameworks must be initialized.",
                nameof(availableTargetFrameworks));
        }

        Declaration = declaration;
        RequestedTargetFramework = requestedTargetFramework;
        AvailableTargetFrameworks = availableTargetFrameworks;
    }

    public PackageToolDeclarationEvidence Declaration { get; }

    public PackageSourceCoordinate Coordinate => Declaration.Coordinate;

    public PackageContentGenerationIdentity Generation =>
        Declaration.Generation;

    public string? RequestedTargetFramework { get; }

    public ImmutableArray<string> AvailableTargetFrameworks { get; }
}

/// <summary>
/// Resource-free package measurements associated with one tool payload.
/// </summary>
public sealed class PackageToolPackageMeasurements
{
    internal PackageToolPackageMeasurements(
        PackageToolSliceMeasurementEvidence evidence,
        long compressedPackageBytes)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentOutOfRangeException.ThrowIfNegative(compressedPackageBytes);

        Evidence = evidence;
        CompressedPackageBytes = compressedPackageBytes;
    }

    public PackageToolSliceMeasurementEvidence Evidence { get; }

    public PackageSourceCoordinate Coordinate => Evidence.Coordinate;

    public PackageContentGenerationIdentity Generation => Evidence.Generation;

    public ImmutableArray<string> AvailableTargetFrameworks =>
        Evidence.AvailableTargetFrameworks;

    public int AvailableTargetFrameworkCount =>
        AvailableTargetFrameworks.Length;

    public long CompressedPackageBytes { get; }
}

/// <summary>
/// Resource-free measurements for one selected tool target-framework slice.
/// </summary>
public sealed class PackageToolSliceMeasurements
{
    internal PackageToolSliceMeasurements(
        PackageToolPackageMeasurements package,
        string selectedTargetFramework,
        ImmutableArray<string> selectedTargetFrameworkFolders,
        ImmutableArray<string> selectedEntries,
        long selectedLibraryPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedTargetFramework);
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
        if (selectedEntries.IsDefault
            || selectedEntries.Any(string.IsNullOrWhiteSpace)
            || selectedEntries
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != selectedEntries.Length)
        {
            throw new ArgumentException(
                "Selected tool entries must be initialized, non-empty paths without case-insensitive duplicates.",
                nameof(selectedEntries));
        }

        Package = package;
        SelectedTargetFramework = selectedTargetFramework;
        SelectedTargetFrameworkFolders = selectedTargetFrameworkFolders;
        SelectedEntries = selectedEntries;
        SelectedLibraryPayloadBytes = selectedLibraryPayloadBytes;
    }

    public PackageToolPackageMeasurements Package { get; }

    public PackageToolSliceMeasurementEvidence Evidence => Package.Evidence;

    public PackageSourceCoordinate Coordinate => Package.Coordinate;

    public PackageContentGenerationIdentity Generation => Package.Generation;

    public string SelectedTargetFramework { get; }

    public ImmutableArray<string> AvailableTargetFrameworks =>
        Package.AvailableTargetFrameworks;

    public int AvailableTargetFrameworkCount =>
        AvailableTargetFrameworks.Length;

    public long CompressedPackageBytes => Package.CompressedPackageBytes;

    public ImmutableArray<string> SelectedTargetFrameworkFolders { get; }

    public ImmutableArray<string> SelectedEntries { get; }

    public int SelectedLibraryCount => SelectedEntries.Length;

    public long SelectedLibraryPayloadBytes { get; }
}

/// <summary>
/// The typed result of projecting Package Info measurements from one acquired
/// tool payload.
/// </summary>
public abstract class PackageToolSliceMeasurementOutcome
{
    private protected PackageToolSliceMeasurementOutcome(
        PackageToolSliceMeasurementEvidence evidence) =>
        Evidence = evidence
            ?? throw new ArgumentNullException(nameof(evidence));

    public PackageToolSliceMeasurementEvidence Evidence { get; }

    public sealed class Measured : PackageToolSliceMeasurementOutcome
    {
        internal Measured(PackageToolSliceMeasurements measurements)
            : base(measurements?.Evidence
                ?? throw new ArgumentNullException(nameof(measurements))) =>
            Measurements = measurements;

        public PackageToolSliceMeasurements Measurements { get; }
    }

    public sealed class SelectedEmpty : PackageToolSliceMeasurementOutcome
    {
        internal SelectedEmpty(PackageToolSliceMeasurements measurements)
            : base(measurements?.Evidence
                ?? throw new ArgumentNullException(nameof(measurements))) =>
            Measurements = measurements;

        public PackageToolSliceMeasurements Measurements { get; }
    }

    public sealed class NoToolSlices : PackageToolSliceMeasurementOutcome
    {
        internal NoToolSlices(PackageToolPackageMeasurements measurements)
            : base(measurements?.Evidence
                ?? throw new ArgumentNullException(nameof(measurements))) =>
            Measurements = measurements;

        public PackageToolPackageMeasurements Measurements { get; }
    }

    public sealed class NoApplicableSlice :
        PackageToolSliceMeasurementOutcome
    {
        internal NoApplicableSlice(PackageToolPackageMeasurements measurements)
            : base(measurements?.Evidence
                ?? throw new ArgumentNullException(nameof(measurements))) =>
            Measurements = measurements;

        public PackageToolPackageMeasurements Measurements { get; }
    }

    public sealed class InvalidSelection :
        PackageToolSliceMeasurementOutcome
    {
        internal InvalidSelection(
            PackageToolPackageMeasurements measurements,
            string reason)
            : base(measurements?.Evidence
                ?? throw new ArgumentNullException(nameof(measurements)))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Measurements = measurements;
            Reason = reason;
        }

        public PackageToolPackageMeasurements Measurements { get; }

        public string Reason { get; }
    }

    public sealed class Unavailable : PackageToolSliceMeasurementOutcome
    {
        internal Unavailable(
            PackageToolSliceMeasurementEvidence evidence,
            PackageToolSliceMeasurementUnavailableReason reason,
            PackageToolPackageMeasurements? packageMeasurements = null,
            string? selectedEntry = null)
            : base(evidence)
        {
            Reason = reason;
            PackageMeasurements = packageMeasurements;
            SelectedEntry = selectedEntry;
        }

        public PackageToolSliceMeasurementUnavailableReason Reason { get; }

        public PackageToolPackageMeasurements? PackageMeasurements { get; }

        public string? SelectedEntry { get; }
    }
}

/// <summary>
/// Projects the aggregate target-framework payload of one declared tool
/// package without opening inspected assemblies.
/// </summary>
public static class PackageToolSliceMeasurementProjection
{
    public static PackageToolSliceMeasurementOutcome Project(
        AcquiredPackageSourcePayload payload,
        PackageToolDeclarationEvidence declaration,
        string? requestedTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Project(
            payload.Coordinate,
            payload.Content,
            declaration,
            requestedTargetFramework);
    }

    public static PackageToolSliceMeasurementOutcome Project(
        PackageSourceCoordinate coordinate,
        IPackageContent content,
        PackageToolDeclarationEvidence declaration,
        string? requestedTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(declaration);
        if (!coordinate.Equals(declaration.Coordinate)
            || !ReferenceEquals(
                content.GenerationIdentity,
                declaration.Generation))
        {
            throw new ArgumentException(
                "Tool declaration evidence must describe the exact acquired payload generation.",
                nameof(declaration));
        }

        string[] entries = [.. content.EnumerateEntries()];
        ImmutableArray<string> frameworks =
        [
            .. entries
                .Select(ParseToolFramework)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static framework =>
                    TfmResolver.GetTfmPriority(framework.ToLowerInvariant()))
                .ThenBy(static framework => framework, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static framework => framework, StringComparer.Ordinal),
        ];
        var evidence = new PackageToolSliceMeasurementEvidence(
            declaration,
            requestedTargetFramework,
            frameworks);

        if (!PackageHouseCompileSliceMeasurementProjection.TryGetArchiveLength(
                content,
                out long compressedPackageBytes,
                out PackageHouseCompileSliceMeasurementUnavailableReason
                    archiveFailure))
        {
            return new PackageToolSliceMeasurementOutcome.Unavailable(
                evidence,
                Map(archiveFailure));
        }
        var packageMeasurements = new PackageToolPackageMeasurements(
            evidence,
            compressedPackageBytes);

        if (frameworks.IsEmpty)
        {
            return new PackageToolSliceMeasurementOutcome.NoToolSlices(
                packageMeasurements);
        }

        string? selectedFramework = requestedTargetFramework is null
            ? frameworks[0]
            : PackageCompileAssetSelector.SelectApplicableFramework(
                frameworks,
                requestedTargetFramework);
        if (selectedFramework is null)
        {
            return new PackageToolSliceMeasurementOutcome.NoApplicableSlice(
                packageMeasurements);
        }

        string[] selectedEntries =
        [
            .. entries
                .Select(ParseToolAsset)
                .OfType<ToolAsset>()
                .Where(asset => asset.TargetFramework.Equals(
                    selectedFramework,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static asset => asset.Path),
        ];
        if (selectedEntries
            .GroupBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1))
        {
            return new PackageToolSliceMeasurementOutcome.InvalidSelection(
                packageMeasurements,
                "The selected tool slice contains ambiguous entry paths.");
        }
        if (content is not IPackageContentEntryManifest manifest)
        {
            return new PackageToolSliceMeasurementOutcome.Unavailable(
                evidence,
                PackageToolSliceMeasurementUnavailableReason
                    .EntryManifestUnavailable,
                packageMeasurements);
        }

        long selectedPayloadBytes = 0;
        foreach (string entry in selectedEntries)
        {
            if (!manifest.TryGetEntryLength(entry, out long entryLength))
            {
                return new PackageToolSliceMeasurementOutcome.Unavailable(
                    evidence,
                    PackageToolSliceMeasurementUnavailableReason
                        .SelectedEntryUnavailable,
                    packageMeasurements,
                    entry);
            }
            if (entryLength < 0)
            {
                return new PackageToolSliceMeasurementOutcome.Unavailable(
                    evidence,
                    PackageToolSliceMeasurementUnavailableReason
                        .SelectedEntryLengthInvalid,
                    packageMeasurements,
                    entry);
            }

            try
            {
                selectedPayloadBytes = checked(
                    selectedPayloadBytes + entryLength);
            }
            catch (OverflowException)
            {
                return new PackageToolSliceMeasurementOutcome.Unavailable(
                    evidence,
                    PackageToolSliceMeasurementUnavailableReason
                        .SelectedPayloadBytesOverflow,
                    packageMeasurements,
                    entry);
            }
        }

        var measurements = new PackageToolSliceMeasurements(
            packageMeasurements,
            selectedFramework,
            PackageHouseCompileSliceMeasurementProjection
                .SelectTargetFrameworkFolders(
                    content,
                    selectedFramework),
            [.. selectedEntries.Order(StringComparer.Ordinal)],
            selectedPayloadBytes);
        return selectedEntries.Length == 0
            ? new PackageToolSliceMeasurementOutcome.SelectedEmpty(
                measurements)
            : new PackageToolSliceMeasurementOutcome.Measured(measurements);
    }

    private static string? ParseToolFramework(string entry)
    {
        if (!PackageCompileAssetSelector.TryParsePathParts(
                entry,
                out string[]? parts))
        {
            return null;
        }

        return parts![0].Equals(
                "tools",
                StringComparison.OrdinalIgnoreCase)
            && TfmResolver.IsTfmLike(parts[1])
                ? parts[1]
                : null;
    }

    private static ToolAsset? ParseToolAsset(string entry)
    {
        if (!PackageCompileAssetSelector.TryParsePathParts(
                entry,
                out string[]? parts)
            || !parts![0].Equals("tools", StringComparison.OrdinalIgnoreCase)
            || !TfmResolver.IsTfmLike(parts[1])
            || !parts[^1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(parts[^1]))
            || IsNativeRuntimeAsset(parts))
        {
            return null;
        }

        if (parts[^1].EndsWith(
                ".resources.dll",
                StringComparison.OrdinalIgnoreCase)
            && parts.Length >= 4
            && TfmResolver.IsCultureFolderName(parts[^2]))
        {
            return null;
        }

        return new ToolAsset(entry, parts[1]);
    }

    private static bool IsNativeRuntimeAsset(IReadOnlyList<string> parts)
    {
        for (int index = 2; index + 2 < parts.Count; index++)
        {
            if (parts[index].Equals(
                    "runtimes",
                    StringComparison.OrdinalIgnoreCase)
                && parts[index + 2].Equals(
                    "native",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static PackageToolSliceMeasurementUnavailableReason Map(
        PackageHouseCompileSliceMeasurementUnavailableReason reason) =>
        reason switch
        {
            PackageHouseCompileSliceMeasurementUnavailableReason
                .ArchiveUnavailable =>
                PackageToolSliceMeasurementUnavailableReason
                    .ArchiveUnavailable,
            PackageHouseCompileSliceMeasurementUnavailableReason
                .ArchiveLengthUnavailable =>
                PackageToolSliceMeasurementUnavailableReason
                    .ArchiveLengthUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private sealed record ToolAsset(string Path, string TargetFramework);
}
