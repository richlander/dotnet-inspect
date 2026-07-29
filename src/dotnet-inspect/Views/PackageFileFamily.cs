namespace DotnetInspector.Views;

using DotnetInspector.Models;

/// <summary>
/// The package file family: <c>Package &lt;X&gt; file(s)</c> listings, each scoped to one
/// kind of document the package ships.
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
        (PackageSections.FilesNuspec, static file => HasExtension(file, ".nuspec")),
        // At most one row: IsReadme is set on the single file that ResolvePackageReadme
        // picked (README.md, then PACKAGE.md, then the declared readme), so the priority
        // chain lives in one place rather than being restated as a predicate here.
        (PackageSections.FilesReadme, static file => file.IsReadme),
        (PackageSections.FilesSkills, IsSkillDocument),
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

    /// <summary>
    /// <c>skills/SKILL.md</c> or <c>skills/**/SKILL.md</c>, matching the globs the project
    /// command's skill discovery uses.
    /// </summary>
    static bool IsSkillDocument(PackageFile file)
        => HasRoot(file, "skills/")
           && (file.Path.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
               || file.Path.Equals("skills/SKILL.md", StringComparison.OrdinalIgnoreCase));
}
