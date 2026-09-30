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

        internal IReadOnlyList<string> Select(
            IReadOnlyCollection<string> entryPaths)
        {
            ArgumentNullException.ThrowIfNull(entryPaths);
            var named = new HashSet<string>(
                Entries,
                StringComparer.OrdinalIgnoreCase);
            return
            [
                .. entryPaths.Where(named.Contains),
            ];
        }

        internal IReadOnlyList<string> Unmatched(
            IEnumerable<string> entryPaths)
        {
            ArgumentNullException.ThrowIfNull(entryPaths);
            var listed = new HashSet<string>(
                entryPaths,
                StringComparer.OrdinalIgnoreCase);
            return
            [
                .. Entries.Where(entry => !listed.Contains(entry)),
            ];
        }

        public override string ToString() => string.Join(", ", Entries);
    }
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
    }

    /// <summary>The package-entry space shared by every terminal.</summary>
    public PackageHouseContentNarrowing Narrowing { get; }

    /// <summary>The requested results, in caller order.</summary>
    public IReadOnlyList<PackageHouseContentTerminal> Terminals { get; }

    internal PackageHouseContentTerminal.Files? FilesTerminal { get; }

    /// <summary>Creates a package-wide exact Files query.</summary>
    public static PackageHouseContentQuery PackageFiles(
        IEnumerable<string> entries) =>
        new(
            new PackageHouseContentNarrowing.PackageWide(),
            [new PackageHouseContentTerminal.Files(entries)]);
}
