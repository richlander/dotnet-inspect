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
/// Package source contexts whose selected implementation population preserves
/// Find's established assembly inventory.
/// </summary>
internal sealed class ConfiguredDeclarationLocatorWorkspace
    : IAsyncDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly IReadOnlyDictionary<int, string> _sourceNames;
    readonly List<
        InspectionEnvelope<TypeDeclarationLocatorSectionResult>>
        _inspections = [];
    readonly HashSet<LocatorFailureKey> _reportedLocatorFailures = [];
    bool _closed;

    ConfiguredDeclarationLocatorWorkspace(
        InspectionWorkspace workspace,
        bool hasFailures,
        bool requiresCompatibility,
        IReadOnlyDictionary<int, string> sourceNames)
    {
        _workspace = workspace;
        _sourceNames = sourceNames;
        HasLoadFailures = hasFailures;
        HasFailures = hasFailures;
        RequiresCompatibility = requiresCompatibility;
    }

    internal bool HasLoadFailures { get; }

    internal bool HasFailures { get; private set; }

    internal bool RequiresCompatibility { get; }

    internal IReadOnlyList<
        InspectionEnvelope<TypeDeclarationLocatorSectionResult>>
        Inspections => _inspections;

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
            || request.Packages.Count == 0
            || request.Assemblies.Count != 0
            || request.PlatformAssemblies.Count != 0
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
            IncludePackageRootBindings = true,
        };
        bool hasFailures = false;
        bool requiresCompatibility = false;
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
                if (context.ContextLoadOutcome
                    is WorkspaceContextLoadOutcome.Loaded loaded
                    && loaded.PackageRoots.Any(
                        static root =>
                            root.Root
                                .HasUnselectedTargetFrameworkAssemblyCandidates))
                {
                    requiresCompatibility = true;
                }
            }

            return new(
                workspace,
                hasFailures,
                requiresCompatibility,
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
        InspectionEnvelope<TypeDeclarationLocatorSectionResult> inspection =
            await TypeDeclarationLocatorInspection.ExecuteAsync(
                _workspace,
                [
                    .. patterns.Select(
                        static pattern =>
                            new TypeDeclarationLocatorRequest.Pattern(
                                pattern)),
                ],
                TypeDeclarationLocatorSectionPlan.All,
                // Find's visibility policy differs from the locator's public
                // surface. Retain all declarations and apply Find policy from
                // Metadata-issued facts during row projection.
                includeAll: true,
                cancellationToken).ConfigureAwait(false);
        TypeDeclarationLocatorSectionResult section = inspection.Content;
        HasFailures |= section switch
        {
            TypeDeclarationLocatorSectionResult.Rejected => true,
            TypeDeclarationLocatorSectionResult.Evaluated evaluated =>
                evaluated.Answers.Any(
                    static answer => !answer.IsComplete),
            _ => throw new InvalidOperationException(
                "Unknown declaration locator section result."),
        };
        WriteLocatorFailures(section);
        _inspections.Add(inspection);
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

    private void WriteLocatorFailures(
        TypeDeclarationLocatorSectionResult section)
    {
        if (section is TypeDeclarationLocatorSectionResult.Rejected rejected)
        {
            if (HasLoadFailures
                && rejected.RejectionKind
                    is TypeDeclarationLocatorRejectionKind
                        .PopulationUnavailable)
            {
                return;
            }

            string detail = rejected.PopulationFailure is { } failure
                ? $": {failure}"
                : ".";
            WriteOnce(
                new(-1, -1, rejected.RejectionKind.ToString(), detail),
                $"Type declaration search was rejected as "
                + $"{rejected.RejectionKind}{detail}");
            return;
        }

        var evaluated =
            (TypeDeclarationLocatorSectionResult.Evaluated)section;
        foreach (TypeDeclarationLocatorMemberCoverage member
            in evaluated.Members.Where(
                static member => !member.IsComplete))
        {
            string subject =
                member.Observation.AssemblyIdentity.Name;
            string detail = member switch
            {
                { CandidateFailure: { } failure } =>
                    failure.Detail,
                { WorkspaceFailure: { } failure } =>
                    $"workspace population failed as {failure}",
                {
                    Outcome:
                        TypeDeclarationLocatorMemberCoverageKind
                            .CoordinateUnavailable,
                } => "no exact source coordinate was available",
                {
                    Outcome:
                        TypeDeclarationLocatorMemberCoverageKind
                            .NotEvaluated,
                } => "the declaration inventory was not evaluated",
                {
                    Outcome:
                        TypeDeclarationLocatorMemberCoverageKind
                            .Searched,
                } =>
                    $"{member.UnsupportedDeclarations.Length} unsupported "
                    + "declaration form(s) were encountered",
                _ => $"evaluation ended as {member.Outcome}",
            };
            WriteOnce(
                new(
                    member.Observation.ContextOrder,
                    member.Observation.MemberOrder,
                    member.Outcome.ToString(),
                    detail),
                $"Type declaration search in '{subject}' was incomplete: "
                + detail);
        }

        void WriteOnce(LocatorFailureKey key, string message)
        {
            if (_reportedLocatorFailures.Add(key))
                CommandError.WriteWarning(message);
        }
    }

    private sealed record LocatorFailureKey(
        int ContextOrder,
        int MemberOrder,
        string Outcome,
        string Detail);
}
