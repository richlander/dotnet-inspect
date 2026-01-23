using MarkdownData;

namespace DotnetInspector;

[MdfSerializable(TitleProperty = nameof(PackageName))]
public class InspectionResult
{
    [MdfPropertyName("Package")]
    public string PackageName { get; set; } = "";

    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public string? Repository { get; set; }

    [MdfPropertyName("Tool Package")]
    public bool IsToolPackage { get; set; }

    [MdfPropertyName("Package Types")]
    public List<string>? PackageTypes { get; set; }

    [MdfPropertyName("Target Frameworks")]
    public List<string>? TargetFrameworks { get; set; }

    [MdfPropertyName("Supported RIDs")]
    public List<string>? SupportedRids { get; set; }

    [MdfPropertyName("Framework Dependent")]
    public bool IsFrameworkDependent { get; set; }

    [MdfPropertyName("RID-Specific Assets")]
    public bool HasRidSpecificAssets { get; set; }

    [MdfPropertyName("Native Dependencies")]
    public bool HasNativeDependencies { get; set; }

    // RID-specific tool (DotNetCliTool Version="2") properties
    [MdfPropertyName("Tool Format")]
    public string? ToolFormat { get; set; }

    [MdfPropertyName("RID-Specific Pointer Package")]
    public bool IsRidSpecificPointerPackage { get; set; }

    [MdfPropertyName("Tool Commands")]
    public List<string>? ToolCommands { get; set; }

    [MdfSection(Name = "RID Packages")]
    public List<RidPackageReference>? RuntimeIdentifierPackages { get; set; }

    [MdfPropertyName("Runtime Target RID")]
    public string? RuntimeTargetRid { get; set; }

    [MdfPropertyName("Native Files")]
    public List<string>? NativeFiles { get; set; }

    [MdfSection(Name = "Package Dependencies")]
    public List<DependencyGroup>? DependencyGroups { get; set; }

    [MdfSection(Name = "Runtime Dependencies")]
    public List<PackageDependency>? RuntimeDependencies { get; set; }

    [MdfSection(Name = "Audit Summary")]
    public AuditSummary? AuditSummary { get; set; }

    [MdfSection(Name = "Assembly Audit")]
    public List<AssemblyAudit>? AssemblyAudits { get; set; }
}
