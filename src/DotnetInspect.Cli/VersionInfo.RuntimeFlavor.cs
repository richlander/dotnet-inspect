namespace DotnetInspect.Cli;

public static partial class VersionInfo
{
#if DOTNET_INSPECT_NATIVEAOT
    private const string PublishedRuntimeFlavor = "NativeAOT";
#else
    private const string PublishedRuntimeFlavor = "CoreCLR";
#endif
}
