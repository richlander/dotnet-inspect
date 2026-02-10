namespace DotnetInspector.Metadata;

/// <summary>
/// Represents a reference to another assembly.
/// </summary>
public record AssemblyReference(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

/// <summary>
/// Represents a node in the transitive assembly reference tree.
/// Uses Depth to indicate tree level instead of nested References to avoid source generator issues.
/// </summary>
public class AssemblyReferenceNode
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? PublicKeyToken { get; set; }

    /// <summary>
    /// Tree depth (0 = direct reference, 1 = reference of reference, etc.)
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// How the assembly was resolved: "local", "platform", or null if unresolved.
    /// </summary>
    public string? ResolvedFrom { get; set; }

    /// <summary>
    /// Resolved file path, or null if not found.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// True if this node was already seen earlier in the tree (circular reference).
    /// </summary>
    public bool IsCyclic { get; set; }

    /// <summary>
    /// Company name from the assembly's metadata.
    /// </summary>
    public string? Company { get; set; }
}

public class AssemblyInfo
{
    public string? AssemblyName { get; set; }
    public string? AssemblyVersion { get; set; }
    public string? FileVersion { get; set; }
    public string? InformationalVersion { get; set; }
    public string? Product { get; set; }
    public string? Company { get; set; }
    public string? Copyright { get; set; }
    public string? Description { get; set; }
    public string? TargetFramework { get; set; }
    public string? Culture { get; set; }

    // PE header information
    public string? Architecture { get; set; }  // AnyCPU, x86, x64, ARM, ARM64
    public bool IsAnyCpu { get; set; }
    public bool Prefers32Bit { get; set; }
    public bool IsExecutable { get; set; }
    public bool IsDll { get; set; }

    // Assembly characteristics
    public bool IsSigned { get; set; }
    public string? PublicKeyToken { get; set; }
    public bool HasUnsafeCode { get; set; }

    // Metadata
    public string? RuntimeVersion { get; set; }
    public int MetadataVersion { get; set; }

    // Compilation type detection
    public string? CompilationType { get; set; }  // "CoreCLR", "NativeAOT", "Native", "ReadyToRun"
    public bool IsNativeAot { get; set; }
    public bool IsReadyToRun { get; set; }
    public bool HasManagedMetadata { get; set; }
    public bool HasCorHeader { get; set; }
    public bool HasILCode { get; set; }

    /// <summary>
    /// List of assemblies referenced by this assembly.
    /// </summary>
    public List<AssemblyReference>? References { get; set; }

    /// <summary>
    /// Transitive reference tree (when --dependencies is used).
    /// </summary>
    public List<AssemblyReferenceNode>? TransitiveReferences { get; set; }
}
