namespace DotnetInspector.Views;

using DotnetInspector.Models;

/// <summary>
/// The <c>Files: &lt;X&gt;</c> family: package file listings scoped to one layout root.
///
/// This is the single declaration of which sections are in the family and what each
/// one matches. The section descriptors, the view's row projections, the command's
/// row lookup, and the <c>@Files</c> category membership all read it, so a new member
/// is added in one place and cannot be half-wired.
/// </summary>
public static class PackageFileFamily
{
    /// <summary>
    /// Family members in catalog order. <c>@Files</c> membership is projected from this
    /// list, so the door and the family cannot drift apart.
    /// </summary>
    public static readonly (string Section, Func<PackageFile, bool> Matches)[] Members =
    [
        (PackageSections.FilesLibrary, static file => HasRoot(file, "lib/")),
        (PackageSections.FilesReference, static file => HasRoot(file, "ref/")),
        (PackageSections.FilesRuntime, static file => HasRoot(file, "runtimes/")),
        (PackageSections.FilesMarkdown, static file => HasExtension(file, ".md")),
        (PackageSections.FilesNuspec, static file => HasExtension(file, ".nuspec")),
    ];

    /// <summary>Section names in the family, in catalog order.</summary>
    public static string[] SectionNames => [.. Members.Select(static member => member.Section)];

    /// <summary>
    /// The predicate for a family member, or null when the section is not in the family.
    /// </summary>
    public static Func<PackageFile, bool>? PredicateFor(string? section)
    {
        if (section is null)
            return null;

        foreach (var (name, matches) in Members)
        {
            if (name.Equals(section, StringComparison.OrdinalIgnoreCase))
                return matches;
        }

        return null;
    }

    public static bool IsFamilySection(string? section) => PredicateFor(section) is not null;

    static bool HasRoot(PackageFile file, string root)
        => file.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase);

    static bool HasExtension(PackageFile file, string extension)
        => file.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}
