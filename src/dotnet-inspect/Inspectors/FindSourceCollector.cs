using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Shared source-resolution skeleton for the <c>find</c> command's closed-set searches. Both the type
/// search (<see cref="TypeSearchService"/>) and the member search (<see cref="MemberSearchService"/>)
/// resolve the same six ordered sources into the same <see cref="AssemblySetRequest"/> shape and, when
/// a result limit is active, stream one source at a time so later sources are never resolved once the
/// limit is met. Only the per-source scan (types vs members) differs between the two callers.
/// </summary>
internal static class FindSourceCollector
{
    /// <summary>
    /// Builds the find request. With no per-source overrides it targets every configured source; the
    /// streaming callers pass a single populated source (and empty lists for the rest) so each source
    /// is resolved and scanned in isolation.
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

    /// <summary>
    /// Resolves each configured source one at a time in <see cref="AssemblySetSourceKind"/> order,
    /// invoking <paramref name="process"/> for each, and short-circuits before resolving the next
    /// source once <paramref name="reachedLimit"/> reports the caller's limit is met. This preserves
    /// the find command's early-exit contract: a source that is never needed is never resolved (so a
    /// missing bin directory past the limit never surfaces an error).
    /// </summary>
    public static async Task StreamSourcesAsync(
        FindOptions options,
        Func<bool> reachedLimit,
        Func<AssemblySetRequest, Task> process)
    {
        foreach (var package in options.Packages)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [package], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: []));
        }

        foreach (var assembly in options.Assemblies)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [assembly], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: []));
        }

        foreach (var platformAssembly in options.PlatformAssemblies)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [platformAssembly],
                platformFrameworks: [], projects: [], directories: []));
        }

        foreach (var framework in options.PlatformFrameworks)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [framework], projects: [], directories: []));
        }

        foreach (var project in options.Projects)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [project], directories: []));
        }

        foreach (var directory in options.BinPaths)
        {
            if (reachedLimit()) return;
            await process(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: [directory]));
        }
    }
}
