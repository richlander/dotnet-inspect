namespace DotnetInspector.Views;

/// <summary>
/// Section names for package inspection results.
/// Used by <see cref="InspectionResultView"/> attributes, verbosity filtering, and --discover output.
/// </summary>
public static class PackageSections
{
    public const string Summary = "Summary";
    public const string Package = "Package";
    public const string Statistics = "Statistics";
    public const string PackageDependencies = "Package Dependencies";
    public const string Files = "Files";
    public const string Vulnerabilities = "Vulnerabilities";
    public const string RidPackages = "RID Packages";
    public const string RuntimeDependencies = "Runtime Dependencies";
}
