namespace DotnetInspector;

public class InspectionResult
{
    public string PackageName { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public string? Repository { get; set; }
    public bool IsToolPackage { get; set; }
    public List<string>? PackageTypes { get; set; }
    public List<string>? TargetFrameworks { get; set; }
    public List<string>? SupportedRids { get; set; }
    public bool IsFrameworkDependent { get; set; }
    public bool HasRidSpecificAssets { get; set; }
    public bool HasNativeDependencies { get; set; }
    public string? RuntimeTargetRid { get; set; }
    public List<string>? NativeFiles { get; set; }
    public List<DependencyGroup>? DependencyGroups { get; set; }
    public List<PackageDependency>? RuntimeDependencies { get; set; }
    public AuditSummary? AuditSummary { get; set; }
    public List<AssemblyAudit>? AssemblyAudits { get; set; }
}
