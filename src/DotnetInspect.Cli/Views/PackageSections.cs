namespace DotnetInspect.Cli.Views;

/// <summary>
/// Section names for package inspection results.
/// Used by <see cref="InspectionResultView"/> attributes, verbosity filtering, and --discover output.
/// </summary>
public static class PackageSections
{
    public const string Summary = "Summary";
    public const string PackageInfo = "Package Info";
    public const string Signals = "Signals";
    public const string AuditArtifactText = "Audit: Artifact Text";
    public const string AuditFindings = "Audit: Findings";
    public const string AuditIdentifierConfusion = "Audit: Identifier Confusion";
    public const string Statistics = "Statistics";
    public const string TargetFrameworks = "Target Frameworks";
    // The package file family. Names are concise nouns that do not repeat the
    // coordinate the route already established (docs/design/section-shapes.md,
    // adoption item 1; relationship-section-naming.md#canonical-grammar), and
    // the @Files door advertises membership. Nuspec and README are single Text
    // payloads; Licenses and Skills are Tables of file rows. The whole-package
    // Files listing is a superset rather than a family member, so it stays
    // outside the door to avoid rendering every row twice.

    /// <summary>The root package manifest: exactly one Text payload.</summary>
    public const string FilesNuspec = "Nuspec";

    /// <summary>
    /// The best package README: <c>README.md</c>, then <c>PACKAGE.md</c>. At most one row.
    /// </summary>
    public const string FilesReadme = "README";

    /// <summary>
    /// Nuspec-declared and corpus-backed license documents shipped by the package.
    /// </summary>
    public const string FilesLicenses = "Licenses";

    /// <summary>
    /// <c>skills/**/SKILL.md</c> documents shipped by the package.
    /// </summary>
    public const string FilesSkills = "Skills";

    /// <summary>
    /// SourceLink-derived source file rows. Same collector and same data as the library
    /// command's <c>SourceLink: Files</c>, so it carries the same name and joins the same
    /// <c>@SourceLink</c> door rather than reading as a sibling of the package file
    /// family, which lists files the package actually ships.
    /// </summary>
    public const string SourceLinkFiles = "SourceLink: Files";
    public const string SourceLinkAvailability = "SourceLink: Availability";
    public const string SourceLinkMissingFiles = "SourceLink: Missing Files";
    public const string SourceLinkIntegrity = "SourceLink: Integrity";
    public const string DependencyHierarchy = "Dependency Hierarchy";
    public const string Dependencies = "Dependencies";
    public const string EcosystemDependencies = "Ecosystem Dependencies";
    public const string Files = "Files";
    public const string Vulnerabilities = "Vulnerabilities";
    public const string Manifest = "Manifest";
    public const string RuntimeDependencies = "Runtime Dependencies";
    public const string Signature = "Signature";
}
