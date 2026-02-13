namespace DotnetInspector.Views;

/// <summary>
/// Registry of known section names per command. Provides case-insensitive
/// matching and wildcard resolution for <c>-s</c> and <c>-x</c> options.
/// </summary>
public static class SectionRegistry
{
    /// <summary>Sections available in the library command.</summary>
    public static readonly string[] LibrarySections =
    [
        "Library Info",
        "Symbols",
        "Source Coverage",
        "Library References",
        "Library References (Transitive)",
        "Dependencies",
        "Extension Methods",
        "Unsafe Methods",
        "P/Invoke Methods",
        "Resources",
        "Custom Attributes",
        "Type Forwarders",
        "Non-normalized Paths",
        "Missing Sources",
    ];

    /// <summary>Sections available in the package command.</summary>
    public static readonly string[] PackageCommandSections =
    [
        PackageSections.Package,
        PackageSections.Statistics,
        PackageSections.PackageDependencies,
        PackageSections.Files,
        PackageSections.Vulnerabilities,
        PackageSections.RidPackages,
        PackageSections.RuntimeDependencies,
    ];

    /// <summary>Sections available in the api type-list view.</summary>
    public static readonly string[] ApiTypeSections =
    [
        "Classes",
        "Structs",
        "Interfaces",
        "Enums",
        "Delegates",
    ];

    /// <summary>Sections available in the api type-detail view.</summary>
    public static readonly string[] ApiMemberSections =
    [
        "Values",
        "Type Parameters",
        "Interfaces",
        "Baseclass",
        "Remote Source",
        "Constructors",
        "Fields",
        "Properties",
        "Methods",
        "Events",
    ];

    /// <summary>
    /// Prints available section names to stdout.
    /// </summary>
    public static void ListSections(string[] knownSections)
    {
        foreach (var section in knownSections)
            Console.WriteLine(section);
    }

    /// <summary>
    /// Resolves both include and exclude section filters.
    /// Returns the resolved sets, or null values with <paramref name="hasError"/> set if resolution fails.
    /// </summary>
    public static (HashSet<string>? Include, HashSet<string>? Exclude) ResolveFilters(
        string[] knownSections,
        HashSet<string>? includeRequested,
        HashSet<string>? excludeRequested,
        out bool hasError)
    {
        var include = Resolve(knownSections, includeRequested, out var includeError);
        var exclude = Resolve(knownSections, excludeRequested, out var excludeError);
        hasError = includeError || excludeError;
        return (include, exclude);
    }

    /// <summary>
    /// Resolves user-supplied section names against known sections.
    /// Supports case-insensitive matching and trailing <c>*</c> wildcards.
    /// </summary>
    /// <param name="knownSections">The set of valid section names.</param>
    /// <param name="requested">User-supplied section names (from <c>-s</c> or <c>-x</c>). Null means no filter.</param>
    /// <returns>
    /// A <see cref="HashSet{String}"/> with original-cased names for Markout,
    /// or <c>null</c> if <paramref name="requested"/> was null (no filter).
    /// Returns an empty set if the user specified <c>-s</c> with no value (header only).
    /// On unresolvable names, writes an error to stderr and returns <c>null</c> with
    /// <paramref name="hasError"/> set to <c>true</c>.
    /// </returns>
    public static HashSet<string>? Resolve(string[] knownSections, HashSet<string>? requested, out bool hasError)
    {
        hasError = false;
        if (requested == null)
            return null;

        // Empty set means "header only" (-s with no value)
        if (requested.Count == 0)
            return [];

        HashSet<string> resolved = new(StringComparer.Ordinal);
        List<string> unmatched = [];

        foreach (var term in requested)
        {
            bool matched = false;

            if (term.EndsWith('*'))
            {
                // Wildcard: match any section starting with the prefix
                var prefix = term[..^1];
                foreach (var section in knownSections)
                {
                    if (section.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved.Add(section);
                        matched = true;
                    }
                }
            }
            else
            {
                // Exact case-insensitive match
                foreach (var section in knownSections)
                {
                    if (section.Equals(term, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved.Add(section);
                        matched = true;
                        break;
                    }
                }
            }

            if (!matched)
                unmatched.Add(term);
        }

        if (unmatched.Count > 0)
        {
            hasError = true;
            Console.Error.WriteLine($"Error: Unknown section{(unmatched.Count > 1 ? "s" : "")}: {string.Join(", ", unmatched)}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Available sections:");
            foreach (var section in knownSections)
                Console.Error.WriteLine($"  {section}");
            return null;
        }

        return resolved;
    }
}
