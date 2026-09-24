namespace DotnetInspector.Packages;

/// <summary>
/// Which package assets a consumer reads. Owned by
/// <c>docs/design/package-read-demand.md</c>.
/// </summary>
public enum PackageAssetDemand
{
    /// <summary>
    /// The compile surface and its implementation universe: depth commands
    /// such as <c>type</c>, <c>member</c>, <c>library</c>, and <c>graph</c>.
    /// </summary>
    SurfaceAndImplementation,

    /// <summary>
    /// The compile surface only (<c>ref/</c> when the package has it, else
    /// <c>lib/</c>): broad public-surface commands such as <c>find</c>.
    /// </summary>
    Surface,
}

/// <summary>
/// The implementation assemblies a consumer names, by file name, compared
/// without regard to case. Owned by
/// <c>docs/design/package-read-demand.md#named-implementation-and-aligned-blocks</c>.
/// </summary>
public sealed class PackageImplementationNames
{
    private readonly HashSet<string> _names;

    private PackageImplementationNames(IReadOnlyList<string> names)
    {
        Names = names;
        _names = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The distinct names, in the order first given.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Validates at least one name, each a bare file name: not empty and
    /// without a path separator.
    /// </summary>
    public static PackageImplementationNames Create(IEnumerable<string> names) =>
        Create(names, nameof(names));

    internal static PackageImplementationNames Create(
        IEnumerable<string> names,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(names, parameterName);
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name)
                || name.Contains('/')
                || name.Contains('\\'))
            {
                throw new ArgumentException(
                    "An implementation assembly is named by its file name, without a path.",
                    parameterName);
            }
            if (seen.Add(name))
                distinct.Add(name);
        }
        if (distinct.Count == 0)
        {
            throw new ArgumentException(
                "Named implementation demand requires at least one name.",
                parameterName);
        }
        return new PackageImplementationNames(distinct.AsReadOnly());
    }

    /// <summary>Whether the entry path's file name is one of the names.</summary>
    public bool MatchesPath(string entryPath)
    {
        ArgumentNullException.ThrowIfNull(entryPath);
        int slash = entryPath.LastIndexOf('/');
        return _names.Contains(slash < 0 ? entryPath : entryPath[(slash + 1)..]);
    }

    /// <summary>
    /// The names that match none of <paramref name="entryPaths"/>, in order;
    /// empty when every name selects an asset.
    /// </summary>
    public IReadOnlyList<string> Unmatched(IEnumerable<string> entryPaths)
    {
        ArgumentNullException.ThrowIfNull(entryPaths);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in entryPaths)
        {
            int slash = path.LastIndexOf('/');
            string fileName = slash < 0 ? path : path[(slash + 1)..];
            if (_names.Contains(fileName))
                matched.Add(fileName);
        }
        return [.. Names.Where(name => !matched.Contains(name))];
    }

    /// <summary>Whether both name the same set of file names.</summary>
    public bool SetEquals(PackageImplementationNames other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _names.SetEquals(other._names);
    }

    public override string ToString() => string.Join(", ", Names);
}
