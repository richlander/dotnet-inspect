using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Inspectors;

internal sealed record FindSearchResult<T>(
    List<T> Rows,
    bool HasFailures);

/// <summary>
/// Shared source-request construction for the <c>find</c> command's
/// closed-set type and member searches.
/// </summary>
internal static class FindSourceCollector
{
    /// <summary>
    /// Builds the complete ordered find request.
    /// </summary>
    public static AssemblySetRequest BuildFindRequest(
        FindOptions options,
        IReadOnlyList<string>? packages = null,
        IReadOnlyList<string>? assemblies = null,
        IReadOnlyList<string>? platformAssemblies = null,
        IReadOnlyList<string>? platformFrameworks = null,
        IReadOnlyList<string>? projects = null,
        IReadOnlyList<string>? directories = null)
    {
        return new AssemblySetRequest
        {
            Packages = packages ?? options.Packages,
            Assemblies = assemblies ?? options.Assemblies,
            PlatformAssemblies = platformAssemblies ?? options.PlatformAssemblies,
            PlatformFrameworks = platformFrameworks ?? options.PlatformFrameworks,
            Projects = projects ?? options.Projects,
            Directories = directories ?? options.BinPaths,
            Tfm = options.Tfm,
            SourceOptions = options.SourceOptions,
            TempDirPrefix = "inspect-find",
            PlatformAssemblyFrameworkHint = options.PlatformFrameworks.Length > 0
                ? options.PlatformFrameworks[0]
                : null,
            IncludePackageRuntimeAssemblies = true,
            SourceOrder =
            [
                AssemblySetSourceKind.Package,
                AssemblySetSourceKind.Assembly,
                AssemblySetSourceKind.PlatformAssembly,
                AssemblySetSourceKind.PlatformFramework,
                AssemblySetSourceKind.Project,
                AssemblySetSourceKind.Directory,
            ],
        };
    }

    public static async Task StreamSourcesAsync(
        FindOptions options,
        Func<bool> reachedLimit,
        Func<AssemblySetRequest, Task> process)
    {
        foreach (string package in options.Packages)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [package], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: []));
        }

        foreach (string assembly in options.Assemblies)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [assembly], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: []));
        }

        foreach (string platformAssembly in options.PlatformAssemblies)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [platformAssembly],
                platformFrameworks: [], projects: [], directories: []));
        }

        foreach (string framework in options.PlatformFrameworks)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [framework], projects: [], directories: []));
        }

        foreach (string project in options.Projects)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [project], directories: []));
        }

        foreach (string directory in options.BinPaths)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: [directory]));
        }
    }
}
