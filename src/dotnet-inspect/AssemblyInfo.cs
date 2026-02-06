using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector;

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Depth { get; set; }
    
    /// <summary>
    /// How the assembly was resolved: "local", "platform", or null if unresolved.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResolvedFrom { get; set; }
    
    /// <summary>
    /// Resolved file path, or null if not found.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }
    
    /// <summary>
    /// True if this node was already seen earlier in the tree (circular reference).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCyclic { get; set; }
}

[MarkoutSerializable]
public class AssemblyInfo
{
    [MarkoutPropertyName("Name")]
    public string? AssemblyName { get; set; }

    [MarkoutPropertyName("Assembly Version")]
    public string? AssemblyVersion { get; set; }

    [MarkoutPropertyName("File Version")]
    public string? FileVersion { get; set; }

    [MarkoutPropertyName("Informational Version")]
    public string? InformationalVersion { get; set; }

    // Assembly identity attributes
    [MarkoutPropertyName("Product")]
    public string? Product { get; set; }

    [MarkoutPropertyName("Company")]
    public string? Company { get; set; }

    [MarkoutPropertyName("Copyright")]
    public string? Copyright { get; set; }

    [MarkoutPropertyName("Description")]
    public string? Description { get; set; }

    [MarkoutPropertyName("Target Framework")]
    public string? TargetFramework { get; set; }

    public string? Culture { get; set; }

    // PE header information
    public string? Architecture { get; set; }  // AnyCPU, x86, x64, ARM, ARM64

    [MarkoutPropertyName("Any CPU")]
    public bool IsAnyCpu { get; set; }

    [MarkoutPropertyName("Prefers 32-bit")]
    public bool Prefers32Bit { get; set; }

    [MarkoutPropertyName("Executable")]
    public bool IsExecutable { get; set; }

    [MarkoutPropertyName("DLL")]
    public bool IsDll { get; set; }

    // Assembly characteristics
    [MarkoutPropertyName("Signed")]
    public bool IsSigned { get; set; }

    [MarkoutPropertyName("Public Key Token")]
    public string? PublicKeyToken { get; set; }

    [MarkoutPropertyName("Unsafe Code")]
    public bool HasUnsafeCode { get; set; }

    // Metadata
    [MarkoutPropertyName("Runtime Version")]
    public string? RuntimeVersion { get; set; }

    [MarkoutPropertyName("Metadata Version")]
    public int MetadataVersion { get; set; }

    // Compilation type detection
    [MarkoutPropertyName("Compilation Type")]
    public string? CompilationType { get; set; }  // "CoreCLR", "NativeAOT", "Native", "ReadyToRun"

    [MarkoutPropertyName("Native AOT")]
    public bool IsNativeAot { get; set; }

    [MarkoutPropertyName("Ready To Run")]
    public bool IsReadyToRun { get; set; }

    [MarkoutPropertyName("Managed Metadata")]
    public bool HasManagedMetadata { get; set; }

    [MarkoutPropertyName("COR Header")]
    public bool HasCorHeader { get; set; }

    [MarkoutPropertyName("IL Code")]
    public bool HasILCode { get; set; }

    /// <summary>
    /// List of assemblies referenced by this assembly.
    /// </summary>
    [MarkoutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AssemblyReference>? References { get; set; }

    /// <summary>
    /// Transitive reference tree (when --transitive is used).
    /// </summary>
    [MarkoutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AssemblyReferenceNode>? TransitiveReferences { get; set; }
}
