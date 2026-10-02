namespace DotnetInspector.Packages;

/// <summary>
/// The package-entry space observed by every terminal in one content query.
/// Owned by <c>docs/design/package-house-semantic-content-demand.md</c>.
/// </summary>
public abstract class PackageHouseContentNarrowing
{
    private PackageHouseContentNarrowing()
    {
    }

    /// <summary>Every admitted entry in the package is in scope.</summary>
    public sealed class PackageWide : PackageHouseContentNarrowing
    {
        public PackageWide()
        {
        }
    }
}

/// <summary>One result requested from a narrowed package-entry space.</summary>
public abstract class PackageHouseContentTerminal
{
    private PackageHouseContentTerminal()
    {
    }

    /// <summary>
    /// The complete validated content of one or more exact package entries.
    /// </summary>
    public sealed class Files : PackageHouseContentTerminal
    {
        public Files(IEnumerable<string> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in entries)
            {
                if (path is null
                    || !PackageEntryPath.IsSafeRelativePath(path))
                {
                    throw new ArgumentException(
                        "A package file is a relative package path of safe segments, without '..' or a root.",
                        nameof(entries));
                }
                if (seen.Add(path))
                    distinct.Add(path);
            }
            if (distinct.Count == 0)
            {
                throw new ArgumentException(
                    "A Files terminal names at least one package entry.",
                    nameof(entries));
            }

            Entries = distinct.AsReadOnly();
        }

        /// <summary>
        /// The exact entry paths, distinct without regard to case, in the
        /// order first given.
        /// </summary>
        public IReadOnlyList<string> Entries { get; }

        internal PackageHouseFilesResolution Resolve(
            IReadOnlyList<string> entryPaths)
        {
            ArgumentNullException.ThrowIfNull(entryPaths);
            Dictionary<string, List<string>> matches = Entries.ToDictionary(
                static entry => entry,
                static _ => new List<string>(2),
                StringComparer.OrdinalIgnoreCase);
            foreach (string path in entryPaths)
            {
                if (matches.TryGetValue(path, out List<string>? paths)
                    && paths.Count < 2)
                {
                    paths.Add(path);
                }
            }

            var selectedPaths = new HashSet<string>(
                StringComparer.Ordinal);
            var missing = new List<string>();
            var ambiguous = new List<string>();
            foreach (string requested in Entries)
            {
                List<string> paths = matches[requested];
                if (paths.Count == 0)
                    missing.Add(requested);
                else if (paths.Count == 1)
                    selectedPaths.Add(paths[0]);
                else
                    ambiguous.Add(requested);
            }

            return new(
                [.. entryPaths.Where(selectedPaths.Contains)],
                missing.AsReadOnly(),
                ambiguous.AsReadOnly());
        }

        public override string ToString() => string.Join(", ", Entries);
    }

    /// <summary>
    /// The complete validated archive entry inventory without expanding
    /// additional entry content.
    /// </summary>
    public sealed class FileList : PackageHouseContentTerminal
    {
    }
}

internal sealed record PackageHouseFilesResolution(
    IReadOnlyList<string> SelectedEntries,
    IReadOnlyList<string> MissingEntries,
    IReadOnlyList<string> AmbiguousEntries);

/// <summary>
/// The complete validated physical entry inventory returned by a File List
/// terminal.
/// </summary>
public sealed class PackageHouseFileList
{
    internal PackageHouseFileList(
        IReadOnlyList<PackageContentEntry> entries)
    {
        Entries = entries
            ?? throw new ArgumentNullException(nameof(entries));
    }

    /// <summary>Every package entry, in archive-directory order.</summary>
    public IReadOnlyList<PackageContentEntry> Entries { get; }
}

/// <summary>
/// One semantic package-content request: one narrowing and one or more
/// compatible result terminals over that exact narrowed space.
/// </summary>
public sealed class PackageHouseContentQuery
{
    public PackageHouseContentQuery(
        PackageHouseContentNarrowing narrowing,
        IEnumerable<PackageHouseContentTerminal> terminals)
    {
        ArgumentNullException.ThrowIfNull(narrowing);
        ArgumentNullException.ThrowIfNull(terminals);
        PackageHouseContentTerminal[] values = [.. terminals];
        if (values.Length == 0)
        {
            throw new ArgumentException(
                "A content query requires at least one result terminal.",
                nameof(terminals));
        }
        if (values.Any(static terminal => terminal is null))
        {
            throw new ArgumentException(
                "A content query cannot contain a null result terminal.",
                nameof(terminals));
        }
        if (values
            .GroupBy(static terminal => terminal.GetType())
            .Any(static group => group.Skip(1).Any()))
        {
            throw new ArgumentException(
                "A content query cannot repeat a result terminal kind.",
                nameof(terminals));
        }

        Narrowing = narrowing;
        Terminals = Array.AsReadOnly(values);
        FilesTerminal = values
            .OfType<PackageHouseContentTerminal.Files>()
            .SingleOrDefault();
        FileListTerminal = values
            .OfType<PackageHouseContentTerminal.FileList>()
            .SingleOrDefault();
    }

    /// <summary>The package-entry space shared by every terminal.</summary>
    public PackageHouseContentNarrowing Narrowing { get; }

    /// <summary>The requested results, in caller order.</summary>
    public IReadOnlyList<PackageHouseContentTerminal> Terminals { get; }

    internal PackageHouseContentTerminal.Files? FilesTerminal { get; }

    internal PackageHouseContentTerminal.FileList? FileListTerminal { get; }

    /// <summary>Creates a package-wide exact Files query.</summary>
    public static PackageHouseContentQuery PackageFiles(
        IEnumerable<string> entries) =>
        new(
            new PackageHouseContentNarrowing.PackageWide(),
            [new PackageHouseContentTerminal.Files(entries)]);

    /// <summary>
    /// Creates one package-wide query for exact Files content and the complete
    /// physical File List over the same directory snapshot.
    /// </summary>
    public static PackageHouseContentQuery PackageFilesWithFileList(
        IEnumerable<string> entries) =>
        new(
            new PackageHouseContentNarrowing.PackageWide(),
            [
                new PackageHouseContentTerminal.Files(entries),
                new PackageHouseContentTerminal.FileList(),
            ]);
}
