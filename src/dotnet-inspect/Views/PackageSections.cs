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
    public const string LibraryFiles = "Library Files";
    public const string MarkdownFiles = "Markdown Files";
    public const string SourceFiles = "Source Files";
    public const string Dependencies = "Dependencies";
    public const string Files = "Files";
    public const string Vulnerabilities = "Vulnerabilities";
    public const string Manifest = "Manifest";
    public const string RuntimeDependencies = "Runtime Dependencies";
    public const string Signature = "Signature";
}
