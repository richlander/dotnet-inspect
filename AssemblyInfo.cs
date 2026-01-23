using MarkdownData;

namespace DotnetInspector;

[MdfSerializable]
public class AssemblyInfo
{
    [MdfPropertyName("Name")]
    public string? AssemblyName { get; set; }

    [MdfPropertyName("Assembly Version")]
    public string? AssemblyVersion { get; set; }

    [MdfPropertyName("File Version")]
    public string? FileVersion { get; set; }

    [MdfPropertyName("Informational Version")]
    public string? InformationalVersion { get; set; }

    [MdfPropertyName("Target Framework")]
    public string? TargetFramework { get; set; }

    public string? Culture { get; set; }

    // PE header information
    public string? Architecture { get; set; }  // AnyCPU, x86, x64, ARM, ARM64

    [MdfPropertyName("Any CPU")]
    public bool IsAnyCpu { get; set; }

    [MdfPropertyName("Prefers 32-bit")]
    public bool Prefers32Bit { get; set; }

    [MdfPropertyName("Executable")]
    public bool IsExecutable { get; set; }

    [MdfPropertyName("DLL")]
    public bool IsDll { get; set; }

    // Assembly characteristics
    [MdfPropertyName("Signed")]
    public bool IsSigned { get; set; }

    [MdfPropertyName("Public Key Token")]
    public string? PublicKeyToken { get; set; }

    [MdfPropertyName("Unsafe Code")]
    public bool HasUnsafeCode { get; set; }

    // Metadata
    [MdfPropertyName("Runtime Version")]
    public string? RuntimeVersion { get; set; }

    [MdfPropertyName("Metadata Version")]
    public int MetadataVersion { get; set; }

    // Compilation type detection
    [MdfPropertyName("Compilation Type")]
    public string? CompilationType { get; set; }  // "CoreCLR", "NativeAOT", "Native", "ReadyToRun"

    [MdfPropertyName("Native AOT")]
    public bool IsNativeAot { get; set; }

    [MdfPropertyName("Ready To Run")]
    public bool IsReadyToRun { get; set; }

    [MdfPropertyName("Managed Metadata")]
    public bool HasManagedMetadata { get; set; }

    [MdfPropertyName("COR Header")]
    public bool HasCorHeader { get; set; }

    [MdfPropertyName("IL Code")]
    public bool HasILCode { get; set; }
}
