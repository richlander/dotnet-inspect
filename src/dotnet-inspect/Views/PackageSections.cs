namespace DotnetInspector.Views;

/// <summary>
/// Section names for package inspection results.
/// Used by <see cref="InspectionResultView"/> attributes, verbosity filtering, and --discover output.
/// </summary>
public static class PackageSections
{
    public const string Summary = "Summary";
    public const string PackageInfo = "Package Info";
    public const string PackageReadme = "Grounding";
    public const string Signals = "Signals";
    public const string Statistics = "Statistics";
    public const string TargetFrameworks = "Target Frameworks";
    // The "Files:" family. A prefix advertises category membership, so every
    // section named this way is a member of the @Files door, and the door owns
    // the family exclusively. Plain Files (below) is the whole-package listing
    // rather than a subset, so it stays outside the door to avoid rendering
    // every row twice.
    public const string FilesLibrary = "Files: Library";
    public const string FilesReference = "Files: Reference";
    public const string FilesRuntime = "Files: Runtime";
    public const string FilesMarkdown = "Files: Markdown";
    public const string FilesNuspec = "Files: Nuspec";

    public const string SourceFiles = "Source Files";
    public const string Dependencies = "Dependencies";
    public const string Files = "Files";
    public const string Vulnerabilities = "Vulnerabilities";
    public const string Manifest = "Manifest";
    public const string RuntimeDependencies = "Runtime Dependencies";
    public const string Signature = "Signature";
}
