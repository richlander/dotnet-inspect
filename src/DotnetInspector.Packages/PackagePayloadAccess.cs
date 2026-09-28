namespace DotnetInspector.Packages;

/// <summary>
/// How a payload that no authorized cache holds is transferred. The cache-first
/// and local-before-HTTP rules of the package source model apply to both.
/// </summary>
public enum PackagePayloadAccess
{
    /// <summary>
    /// Acquire the whole archive and commit it to the authority's store.
    /// </summary>
    Complete,

    /// <summary>
    /// Read the archive's directory by range and materialize only the entries
    /// the operation's realization selects; nothing is committed. An authority
    /// whose client cannot range, or whose source refuses the ranged read,
    /// falls back to <see cref="Complete"/> for that authority. Requires a
    /// Realize operation, which supplies the selection, or an Acquire
    /// operation carrying a <see cref="PackageDocumentDemand"/>, which names
    /// the entries it reads.
    /// </summary>
    Ranged,
}

/// <summary>
/// Chooses, from a directory-only view of package content, the entry paths a
/// ranged acquisition materializes. It runs once per opened archive with
/// content whose <see cref="IPackageContent.EnumerateEntries"/> is complete
/// and whose entry bodies are not yet readable; every returned path must be an
/// entry of that directory.
/// </summary>
public delegate PackageRangedSelection PackageEntrySelector(
    IPackageContent directory);

/// <summary>
/// The entries a ranged acquisition reads: exact entries, read as named, and
/// block anchors, each read with every entry of the aligned block that holds
/// it (docs/design/package-read-demand.md#named-implementation-and-aligned-blocks).
/// The acquisition step plans the blocks, because only it holds the
/// archive's offsets.
/// </summary>
public sealed class PackageRangedSelection
{
    public PackageRangedSelection(
        IReadOnlyList<string> entries,
        IReadOnlyList<string>? blockAnchors = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries;
        BlockAnchors = blockAnchors ?? [];
    }

    /// <summary>Entries read exactly as named.</summary>
    public IReadOnlyList<string> Entries { get; }

    /// <summary>Entries read together with their aligned block.</summary>
    public IReadOnlyList<string> BlockAnchors { get; }
}

/// <summary>
/// What a ranged acquisition reads and when it reads by range at all: the
/// entry selector, and the size cut at or under which the archive is
/// acquired complete and cached instead (docs/design/package-cache-policy.md).
/// </summary>
public sealed class PackageRangedRead
{
    /// <summary>The size cut hosts use unless they set another: 1 MB of archive.</summary>
    public const long DefaultSizeCut = 1_000_000;

    public PackageRangedRead(
        PackageEntrySelector selectEntries,
        long sizeCut = DefaultSizeCut)
    {
        ArgumentNullException.ThrowIfNull(selectEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeCut);
        SelectEntries = selectEntries;
        SizeCut = sizeCut;
    }

    /// <summary>Chooses the entries to read from a directory-only view.</summary>
    public PackageEntrySelector SelectEntries { get; }

    /// <summary>
    /// Archives whose advertised length is at or under this are acquired
    /// complete; larger ones are read by range. It is also the budget of an
    /// aligned block.
    /// </summary>
    public long SizeCut { get; }
}
