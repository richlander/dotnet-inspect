namespace DotnetInspector.Packages;

/// <summary>
/// One admitted package Library: its package-relative asset path, with
/// <c>/</c> separators, and its owner-issued assembly simple name.
/// </summary>
public sealed record PackageLibraryIdentity(
    string AssetPath,
    string AssemblySimpleName);

/// <summary>Why a First Library request chose its Library.</summary>
public enum FirstPackageLibraryReason
{
    Namesake,
    FirstInLibraryOrder,
}

/// <summary>The typed outcome of a First Library request.</summary>
public abstract record FirstPackageLibrarySelection
{
    private FirstPackageLibrarySelection()
    {
    }

    /// <summary>
    /// The chosen Library, why it was chosen, and the admitted Libraries in
    /// Library order.
    /// </summary>
    public sealed record Selected(
        PackageLibraryIdentity Library,
        FirstPackageLibraryReason Reason,
        IReadOnlyList<PackageLibraryIdentity> LibraryOrder)
        : FirstPackageLibrarySelection;

    /// <summary>
    /// At least one candidate identity is unresolved, so it could change which
    /// Library is first.
    /// </summary>
    public sealed record IdentityUnresolved(
        IReadOnlyList<string> AssetPaths)
        : FirstPackageLibrarySelection;

    /// <summary>The admitted population holds no Library.</summary>
    public sealed record Unavailable : FirstPackageLibrarySelection;
}

/// <summary>
/// Library order and the First Library request defined by Inspection Subject
/// Navigation, over issued identities so every host shares one result.
/// </summary>
public static class PackageLibraryOrder
{
    /// <summary>
    /// Compares assembly simple names ordinally ignoring case, then
    /// ordinally, then asset paths ordinally.
    /// </summary>
    public static int Compare(
        PackageLibraryIdentity left,
        PackageLibraryIdentity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int order = string.Compare(
            left.AssemblySimpleName,
            right.AssemblySimpleName,
            StringComparison.OrdinalIgnoreCase);
        if (order == 0)
        {
            order = string.CompareOrdinal(
                left.AssemblySimpleName,
                right.AssemblySimpleName);
        }

        return order != 0
            ? order
            : string.CompareOrdinal(left.AssetPath, right.AssetPath);
    }

    /// <summary>
    /// The first namesake in Library order, otherwise the first Library in
    /// that order. A namesake's assembly simple name equals
    /// <paramref name="packageId"/> ignoring case. Any unresolved identity
    /// fails the request, and an empty population is unavailable; neither
    /// falls back to another Library, the aggregate, or Package.
    /// </summary>
    public static FirstPackageLibrarySelection SelectFirst(
        IReadOnlyList<PackageLibraryIdentity> libraries,
        IReadOnlyList<string> unresolvedAssetPaths,
        string packageId)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(unresolvedAssetPaths);
        ArgumentNullException.ThrowIfNull(packageId);

        if (unresolvedAssetPaths.Count > 0)
        {
            return new FirstPackageLibrarySelection.IdentityUnresolved(
                [.. unresolvedAssetPaths]);
        }

        if (libraries.Count == 0)
            return new FirstPackageLibrarySelection.Unavailable();

        PackageLibraryIdentity[] ordered = [.. libraries];
        Array.Sort(ordered, Compare);
        PackageLibraryIdentity? namesake = Array.Find(
            ordered,
            library => library.AssemblySimpleName.Equals(
                packageId,
                StringComparison.OrdinalIgnoreCase));
        return new FirstPackageLibrarySelection.Selected(
            namesake ?? ordered[0],
            namesake is null
                ? FirstPackageLibraryReason.FirstInLibraryOrder
                : FirstPackageLibraryReason.Namesake,
            ordered);
    }
}
