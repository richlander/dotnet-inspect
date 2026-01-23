namespace DotnetInspector;

public class AssemblyInfo
{
    public string? AssemblyName { get; set; }
    public string? AssemblyVersion { get; set; }
    public string? FileVersion { get; set; }
    public string? InformationalVersion { get; set; }
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
}
