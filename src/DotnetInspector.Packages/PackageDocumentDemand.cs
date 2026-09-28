namespace DotnetInspector.Packages;

/// <summary>
/// The package documents a consumer reads, named directly rather than through
/// an asset realization: exact entry paths, such as the root
/// <c>README.md</c>, and folder prefixes, such as a skill folder
/// <c>skills/&lt;name&gt;/</c>. Paths use <c>/</c> and are compared without
/// regard to case. Owned by
/// <c>docs/design/package-read-demand.md#document-demand</c>.
/// </summary>
/// <remarks>
/// A ranged read of a document demand fetches the package's root folder, the
/// folder of each named entry (its direct entries, as the folder unit reads
/// an asset folder), and each named folder with every entry beneath it,
/// subfolders included, because a skill's references and assets live in
/// subfolders of the skill folder.
/// </remarks>
public sealed class PackageDocumentDemand
{
    private PackageDocumentDemand(
        IReadOnlyList<string> entries,
        IReadOnlyList<string> folders)
    {
        Entries = entries;
        Folders = folders;
    }

    /// <summary>The exact entry paths, distinct, in the order first given.</summary>
    public IReadOnlyList<string> Entries { get; }

    /// <summary>
    /// The folder prefixes, each ending with <c>/</c>, distinct, in the order
    /// first given.
    /// </summary>
    public IReadOnlyList<string> Folders { get; }

    /// <summary>
    /// Validates at least one entry or folder. Each is a relative package
    /// path whose segments are safe entry segments: not rooted, no empty,
    /// <c>.</c>, or <c>..</c> segment, and no <c>\</c>, <c>:</c>, or NUL.
    /// A folder may end with <c>/</c>; an entry may not.
    /// </summary>
    public static PackageDocumentDemand Create(
        IEnumerable<string> entries,
        IEnumerable<string>? folders = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<string> entryPaths = Distinct(
            entries,
            static path => ValidateEntry(path, nameof(entries)));
        List<string> folderPaths = Distinct(
            folders ?? [],
            static path => ValidateFolder(path, nameof(folders)));
        if (entryPaths.Count == 0 && folderPaths.Count == 0)
        {
            throw new ArgumentException(
                "A document demand names at least one entry or folder.",
                nameof(entries));
        }

        return new PackageDocumentDemand(
            entryPaths.AsReadOnly(),
            folderPaths.AsReadOnly());
    }

    /// <summary>
    /// The named entries and folders that <paramref name="entryPaths"/> does
    /// not list, in order: an entry that no path equals, and a folder that no
    /// path lies beneath. Empty when every name is listed.
    /// </summary>
    public IReadOnlyList<string> Unmatched(IEnumerable<string> entryPaths)
    {
        ArgumentNullException.ThrowIfNull(entryPaths);
        var listed = new HashSet<string>(entryPaths, StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();
        foreach (string entry in Entries)
        {
            if (!listed.Contains(entry))
                unmatched.Add(entry);
        }
        foreach (string folder in Folders)
        {
            if (!listed.Any(path => IsBeneath(path, folder)))
                unmatched.Add(folder);
        }
        return unmatched;
    }

    /// <summary>
    /// The entries of <paramref name="entryPaths"/> a ranged read of this
    /// demand fetches, in the given order: every root entry, every direct
    /// entry of a named entry's folder, and every entry beneath a named
    /// folder.
    /// </summary>
    public IReadOnlyList<string> Select(IReadOnlyCollection<string> entryPaths)
    {
        ArgumentNullException.ThrowIfNull(entryPaths);
        var named = new HashSet<string>(Entries, StringComparer.OrdinalIgnoreCase);
        var entryFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };
        foreach (string path in entryPaths)
        {
            if (named.Contains(path))
                entryFolders.Add(FolderOf(path));
        }

        var selected = new List<string>();
        foreach (string path in entryPaths)
        {
            if (entryFolders.Contains(FolderOf(path))
                || Folders.Any(folder => IsBeneath(path, folder)))
            {
                selected.Add(path);
            }
        }
        return selected;
    }

    public override string ToString() =>
        string.Join(", ", Entries.Concat(Folders));

    private static string FolderOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..(slash + 1)];
    }

    private static bool IsBeneath(string path, string folder) =>
        path.Length > folder.Length
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);

    private static List<string> Distinct(
        IEnumerable<string> paths,
        Func<string, string> validate)
    {
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            string normalized = validate(path);
            if (seen.Add(normalized))
                distinct.Add(normalized);
        }
        return distinct;
    }

    private static string ValidateEntry(string path, string parameterName)
    {
        if (path is null || !HasSafeSegments(path))
        {
            throw new ArgumentException(
                "A document entry is a relative package path of safe segments, without '..' or a root.",
                parameterName);
        }
        return path;
    }

    private static string ValidateFolder(string path, string parameterName)
    {
        string? folder = path is not null && path.EndsWith('/') ? path[..^1] : path;
        if (folder is null || !HasSafeSegments(folder))
        {
            throw new ArgumentException(
                "A document folder is a relative package path of safe segments, without '..' or a root.",
                parameterName);
        }
        return folder + "/";
    }

    private static bool HasSafeSegments(string path) =>
        path.Length != 0
        && path.Split('/').All(PackageEntryPath.IsSafeSegment);
}
