using System.Collections.Immutable;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// The package layout whose selected target-framework slice Package Info
/// measures.
/// </summary>
public enum PackageInfoSliceProfile
{
    Compile,
    Tool,
}

/// <summary>
/// A structural reason why one Package Info measurement could not be issued.
/// </summary>
public enum PackageInfoMeasurementFailureKind
{
    ArchiveUnavailable,
    ArchiveLengthUnavailable,
    EntryManifestUnavailable,
    NoTargetFrameworks,
    NoMatchingTargetFramework,
    SelectionRejected,
    AmbiguousSelectedEntry,
    SelectedEntryUnavailable,
    InvalidEntryLength,
    SizeOverflow,
}

/// <summary>One typed Package Info measurement failure.</summary>
public sealed record PackageInfoMeasurementFailure(
    PackageInfoMeasurementFailureKind Kind,
    string Message);

/// <summary>The compressed size of the retained package archive.</summary>
public abstract record PackageArchiveSizeMeasurement
{
    private PackageArchiveSizeMeasurement()
    {
    }

    public sealed record Available(long Bytes) : PackageArchiveSizeMeasurement;

    public sealed record Unavailable(PackageInfoMeasurementFailure Failure)
        : PackageArchiveSizeMeasurement;
}

/// <summary>
/// The aggregate measurement for one selected package target-framework slice.
/// </summary>
public abstract record PackageSelectedTfmMeasurement
{
    private PackageSelectedTfmMeasurement()
    {
    }

    public sealed record Available(
        string TargetFramework,
        long UncompressedSize,
        int LibraryCount,
        ImmutableArray<string> MeasuredEntries)
        : PackageSelectedTfmMeasurement;

    public sealed record Unavailable(PackageInfoMeasurementFailure Failure)
        : PackageSelectedTfmMeasurement;

    public sealed record Invalid(PackageInfoMeasurementFailure Failure)
        : PackageSelectedTfmMeasurement;
}

/// <summary>
/// Resource-free evidence for Package Info measurements over one retained
/// package-content generation.
/// </summary>
public sealed record PackageInfoMeasurementReceipt(
    PackageContentGenerationIdentity Generation,
    PackageInfoSliceProfile Profile,
    string? RequestedTargetFramework,
    ImmutableArray<string> AvailableTargetFrameworks,
    PackageArchiveSizeMeasurement ArchiveSize,
    PackageSelectedTfmMeasurement SelectedTargetFramework,
    PackageCompileAssetSelectionReceipt? CompileSelection);

/// <summary>
/// Produces whole-package and selected-target-framework measurements without
/// opening inspected assemblies.
/// </summary>
public static class PackageInfoMeasurementQuery
{
    public static PackageInfoMeasurementReceipt Evaluate(
        IPackageContent content,
        string packageId,
        PackageInfoSliceProfile profile,
        string? requestedTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        PackageArchiveSizeMeasurement archiveSize = MeasureArchive(content);
        return profile switch
        {
            PackageInfoSliceProfile.Compile =>
                EvaluateCompile(
                    content,
                    packageId,
                    requestedTargetFramework,
                    archiveSize),
            PackageInfoSliceProfile.Tool =>
                EvaluateTool(
                    content,
                    requestedTargetFramework,
                    archiveSize),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
    }

    private static PackageInfoMeasurementReceipt EvaluateCompile(
        IPackageContent content,
        string packageId,
        string? requestedTargetFramework,
        PackageArchiveSizeMeasurement archiveSize)
    {
        PackageCompileAssetSelectionPolicy policy =
            requestedTargetFramework is null
                ? PackageCompileAssetSelectionPolicy.HighestAvailable
                : PackageCompileAssetSelectionPolicy.ExplicitTarget;
        PackageCompileAssetSelectionReceipt selectionReceipt =
            PackageCompileAssetSelector.Evaluate(
                content,
                packageId,
                policy,
                requestedTargetFramework);
        PackageCompileAssetSelection selection = selectionReceipt.Selection;
        ImmutableArray<string> frameworks =
            [.. selection.AvailableTargetFrameworks];

        PackageSelectedTfmMeasurement measurement = selection.Status switch
        {
            PackageCompileAssetSelectionStatus.Selected =>
                MeasureCompileSelection(content, selection),
            PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                MeasureSelectedEntries(
                    content,
                    selection.TargetFramework!,
                    libraryCount: 0,
                    selectedEntries: []),
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                Unavailable(
                    PackageInfoMeasurementFailureKind
                        .NoMatchingTargetFramework,
                    "The requested target framework has no applicable package slice."),
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                Unavailable(
                    PackageInfoMeasurementFailureKind.NoTargetFrameworks,
                    "The package contains no compile target-framework slice."),
            _ => Invalid(
                PackageInfoMeasurementFailureKind.SelectionRejected,
                selection.Message
                    ?? "Package compile asset selection was rejected."),
        };

        return new PackageInfoMeasurementReceipt(
            content.GenerationIdentity,
            PackageInfoSliceProfile.Compile,
            requestedTargetFramework,
            frameworks,
            archiveSize,
            measurement,
            selectionReceipt);
    }

    private static PackageSelectedTfmMeasurement MeasureCompileSelection(
        IPackageContent content,
        PackageCompileAssetSelection selection)
    {
        if (selection.Assets
            .GroupBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            return Invalid(
                PackageInfoMeasurementFailureKind.AmbiguousSelectedEntry,
                "The selected compile slice contains ambiguous source entry paths.");
        }

        return MeasureSelectedEntries(
            content,
            selection.TargetFramework!,
            selection.Assets.Count,
            [
                .. selection.Assets.Select(asset =>
                    (selection.FindImplementationAsset(asset) ?? asset).Path)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ]);
    }

    private static PackageInfoMeasurementReceipt EvaluateTool(
        IPackageContent content,
        string? requestedTargetFramework,
        PackageArchiveSizeMeasurement archiveSize)
    {
        ToolAsset[] assets =
        [
            .. content.EnumerateEntries()
                .Select(ParseToolAsset)
                .OfType<ToolAsset>()
                .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.Ordinal),
        ];
        ImmutableArray<string> frameworks =
        [
            .. assets
                .Select(asset => asset.TargetFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(framework =>
                    TfmResolver.GetTfmPriority(framework.ToLowerInvariant()))
                .ThenBy(framework => framework, StringComparer.OrdinalIgnoreCase)
                .ThenBy(framework => framework, StringComparer.Ordinal),
        ];

        if (frameworks.IsEmpty)
        {
            return new PackageInfoMeasurementReceipt(
                content.GenerationIdentity,
                PackageInfoSliceProfile.Tool,
                requestedTargetFramework,
                frameworks,
                archiveSize,
                Unavailable(
                    PackageInfoMeasurementFailureKind.NoTargetFrameworks,
                    "The package contains no tool target-framework Library slice."),
                CompileSelection: null);
        }

        string? selectedFramework = requestedTargetFramework is null
            ? frameworks[0]
            : PackageCompileAssetSelector.SelectApplicableFramework(
                frameworks,
                requestedTargetFramework);
        PackageSelectedTfmMeasurement measurement;
        if (selectedFramework is null)
        {
            measurement = Unavailable(
                PackageInfoMeasurementFailureKind.NoMatchingTargetFramework,
                "The requested target framework has no applicable tool slice.");
        }
        else
        {
            string[] selectedEntries =
            [
                .. assets
                    .Where(asset => asset.TargetFramework.Equals(
                        selectedFramework,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(asset => asset.Path),
            ];
            measurement = MeasureSelectedEntries(
                content,
                selectedFramework,
                selectedEntries.Length,
                selectedEntries);
        }

        return new PackageInfoMeasurementReceipt(
            content.GenerationIdentity,
            PackageInfoSliceProfile.Tool,
            requestedTargetFramework,
            frameworks,
            archiveSize,
            measurement,
            CompileSelection: null);
    }

    private static PackageArchiveSizeMeasurement MeasureArchive(
        IPackageContent content)
    {
        if (!content.TryOpenArchive(out Stream? archive))
        {
            return new PackageArchiveSizeMeasurement.Unavailable(
                Failure(
                    PackageInfoMeasurementFailureKind.ArchiveUnavailable,
                    "The retained package archive is unavailable."));
        }

        using (archive)
        {
            if (!archive.CanSeek)
            {
                return new PackageArchiveSizeMeasurement.Unavailable(
                    Failure(
                        PackageInfoMeasurementFailureKind
                            .ArchiveLengthUnavailable,
                        "The retained package archive does not expose its length."));
            }

            return new PackageArchiveSizeMeasurement.Available(archive.Length);
        }
    }

    private static PackageSelectedTfmMeasurement MeasureSelectedEntries(
        IPackageContent content,
        string targetFramework,
        int libraryCount,
        IReadOnlyList<string> selectedEntries)
    {
        if (content is not IPackageContentEntryManifest manifest)
        {
            return Unavailable(
                PackageInfoMeasurementFailureKind.EntryManifestUnavailable,
                "The package content does not expose uncompressed entry lengths.");
        }

        if (selectedEntries
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            return Invalid(
                PackageInfoMeasurementFailureKind.AmbiguousSelectedEntry,
                "The selected package slice contains ambiguous entry paths.");
        }

        ImmutableArray<string> measuredEntries =
            [.. selectedEntries.Order(StringComparer.Ordinal)];
        try
        {
            long size = 0;
            foreach (string entry in measuredEntries)
            {
                if (!manifest.TryGetEntryLength(entry, out long length))
                {
                    return Invalid(
                        PackageInfoMeasurementFailureKind
                            .SelectedEntryUnavailable,
                        "A selected package entry has no declared uncompressed length.");
                }
                if (length < 0)
                {
                    return Invalid(
                        PackageInfoMeasurementFailureKind.InvalidEntryLength,
                        "A selected package entry has a negative uncompressed length.");
                }

                size = checked(size + length);
            }

            return new PackageSelectedTfmMeasurement.Available(
                targetFramework,
                size,
                libraryCount,
                measuredEntries);
        }
        catch (OverflowException)
        {
            return Invalid(
                PackageInfoMeasurementFailureKind.SizeOverflow,
                "The selected package slice exceeds the supported size range.");
        }
    }

    private static ToolAsset? ParseToolAsset(string entry)
    {
        string[] parts = entry.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !parts[0].Equals("tools", StringComparison.OrdinalIgnoreCase)
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

    private static PackageSelectedTfmMeasurement.Unavailable Unavailable(
        PackageInfoMeasurementFailureKind kind,
        string message) =>
        new(Failure(kind, message));

    private static PackageSelectedTfmMeasurement.Invalid Invalid(
        PackageInfoMeasurementFailureKind kind,
        string message) =>
        new(Failure(kind, message));

    private static PackageInfoMeasurementFailure Failure(
        PackageInfoMeasurementFailureKind kind,
        string message) =>
        new(kind, message);

    private sealed record ToolAsset(string Path, string TargetFramework);
}
