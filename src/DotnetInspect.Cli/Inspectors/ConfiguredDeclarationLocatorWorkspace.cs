using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// CLI adoption of the Workspace-resident declaration locator for explicit
/// Package and Platform Library source contexts.
/// </summary>
internal sealed class ConfiguredDeclarationLocatorWorkspace
    : IAsyncDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly WorkspaceDeclarationLocator _locator;
    readonly bool _includeAll;
    readonly IReadOnlyDictionary<int, string> _sourceNames;
    readonly List<TypeDeclarationLocatorSectionResult> _sections = [];
    bool _closed;

    ConfiguredDeclarationLocatorWorkspace(
        InspectionWorkspace workspace,
        bool includeAll,
        bool hasFailures,
        IReadOnlyDictionary<int, string> sourceNames)
    {
        _workspace = workspace;
        _locator = workspace.GetDeclarationLocator();
        _includeAll = includeAll;
        _sourceNames = sourceNames;
        HasFailures = hasFailures;
    }

    internal bool HasFailures { get; private set; }

    internal IReadOnlyList<TypeDeclarationLocatorSectionResult> Sections =>
        _sections;

    internal static bool IsEligible(
        AssemblySetRequest request,
        string? targetFramework)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(targetFramework)
            || string.Equals(
                targetFramework,
                "all",
                StringComparison.OrdinalIgnoreCase)
            || request.Packages.Count + request.PlatformAssemblies.Count == 0
            || request.Assemblies.Count != 0
            || request.PlatformFrameworks.Count != 0
            || request.Projects.Count != 0
            || request.Directories.Count != 0)
        {
            return false;
        }

        foreach (string packageReference in request.Packages)
        {
            (string packageId, string? version) =
                PackageReferenceParser.Parse(packageReference);
            if (version is null
                || PackageCoordinateResolver.Validate(
                    new PackageCoordinate(packageId, version))
                    is not null)
            {
                return false;
            }
        }

        return true;
    }

    internal static async Task<ConfiguredDeclarationLocatorWorkspace> OpenAsync(
        FindOptions options,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(options.Tfm))
        {
            throw new ArgumentException(
                "A declaration-locator search requires a target framework.",
                nameof(options));
        }

        WorkspacePlan plan =
            FindSourceCollector.CreateWorkspacePlan(options);
        var workspace = new InspectionWorkspace(plan);
        var loadOptions = new WorkspaceContextLoadOptions
        {
            HttpClient = httpClient,
            SourceAuthorization =
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            UseVersionCache = false,
            Log = logger.Log,
        };
        bool hasFailures = false;
        var sourceNames = new Dictionary<int, string>();

        try
        {
            foreach (string packageReference in options.Packages)
            {
                (string packageId, string? version) =
                    PackageReferenceParser.Parse(packageReference);
                WorkspaceDeclarationContext context =
                    await WorkspaceContextLoader
                        .LoadDeclarationContextAsync(
                            workspace,
                            new WorkspaceContextInput
                            {
                                Framework = options.Tfm,
                                Members =
                                [
                                    WorkspaceMemberCoordinate.Package(
                                        packageId,
                                        version),
                                ],
                            },
                            loadOptions,
                            cancellationToken).ConfigureAwait(false);
                sourceNames.Add(context.Receipt.Order, packageId);
                hasFailures |= context.ContextLoadOutcome
                    is WorkspaceContextLoadOutcome.Failed;
            }

            foreach (string assembly in options.PlatformAssemblies)
            {
                PlatformContext? platform =
                    await ResolvePlatformContextAsync(
                        assembly,
                        options.Tfm,
                        options.SourceOptions,
                        logger,
                        httpClient,
                        cancellationToken).ConfigureAwait(false);
                if (platform is null)
                {
                    hasFailures = true;
                    continue;
                }

                WorkspaceDeclarationContext context =
                    await WorkspaceContextLoader
                        .LoadDeclarationContextAsync(
                            workspace,
                            new WorkspaceContextInput
                            {
                                Framework = options.Tfm,
                                Members =
                                [
                                    WorkspaceMemberCoordinate.Platform(
                                        platform.Family,
                                        assembly,
                                        platform.Version),
                                ],
                            },
                            loadOptions,
                            cancellationToken).ConfigureAwait(false);
                sourceNames.Add(context.Receipt.Order, platform.Family);
                hasFailures |= context.ContextLoadOutcome
                    is WorkspaceContextLoadOutcome.Failed;
            }

            return new(
                workspace,
                options.IncludeAll,
                hasFailures,
                sourceNames);
        }
        catch
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<TypeDeclarationLocatorSectionResult> LocateAsync(
        IReadOnlyList<string> patterns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        TypeDeclarationLocatorResult result =
            await _locator.ExecuteAsync(
                [
                    .. patterns.Select(
                        static pattern =>
                            new TypeDeclarationLocatorRequest.Pattern(
                                pattern)),
                ],
                _includeAll,
                cancellationToken).ConfigureAwait(false);
        TypeDeclarationLocatorSectionResult section =
            TypeDeclarationLocatorSection.Project(
                result,
                TypeDeclarationLocatorSectionPlan.All);
        HasFailures |= section switch
        {
            TypeDeclarationLocatorSectionResult.Rejected => true,
            TypeDeclarationLocatorSectionResult.Evaluated evaluated =>
                evaluated.Answers.Any(
                    static answer => !answer.IsComplete),
            _ => throw new InvalidOperationException(
                "Unknown declaration locator section result."),
        };
        _sections.Add(section);
        return section;
    }

    internal string SourceFor(
        TypeDeclarationLocatorSectionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return _sourceNames.TryGetValue(
            candidate.Observation.ContextOrder,
            out string? source)
                ? source
                : throw new InvalidOperationException(
                    "The locator candidate has no originating Find source.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        _closed = true;
        await _workspace.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<PlatformContext?> ResolvePlatformContextAsync(
        string assembly,
        string targetFramework,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        if (!PlatformResolver.TryGetFrameworkSpecsForTargetFramework(
                targetFramework,
                out IReadOnlyList<string> frameworkSpecs))
        {
            CommandError.WriteWarning(
                $"Target framework '{targetFramework}' cannot select "
                + $"Platform Library '{assembly}'.");
            return null;
        }

        var failures = new List<string>();
        foreach (string frameworkSpec in frameworkSpecs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution =
                await PlatformResolver.ResolveAssemblyAsync(
                    assembly,
                    httpClient,
                    logger.Log,
                    frameworkSpec,
                    sourceOptions: sourceOptions).ConfigureAwait(false);
            if (resolution.AssemblyPath is not null
                && resolution.Framework is "runtime" or "aspnetcore"
                && resolution.Version is not null)
            {
                return new(
                    resolution.Framework,
                    resolution.Version);
            }
            if (!string.IsNullOrWhiteSpace(resolution.Error))
                failures.Add(resolution.Error);
        }

        string detail = failures.Count == 0
            ? "No supported runtime or ASP.NET Core family supplied it."
            : string.Join("; ", failures.Distinct());
        CommandError.WriteWarning(
            $"Platform Library '{assembly}' could not be admitted to "
            + $"the declaration locator: {detail}");
        return null;
    }

    private sealed record PlatformContext(
        string Family,
        string Version);
}
