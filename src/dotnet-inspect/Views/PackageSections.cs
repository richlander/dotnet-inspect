namespace DotnetInspector.Views;

/// <summary>
/// Section names for package inspection results.
/// Used by <see cref="InspectionResultView"/> attributes, verbosity filtering, and --discover output.
/// </summary>
public static class PackageSections
{
    public const string Summary = "Summary";
    public const string PackageInfo = "Package Info";
    public const string Signals = "Signals";
    public const string Statistics = "Statistics";
    public const string TargetFrameworks = "Target Frameworks";
    // The package file family. These read as natural noun phrases rather than
    // carrying a "Group: Leaf" prefix, so membership is advertised by the
    // trailing "file"/"files" noun plus the @Files door rather than by a prefix.
    // Singular names render exactly one row; plural names are listings.
    // Plain PackageFiles is the whole-package listing rather than a subset, so
    // it stays outside the door to avoid rendering every row twice.
    public const string FilesNuspec = "Package nuspec file";

    /// <summary>
    /// The best package README: <c>README.md</c>, then <c>PACKAGE.md</c>. At most one row.
    /// </summary>
    public const string FilesReadme = "Package README file";

    /// <summary>
    /// <c>skills/**/SKILL.md</c> documents shipped by the package.
    /// </summary>
    public const string FilesSkills = "Package skill files";

    /// <summary>
    /// SourceLink-derived source file rows. Same collector and same data as the library
    /// command's <c>SourceLink: Files</c>, so it carries the same name and joins the same
    /// <c>@SourceLink</c> door rather than reading as a sibling of the package file
    /// family, which lists files the package actually ships.
    /// </summary>
    public const string SourceLinkFiles = "SourceLink: Files";
    public const string Dependencies = "Dependencies";
    public const string Files = "Package files";
    public const string Vulnerabilities = "Vulnerabilities";
    public const string Manifest = "Manifest";
    public const string RuntimeDependencies = "Runtime Dependencies";
    public const string Signature = "Signature";
}
