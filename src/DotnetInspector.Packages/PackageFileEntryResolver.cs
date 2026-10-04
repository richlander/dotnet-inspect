namespace DotnetInspector.Packages;

internal enum PackageFileEntryResolutionStatus
{
    Resolved,
    ManifestUnavailable,
    Missing,
    Ambiguous,
}

internal sealed record PackageFileEntryResolution(
    PackageFileEntryResolutionStatus Status,
    PackageContentEntry? Entry)
{
    public static PackageFileEntryResolution Resolved(
        PackageContentEntry entry) =>
        new(
            PackageFileEntryResolutionStatus.Resolved,
            entry);

    public static PackageFileEntryResolution Unavailable(
        PackageFileEntryResolutionStatus status)
    {
        if (status == PackageFileEntryResolutionStatus.Resolved)
        {
            throw new ArgumentException(
                "An unavailable package file entry cannot be resolved.",
                nameof(status));
        }

        return new(status, Entry: null);
    }
}

internal static class PackageFileEntryResolver
{
    public static PackageFileEntryResolution Resolve(
        PackageHouseSettlement.Acquired settlement,
        string path)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        string normalizedPath =
            PackageFileDemand.Create([path]).Entries.Single();
        IReadOnlyList<PackageContentEntry>? entries =
            settlement.Result.Evidence.FileList?.Entries;
        if (entries is null
            && settlement.Payload.Content
                is IPackageContentEntryManifest manifest)
        {
            entries = manifest.EnumerateEntriesWithLengths();
        }
        if (entries is null)
        {
            return PackageFileEntryResolution.Unavailable(
                PackageFileEntryResolutionStatus.ManifestUnavailable);
        }

        PackageContentEntry[] matches =
        [
            .. entries
                .Where(entry => entry.Path.Equals(
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2),
        ];
        return matches.Length switch
        {
            0 => PackageFileEntryResolution.Unavailable(
                PackageFileEntryResolutionStatus.Missing),
            1 => PackageFileEntryResolution.Resolved(matches[0]),
            _ => PackageFileEntryResolution.Unavailable(
                PackageFileEntryResolutionStatus.Ambiguous),
        };
    }
}
