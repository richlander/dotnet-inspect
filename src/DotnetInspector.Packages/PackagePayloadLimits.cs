namespace DotnetInspector.Packages;

/// <summary>
/// The bounds a package payload must respect before it may be published into a
/// package store.
/// </summary>
/// <remarks>
/// <para>
/// A payload arrives from a remote feed, so every one of these bounds is
/// checked against the bytes actually received rather than against what the
/// response advertised. A feed that sends no <c>Content-Length</c>, sends a
/// truthful length and then more bytes, or sends a small archive whose central
/// directory declares an enormous expansion is bounded by the same limits.
/// </para>
/// <para>
/// The defaults are deliberately generous — they exist to bound an attack, not
/// to express a policy about package size — and every one of them is
/// overridable so a test can prove the bound at kilobyte scale instead of
/// allocating hundreds of megabytes.
/// </para>
/// </remarks>
public sealed record PackagePayloadLimits
{
    /// <summary>The bounds used when a caller states none.</summary>
    public static PackagePayloadLimits Default { get; } = new();

    /// <summary>
    /// The largest archive, in bytes as received, that may be published. It
    /// matches the shared transport cap so the two cannot disagree about what
    /// a downloadable package is.
    /// </summary>
    public long MaxArchiveBytes { get; init; } = 500_000_000;

    /// <summary>
    /// The largest total uncompressed size, summed over the archive's entries,
    /// that may be published. This is what bounds a compression bomb: the ratio
    /// itself is not limited, because a legitimate package of mostly-textual
    /// content compresses well.
    /// </summary>
    public long MaxExpandedBytes { get; init; } = 2_000_000_000;

    /// <summary>
    /// The largest number of entries an archive may declare. An archive of many
    /// tiny entries costs directory work rather than bytes, so it is bounded
    /// separately from <see cref="MaxExpandedBytes"/>.
    /// </summary>
    public int MaxEntryCount { get; init; } = 50_000;

    /// <summary>
    /// The largest number of unique intermediate directories entry paths may
    /// imply. Together with <see cref="PackageArchiveValidator.MaxEntryPathDepth"/>
    /// this keeps the validator's ancestor set from growing into multi-GB
    /// allocations when every entry uses a distinct deep prefix.
    /// </summary>
    public int MaxUniqueDirectories { get; init; } = 100_000;
}
