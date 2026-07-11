using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

internal static class AssemblySourceOptionsExtensions
{
    public static AssemblySetRequest ToAssemblySetRequest(
        this IAssemblySourceOptions options,
        string tempDirPrefix = "inspect",
        IReadOnlyList<string>? directories = null)
        => new()
        {
            Packages = options.Packages,
            Assemblies = options.Assemblies,
            PlatformAssemblies = options.PlatformAssemblies,
            PlatformFrameworks = options.PlatformFrameworks,
            Projects = options.Projects,
            Directories = directories ?? [],
            Tfm = options.Tfm,
            SourceOptions = options.SourceOptions,
            TempDirPrefix = tempDirPrefix,
        };
}
