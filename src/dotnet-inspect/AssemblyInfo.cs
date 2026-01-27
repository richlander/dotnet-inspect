using Markout;

namespace DotnetInspector;

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
}
