using ZipFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One entry's place in the archive for block planning: its local-header
/// offset and its length up to the next local header of its folder, or, for
/// the folder's last entry, up to the next local header in the archive.
/// </summary>
internal readonly record struct PackageEntryExtent(
    string Name,
    long Offset,
    long Length)
{
    public long End => Offset + Length;
}

/// <summary>
/// A ranged read's plan: the exact entries, then each aligned block that
/// holds a block anchor, with every entry of any folder that lies inside
/// the block's request, in archive order, and every entry the read requires.
/// </summary>
internal sealed record PackageRangedPlan(
    IReadOnlyList<string> Entries,
    IReadOnlyList<IReadOnlyList<string>> Blocks)
{
    /// <summary>Every entry the plan requires, exact entries first, each once.</summary>
    public IReadOnlyList<string> Required
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var required = new List<string>();
            foreach (string entry in Entries.Concat(Blocks.SelectMany(static block => block)))
            {
                if (seen.Add(entry))
                    required.Add(entry);
            }
            return required;
        }
    }
}

/// <summary>
/// Plans aligned blocks (docs/design/package-read-demand.md#named-implementation-and-aligned-blocks):
/// a folder's entries in local-header order, walked from its first entry,
/// with a block closing when the next whole entry would take it past the
/// budget. No entry is split, so an entry above the budget is a block alone.
/// Blocks come from the archive alone, so one archive always yields the same
/// blocks.
/// </summary>
internal static class PackageEntryBlocks
{
    /// <summary>
    /// Tiles one folder's entries into blocks, in archive order. The input
    /// order does not matter.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PackageEntryExtent>> Plan(
        IEnumerable<PackageEntryExtent> folderEntries,
        long budget)
    {
        ArgumentNullException.ThrowIfNull(folderEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        PackageEntryExtent[] ordered =
        [
            .. folderEntries
                .OrderBy(static entry => entry.Offset)
                .ThenBy(static entry => entry.Name, StringComparer.Ordinal),
        ];
        var blocks = new List<IReadOnlyList<PackageEntryExtent>>();
        var current = new List<PackageEntryExtent>();
        long size = 0;
        foreach (PackageEntryExtent entry in ordered)
        {
            if (current.Count > 0 && size + entry.Length > budget)
            {
                blocks.Add(current.AsReadOnly());
                current = [];
                size = 0;
            }
            current.Add(entry);
            size += entry.Length;
        }
        if (current.Count > 0)
            blocks.Add(current.AsReadOnly());
        return blocks.AsReadOnly();
    }

    /// <summary>
    /// The direct entries of <paramref name="folder"/> (a prefix ending in
    /// <c>/</c>, or empty for the root), each measured to the next local
    /// header of the folder; the last is measured to the next local header in
    /// the archive, or to the central directory.
    /// </summary>
    public static IReadOnlyList<PackageEntryExtent> FolderExtents(
        ZipDirectory directory,
        string folder)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(folder);
        long[] headers =
        [
            .. directory.Entries
                .Select(static entry => (long)entry.LocalHeaderOffset)
                .Distinct()
                .Order(),
        ];
        ZipEntry[] members =
        [
            .. directory.Entries
                .Where(entry => IsFileIn(entry.Name, folder))
                .OrderBy(static entry => entry.LocalHeaderOffset)
                .ThenBy(static entry => entry.Name, StringComparer.Ordinal),
        ];
        var extents = new PackageEntryExtent[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            long offset = members[i].LocalHeaderOffset;
            long end = i + 1 < members.Length
                ? members[i + 1].LocalHeaderOffset
                : NextHeader(headers, offset, directory.DirectoryOffset);
            extents[i] = new PackageEntryExtent(
                members[i].Name,
                offset,
                Math.Max(0, end - offset));
        }
        return extents;
    }

    /// <summary>
    /// Plans a selection over an archive directory: its exact entries, and
    /// the block of each anchor's folder that holds the anchor, each block
    /// once.
    /// </summary>
    public static PackageRangedPlan PlanSelection(
        ZipDirectory directory,
        PackageRangedSelection selection,
        long budget)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(selection);
        var blocks = new List<IReadOnlyList<string>>();
        var planned = new Dictionary<string, IReadOnlyList<IReadOnlyList<PackageEntryExtent>>>(
            StringComparer.Ordinal);
        var taken = new HashSet<long>();
        foreach (string anchor in selection.BlockAnchors)
        {
            string folder = FolderOf(anchor);
            if (!planned.TryGetValue(folder, out IReadOnlyList<IReadOnlyList<PackageEntryExtent>>? folderBlocks))
            {
                folderBlocks = Plan(FolderExtents(directory, folder), budget);
                planned[folder] = folderBlocks;
            }
            IReadOnlyList<PackageEntryExtent> block =
                folderBlocks.FirstOrDefault(candidate =>
                    candidate.Any(entry => entry.Name.Equals(anchor, StringComparison.Ordinal)))
                ?? throw new InvalidOperationException(
                    $"The ranged block anchor '{anchor}' is not in the archive directory.");
            if (taken.Add(block[0].Offset))
                blocks.Add(Covered(directory, block[0].Offset, block[^1].Offset));
        }
        return new PackageRangedPlan(selection.Entries, blocks.AsReadOnly());
    }

    /// <summary>
    /// Every file entry whose local header lies from the block's first entry
    /// through its last, whatever its folder, in archive order. The block's
    /// request runs from the first entry's header to the end of the last
    /// entry, so an entry of another folder interleaved between two of the
    /// block's entries lies wholly inside it, up to the next local header,
    /// and is read and kept rather than transferred and dropped.
    /// </summary>
    private static IReadOnlyList<string> Covered(
        ZipDirectory directory,
        long first,
        long last) =>
    [
        .. directory.Entries
            .Where(entry =>
                entry.LocalHeaderOffset >= first
                && entry.LocalHeaderOffset <= last
                && entry.Name.Length > 0
                && !entry.Name.EndsWith('/'))
            .OrderBy(static entry => entry.LocalHeaderOffset)
            .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
            .Select(static entry => entry.Name),
    ];

    internal static string FolderOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..(slash + 1)];
    }

    private static bool IsFileIn(string name, string folder) =>
        name.Length > folder.Length
        && !name.EndsWith('/')
        && name.StartsWith(folder, StringComparison.Ordinal)
        && name.IndexOf('/', folder.Length) < 0;

    private static long NextHeader(long[] headers, long offset, long directoryOffset)
    {
        int index = Array.BinarySearch(headers, offset);
        index = index < 0 ? ~index : index + 1;
        return index < headers.Length ? headers[index] : directoryOffset;
    }
}
