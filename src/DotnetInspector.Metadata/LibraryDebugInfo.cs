namespace DotnetInspector.Metadata;

/// <summary>
/// Audit-relevant information extracted from a PE assembly's debug directory and PDB.
/// This is a pure data type without rendering concerns.
/// </summary>
public class LibraryDebugInfo
{
    public bool HasReproducibleFlag { get; set; }
    public bool HasEmbeddedPdb { get; set; }
    public bool? HasNormalizedPaths { get; set; }
    public string? PdbPath { get; set; }
    public string? PdbFormat { get; set; }
    public bool HasSourceLink { get; set; }
    public string? SourceLinkJson { get; set; }
    public string? RepositoryUrl { get; set; }
    public List<string>? NonNormalizedPaths { get; set; }
    public bool IsDeterministic { get; set; }
    public bool IsNativeAot { get; set; }
    public AssemblyInfo? AssemblyInfo { get; set; }
}
