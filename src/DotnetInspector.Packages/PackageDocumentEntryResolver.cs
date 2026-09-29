namespace DotnetInspector.Packages;

public enum PackageDocumentEntryResolutionStatus
{
    Resolved,
    ManifestUnavailable,
    Missing,
    Ambiguous,
}

public sealed record PackageDocumentEntryResolution(
    PackageDocumentEntryResolutionStatus Status,
    PackageContentEntry? Entry)
{
    public static PackageDocumentEntryResolution Resolved(
        PackageContentEntry entry) =>
        new(
            PackageDocumentEntryResolutionStatus.Resolved,
            entry);

    public static PackageDocumentEntryResolution Unavailable(
        PackageDocumentEntryResolutionStatus status)
    {
        if (status == PackageDocumentEntryResolutionStatus.Resolved)
        {
            throw new ArgumentException(
                "An unavailable package document entry cannot be resolved.",
                nameof(status));
        }

        return new(status, Entry: null);
    }
}

public static class PackageDocumentEntryResolver
{
    public static PackageDocumentEntryResolution Resolve(
        PackageHouseSettlement.Acquired settlement,
        string path)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        string normalizedPath =
            PackageDocumentDemand.Create([path]).Entries.Single();
        if (settlement.Payload.Content
            is not IPackageContentEntryManifest manifest)
        {
            return PackageDocumentEntryResolution.Unavailable(
                PackageDocumentEntryResolutionStatus.ManifestUnavailable);
        }

        PackageContentEntry[] matches =
        [
            .. manifest.EnumerateEntriesWithLengths()
                .Where(entry => entry.Path.Equals(
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2),
        ];
        return matches.Length switch
        {
            0 => PackageDocumentEntryResolution.Unavailable(
                PackageDocumentEntryResolutionStatus.Missing),
            1 => PackageDocumentEntryResolution.Resolved(matches[0]),
            _ => PackageDocumentEntryResolution.Unavailable(
                PackageDocumentEntryResolutionStatus.Ambiguous),
        };
    }
}
