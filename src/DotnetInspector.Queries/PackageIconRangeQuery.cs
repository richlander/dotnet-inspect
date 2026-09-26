using DotnetInspector.Packages;
using NuGetFetch;
using ZipFetch;

namespace DotnetInspector.Queries;

/// <summary>The result of reading one package icon through archive ranges.</summary>
public abstract record PackageIconRangeResult
{
    private PackageIconRangeResult()
    {
    }

    /// <summary>The range read completed and produced the ordinary icon outcome.</summary>
    public sealed record Completed(PackageIconResult Icon)
        : PackageIconRangeResult;

    /// <summary>The package source failed while opening or reading the archive.</summary>
    public sealed record Failed(PackageSourceFailure Failure)
        : PackageIconRangeResult;

    /// <summary>The source or archive refused ranged access.</summary>
    public sealed record Refused(PackageArchiveReadRefusal Reason)
        : PackageIconRangeResult;
}

/// <summary>
/// Reads one embedded package icon through the archive directory and exact
/// entry ranges without falling back to a complete package download.
/// </summary>
public static class PackageIconRangeQuery
{
    public static InspectionQuery<PackageIconRangeResult> Definition { get; } =
        new("Package icon range read", InspectionCost.Moderated);

    public static async Task<PackageIconRangeResult> ExecuteAsync(
        IPackageArchiveRangeSource source,
        PackageSourceCoordinate coordinate,
        ZipReadLimits limits,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null,
        PackageArchiveRequestLog? requestLog = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(limits);

        PackageArchiveReadResult<PackageArchiveReader> open =
            await source.OpenArchiveAsync(
                    coordinate.PackageId,
                    coordinate.Version,
                    limits,
                    cancellationToken,
                    operationContext,
                    requestLog)
                .ConfigureAwait(false);
        if (TryMap(open, out PackageIconRangeResult openFailure))
            return openFailure;

        await using PackageArchiveReader reader = open.Value!;
        if (reader.Coordinate != coordinate)
        {
            throw new InvalidOperationException(
                "The package archive reader returned another coordinate.");
        }

        string? manifestPath;
        try
        {
            manifestPath = PackageManifestContent.FindRootManifest(
                reader.Directory.Entries.Select(static entry => entry.Name));
        }
        catch (InvalidDataException)
        {
            return CompletedUnavailable(
                PackageIconUnavailableReason.InvalidManifest);
        }

        if (manifestPath is null)
            return new PackageIconRangeResult.Completed(
                new PackageIconResult.Missing());

        ZipEntry manifestEntry = reader.Directory.Find(manifestPath)
            ?? throw new InvalidOperationException(
                "The selected package manifest is absent from its archive directory.");
        if (manifestEntry.ExpandedLength
            > PackageManifestFactsQuery.MaxManifestBytes)
        {
            return CompletedUnavailable(
                PackageIconUnavailableReason.InvalidManifest);
        }

        PackageArchiveReadResult<PackageArchiveEntryContent> manifestRead =
            await reader.ReadEntryAsync(
                    manifestEntry,
                    PackageManifestFactsQuery.MaxManifestBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        if (TryMap(manifestRead, out PackageIconRangeResult manifestFailure))
            return manifestFailure;

        PackageIconResult? manifestResult =
            PackageIconQuery.InspectManifest(
                manifestRead.Value!.Content,
                coordinate,
                out string? iconPath);
        if (manifestResult is not null)
            return new PackageIconRangeResult.Completed(manifestResult);

        ZipEntry? iconEntry = reader.Directory.Find(iconPath!);
        if (iconEntry is null)
        {
            return CompletedUnavailable(
                PackageIconUnavailableReason.MissingEntry);
        }
        if (iconEntry.ExpandedLength > PackageIconQuery.MaxIconBytes)
        {
            return CompletedUnavailable(
                PackageIconUnavailableReason.ConfiguredLimitExceeded);
        }

        PackageArchiveReadResult<PackageArchiveEntryContent> iconRead =
            await reader.ReadEntryAsync(
                    iconEntry,
                    PackageIconQuery.MaxIconBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        if (TryMap(iconRead, out PackageIconRangeResult iconFailure))
            return iconFailure;

        return new PackageIconRangeResult.Completed(
            PackageIconQuery.InspectImage(iconRead.Value!.Content.Span));
    }

    private static PackageIconRangeResult CompletedUnavailable(
        PackageIconUnavailableReason reason) =>
        new PackageIconRangeResult.Completed(
            new PackageIconResult.Unavailable(reason));

    private static bool TryMap<T>(
        PackageArchiveReadResult<T> result,
        out PackageIconRangeResult failure)
        where T : class
    {
        if (result.Failure is { } sourceFailure)
        {
            failure = new PackageIconRangeResult.Failed(sourceFailure);
            return true;
        }
        if (result.Refusal is { } refusal)
        {
            failure = new PackageIconRangeResult.Refused(refusal);
            return true;
        }
        if (result.Value is null)
        {
            throw new InvalidOperationException(
                "A package archive read returned no value, failure, or refusal.");
        }

        failure = null!;
        return false;
    }
}
