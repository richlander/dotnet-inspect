using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable]
public class AssemblyInfo
{
    [MarkOutPropertyName("Name")]
    public string? AssemblyName { get; set; }

    [MarkOutPropertyName("Assembly Version")]
    public string? AssemblyVersion { get; set; }

    [MarkOutPropertyName("File Version")]
    public string? FileVersion { get; set; }

    [MarkOutPropertyName("Informational Version")]
    public string? InformationalVersion { get; set; }

    [MarkOutPropertyName("Target Framework")]
    public string? TargetFramework { get; set; }

    public string? Culture { get; set; }

    // PE header information
    public string? Architecture { get; set; }  // AnyCPU, x86, x64, ARM, ARM64

    [MarkOutPropertyName("Any CPU")]
    public bool IsAnyCpu { get; set; }

    [MarkOutPropertyName("Prefers 32-bit")]
    public bool Prefers32Bit { get; set; }

    [MarkOutPropertyName("Executable")]
    public bool IsExecutable { get; set; }

    [MarkOutPropertyName("DLL")]
    public bool IsDll { get; set; }

    // Assembly characteristics
    [MarkOutPropertyName("Signed")]
    public bool IsSigned { get; set; }

    [MarkOutPropertyName("Public Key Token")]
    public string? PublicKeyToken { get; set; }

    [MarkOutPropertyName("Unsafe Code")]
    public bool HasUnsafeCode { get; set; }

    // Metadata
    [MarkOutPropertyName("Runtime Version")]
    public string? RuntimeVersion { get; set; }

    [MarkOutPropertyName("Metadata Version")]
    public int MetadataVersion { get; set; }

    // Compilation type detection
    [MarkOutPropertyName("Compilation Type")]
    public string? CompilationType { get; set; }  // "CoreCLR", "NativeAOT", "Native", "ReadyToRun"

    [MarkOutPropertyName("Native AOT")]
    public bool IsNativeAot { get; set; }

    [MarkOutPropertyName("Ready To Run")]
    public bool IsReadyToRun { get; set; }

    [MarkOutPropertyName("Managed Metadata")]
    public bool HasManagedMetadata { get; set; }

    [MarkOutPropertyName("COR Header")]
    public bool HasCorHeader { get; set; }

    [MarkOutPropertyName("IL Code")]
    public bool HasILCode { get; set; }
}
