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
    /// Realize operation, which supplies the selection.
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
public delegate IReadOnlyList<string> PackageEntrySelector(
    IPackageContent directory);
