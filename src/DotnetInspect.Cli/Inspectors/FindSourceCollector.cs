using DotnetInspect.Cli.Options;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Inspectors;

internal enum FindSearchCompletion
{
    Exhausted,
    ResultLimitReached,
    Incomplete,
}

internal sealed record FindSearchResult<T>(
    List<T> Rows,
    bool HasFailures,
    IReadOnlyList<string>? UnmatchedPatterns = null)
{
    public bool SourceSelectionIncomplete { get; init; }

    public FindSearchCompletion Completion { get; init; } =
        FindSearchCompletion.Exhausted;

    public IReadOnlyList<
        InspectionEnvelope<TypeDeclarationLocatorSectionResult>>
        LocatorInspections
    {
        get;
        init;
    } = [];

    public IReadOnlyList<TypeDeclarationLocatorSectionResult> LocatorSections =>
        [
            .. LocatorInspections.Select(
                static inspection => inspection.Content),
        ];
}

/// <summary>
/// Shared source-request construction for the <c>find</c> command's
/// closed-set type and member searches.
/// </summary>
internal static class FindSourceCollector
{
    internal static WorkspacePlan CreateWorkspacePlan(
        FindOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsesImplicitPlatform
            ? EcosystemPackCatalog.CreatePlatformWorkspacePlan()
            : options.Ecosystems is not null
                ? EcosystemPackCatalog.CreateWorkspacePlan(
                    options.Ecosystems)
                : WorkspacePlan.Empty;
    }

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

    internal static IReadOnlyList<AssemblySetRequest>
        BuildOrderedSourceRequests(FindOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var requests = new List<AssemblySetRequest>();
        foreach (string package in options.Packages)
        {
            requests.Add(BuildFindRequest(options,
                packages: [package], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: []));
        }
        foreach (string assembly in options.Assemblies)
        {
            requests.Add(BuildFindRequest(options,
                packages: [], assemblies: [assembly], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: []));
        }
        foreach (string platformAssembly in options.PlatformAssemblies)
        {
            requests.Add(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [platformAssembly],
                platformFrameworks: [], projects: [], directories: []));
        }
        foreach (string framework in options.PlatformFrameworks)
        {
            requests.Add(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [framework], projects: [], directories: []));
        }
        foreach (string project in options.Projects)
        {
            requests.Add(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [project], directories: []));
        }
        foreach (string directory in options.BinPaths)
        {
            requests.Add(BuildFindRequest(options,
                packages: [], assemblies: [], platformAssemblies: [],
                platformFrameworks: [], projects: [], directories: [directory]));
        }
        return requests;
    }
}
