namespace NuGetFetch;

/// <summary>
/// Configures finite work limits for one local-folder package-source operation.
/// </summary>
public sealed record LocalPackageSourceOptions
{
    public const int DefaultMaxDirectoryEntries = 16_384;
    public const int DefaultMaxCandidateArchives = 4_096;
    public const int DefaultMaxArchiveEntries = 50_000;
    public const long DefaultMaxCentralDirectoryBytes = 16 * 1024 * 1024;
    public const long DefaultMaxManifestBytes = 1024 * 1024;
    public const long DefaultMaxAggregateManifestBytes = 64 * 1024 * 1024;
    public const long DefaultMaxPackageBytes = 500_000_000;

    public int MaxDirectoryEntries { get; init; } =
        DefaultMaxDirectoryEntries;

    public int MaxCandidateArchives { get; init; } =
        DefaultMaxCandidateArchives;

    public int MaxArchiveEntries { get; init; } =
        DefaultMaxArchiveEntries;

    public long MaxCentralDirectoryBytes { get; init; } =
        DefaultMaxCentralDirectoryBytes;

    public long MaxManifestBytes { get; init; } =
        DefaultMaxManifestBytes;

    public long MaxAggregateManifestBytes { get; init; } =
        DefaultMaxAggregateManifestBytes;

    public long MaxPackageBytes { get; init; } =
        DefaultMaxPackageBytes;

    internal static LocalPackageSourceOptions Validate(
        LocalPackageSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxDirectoryEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxCandidateArchives);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxArchiveEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxCentralDirectoryBytes);
        if (options.MaxCentralDirectoryBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxCentralDirectoryBytes),
                options.MaxCentralDirectoryBytes,
                "The central-directory limit cannot exceed the maximum array length.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxManifestBytes);
        if (options.MaxManifestBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxManifestBytes),
                options.MaxManifestBytes,
                "The manifest limit cannot exceed the maximum array length.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxAggregateManifestBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxPackageBytes);
        return options;
    }
}
