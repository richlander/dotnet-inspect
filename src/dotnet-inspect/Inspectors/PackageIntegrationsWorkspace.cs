using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record PackageIntegrationAssembly(
    string Path,
    string? TargetFramework,
    string? ContextKey = null);

/// <summary>
/// Owns the binding-consistent package groups used by one all-library
/// Integrations request.
/// </summary>
internal sealed class PackageIntegrationsWorkspace : IAsyncDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly IDisposable _realization;
    readonly Dictionary<string, ParticipantResult> _participants;
    readonly Dictionary<string, string> _preflightFailures;
    readonly HashSet<string> _withoutAssembly = new(StringComparer.Ordinal);
    readonly bool _includeIntegrationOpportunities;

    PackageIntegrationsWorkspace(
        InspectionWorkspace workspace,
        IDisposable realization,
        Dictionary<string, ParticipantResult> participants,
        Dictionary<string, string> preflightFailures,
        int contextGroupCount,
        bool includeIntegrationOpportunities)
    {
        _workspace = workspace;
        _realization = realization;
        _participants = participants;
        _preflightFailures = preflightFailures;
        _includeIntegrationOpportunities =
            includeIntegrationOpportunities;
        ContextGroupCount = contextGroupCount;
    }

    internal int ContextGroupCount { get; }

    internal long RetainedImageBytes =>
        _participants.Values
            .SelectMany(static participant =>
                new[]
                {
                    participant.SelectedGroup,
                    participant.QueryGroup,
                })
            .Distinct()
            .Sum(static group => group.RetainedImageBytes);

    internal static async ValueTask<PackageIntegrationsWorkspace> CreateSelectedAsync(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        string extractionRoot,
        PackageInspectionInput input,
        long? maxRetainedImageBytes = null,
        bool includeIntegrationOpportunities = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        ArgumentNullException.ThrowIfNull(input);

        PackageIntegrationAssembly[] requested = [.. assemblies];
        PackageInspectionSelection selection = input.SelectAssemblies(
            requested.Select(assembly => new PackageInspectionAssembly(
                Path.GetRelativePath(extractionRoot, Path.GetFullPath(assembly.Path))
                    .Replace('\\', '/'),
                assembly.TargetFramework,
                assembly.ContextKey)));
        InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        try
        {
            PackageInspectionAssemblyContext realization =
                await workspace.RealizePackageInspectionAsync(
                    selection,
                    references => new SourceRelativeAssemblyGroupBindingPolicy(
                        references.Select(reference =>
                            (reference.Assembly,
                                (IAssemblyBindingPolicy)new AssemblyDependencyResolver(
                                    new AssemblyDependencyResolutionOptions(
                                        Path.Combine(extractionRoot, reference.Selection.Path))
                                    {
                                        TargetFramework = reference.Selection.TargetFramework,
                                    })))),
                    groupOptions: maxRetainedImageBytes is long maxBytes
                        ? new AssemblyContextGroupOptions { MaxRetainedImageBytes = maxBytes }
                        : null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            var results = new Dictionary<string, ParticipantResult>(StringComparer.Ordinal);
            var failures = new Dictionary<string, string>(StringComparer.Ordinal);
            var withoutAssembly = new List<string>();
            for (int index = 0; index < requested.Length; index++)
            {
                string path = Path.GetFullPath(requested[index].Path);
                switch (realization.Assemblies[index])
                {
                    case PackageInspectionAssemblyOutcome.Available available:
                        results.Add(path, new ParticipantResult(
                            available.Group, available.Participant,
                            available.Group, available.Participant));
                        break;
                    case PackageInspectionAssemblyOutcome.Unavailable unavailable:
                        failures.Add(path, unavailable.Reason);
                        break;
                    case PackageInspectionAssemblyOutcome.WithoutAssembly:
                        withoutAssembly.Add(path);
                        break;
                }
            }

            var result = new PackageIntegrationsWorkspace(
                workspace, realization, results, failures,
                realization.Groups.Length, includeIntegrationOpportunities);
            result._withoutAssembly.UnionWith(withoutAssembly);
            return result;
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure).ConfigureAwait(false);
            throw;
        }
    }

    internal static async ValueTask<PackageIntegrationsWorkspace>
        CreateArtifactBackedAsync(
            IEnumerable<PackageIntegrationAssembly> assemblies,
            string extractionRoot,
            PackageRootBinding package,
            bool includeIntegrationOpportunities = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        ArgumentNullException.ThrowIfNull(package);

        PackageIntegrationAssembly[] requested = [.. assemblies];
        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        try
        {
            PackageAssemblyContextRealization realization =
                await workspace.RealizePackageAssemblyContextRolesAsync(
                        package,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            var results = new Dictionary<string, ParticipantResult>(
                StringComparer.Ordinal);
            var preflightFailures = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (!realization.HasAssemblyContexts)
            {
                foreach (PackageIntegrationAssembly assembly in requested)
                {
                    preflightFailures.Add(
                        Path.GetFullPath(assembly.Path),
                        "The selected library has no artifact-backed compile role.");
                }

                return new PackageIntegrationsWorkspace(
                    workspace,
                    realization,
                    results,
                    preflightFailures,
                    contextGroupCount: 0,
                    includeIntegrationOpportunities);
            }

            Dictionary<string, ParticipantResult> roles =
                ArtifactRoles(realization);
            foreach (PackageIntegrationAssembly assembly in requested)
            {
                string fullPath = Path.GetFullPath(assembly.Path);
                string packagePath = Path.GetRelativePath(
                        extractionRoot,
                        fullPath)
                    .Replace('\\', '/');
                if (roles.TryGetValue(
                        packagePath,
                        out ParticipantResult? participant))
                {
                    results.Add(fullPath, participant);
                }
                else
                {
                    preflightFailures.Add(
                        fullPath,
                        "The selected library is outside the artifact-backed package roles.");
                }
            }

            int contextGroupCount = results.Values
                .SelectMany(static participant =>
                    new[]
                    {
                        participant.SelectedGroup,
                        participant.QueryGroup,
                    })
                .Distinct()
                .Count();
            return new PackageIntegrationsWorkspace(
                workspace,
                realization,
                results,
                preflightFailures,
                contextGroupCount,
                includeIntegrationOpportunities);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure).ConfigureAwait(false);
            throw;
        }
    }

    internal static async ValueTask<PackageIntegrationsWorkspace?>
        TryCreateArtifactBackedAsync(
            IEnumerable<PackageIntegrationAssembly> assemblies,
            string extractionRoot,
            PackageRootBinding package,
            bool includeIntegrationOpportunities = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        ArgumentNullException.ThrowIfNull(package);

        PackageIntegrationAssembly[] requested = [.. assemblies];
        PackageCompileAssetSelection selection =
            package.Root.AssetSelection;
        if (!selection.IsSelected
            || !HasExactSurfaceSelection(
                requested,
                extractionRoot,
                selection.Assets))
        {
            return null;
        }

        try
        {
            return await CreateArtifactBackedAsync(
                    requested,
                    extractionRoot,
                    package,
                    includeIntegrationOpportunities,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PackageAssemblyRoleCorrespondenceException failure) when (
            !failure.Data.Contains("DotnetInspector.Artifacts.Workspaces.CleanupFailures")
            && !failure.Data.Contains("DotnetInspector.Queries.WorkspaceCleanupFailure"))
        {
            return null;
        }
    }

    static bool HasExactSurfaceSelection(
        IReadOnlyList<PackageIntegrationAssembly> requested,
        string extractionRoot,
        IReadOnlyList<PackageCompileAsset> surfaceAssets)
    {
        if (requested.Count != surfaceAssets.Count)
            return false;

        var requestedPaths = new HashSet<string>(
            requested.Select(assembly =>
                Path.GetRelativePath(
                        extractionRoot,
                        Path.GetFullPath(assembly.Path))
                    .Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        return requestedPaths.Count == requested.Count
            && surfaceAssets.All(asset =>
                requestedPaths.Remove(asset.Path))
            && requestedPaths.Count == 0;
    }

    static Dictionary<string, ParticipantResult> ArtifactRoles(
        PackageAssemblyContextRealization realization)
    {
        var results = new Dictionary<string, ParticipantResult>(
            StringComparer.OrdinalIgnoreCase);
        foreach (PackageAssemblyRoleParticipant surface
            in realization.SurfaceParticipants)
        {
            PackageAssemblyRoleParticipant queryParticipant =
                realization.ImplementationParticipant(surface)
                ?? surface;
            AssemblyContextGroup queryGroup =
                ReferenceEquals(queryParticipant, surface)
                    ? realization.SurfaceGroup
                    : realization.ImplementationGroup!;
            results.Add(
                surface.Asset.Path,
                new ParticipantResult(
                    realization.SurfaceGroup,
                    surface.Participant,
                    queryGroup,
                    queryParticipant.Participant));
        }

        if (realization.ImplementationGroup is { } implementationGroup)
        {
            foreach (PackageAssemblyRoleParticipant implementation
                in realization.ImplementationParticipants)
            {
                results.TryAdd(
                    implementation.Asset.Path,
                    new ParticipantResult(
                        implementationGroup,
                        implementation.Participant,
                        implementationGroup,
                        implementation.Participant));
            }
        }

        return results;
    }

    internal async Task<TResult> UseAssemblyAsync<TResult>(
        string path,
        Func<
            ResolvedAssemblyReference?,
            AssemblyIntegrationsEntry?,
            AssemblyIntegrationOpportunitiesEntry?,
            Task<TResult>> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(callback);

        if (!_participants.TryGetValue(
                Path.GetFullPath(path),
                out ParticipantResult? participant))
        {
            return await callback(null, null, null).ConfigureAwait(false);
        }

        if (ReferenceEquals(
                participant.SelectedParticipant,
                participant.QueryParticipant))
        {
            return await ExecuteQueryAsync(
                    participant.QueryGroup,
                    participant.QueryParticipant,
                    callback)
                .ConfigureAwait(false);
        }

        return await AssemblyContextIntegrationsQuery
            .ExecuteParticipantAsync(
                participant.SelectedGroup,
                participant.SelectedParticipant,
                (selectedAssembly, selectedIntegrations) =>
                    selectedAssembly is null
                        ? callback(
                            null,
                            selectedIntegrations,
                            null)
                        : ExecuteQueryAsync(
                            participant.QueryGroup,
                            participant.QueryParticipant,
                            (_, integrations, opportunities) =>
                                callback(
                                    selectedAssembly,
                                    integrations,
                                    opportunities)))
            .ConfigureAwait(false);

        Task<TResult> ExecuteQueryAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant queryParticipant,
            Func<
                ResolvedAssemblyReference?,
                AssemblyIntegrationsEntry?,
                AssemblyIntegrationOpportunitiesEntry?,
                Task<TResult>> consumer) =>
            _includeIntegrationOpportunities
                ? AssemblyContextIntegrationOpportunitiesQuery
                    .ExecuteParticipantAsync(
                        group,
                        queryParticipant,
                        consumer)
                : AssemblyContextIntegrationsQuery
                    .ExecuteParticipantAsync(
                        group,
                        queryParticipant,
                        (retained, integrations) =>
                            consumer(retained, integrations, null));
    }

    internal bool TryGetPreflightFailure(
        string path,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_preflightFailures.TryGetValue(
                Path.GetFullPath(path),
                out string? failure))
        {
            reason = failure;
            return true;
        }

        reason = "";
        return false;
    }

    internal bool HasNoAssembly(string path) =>
        _withoutAssembly.Contains(Path.GetFullPath(path));

    public async ValueTask DisposeAsync()
    {
        List<Exception>? failures = null;
        try
        {
            _realization.Dispose();
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        try
        {
            InspectionWorkspaceCloseReport report =
                await _workspace.CloseAsync().ConfigureAwait(false);
            if (!report.ArtifactSessionCleanupFailures.IsEmpty)
            {
                (failures ??= []).AddRange(
                    report.ArtifactSessionCleanupFailures);
            }
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    static async Task CloseAfterFailureAsync(
        InspectionWorkspace workspace,
        Exception failure)
    {
        try
        {
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            if (!report.ArtifactSessionCleanupFailures.IsEmpty)
                failure.Data["DotnetInspector.Artifacts.Workspaces.CleanupFailures"] =
                    report.ArtifactSessionCleanupFailures;
        }
        catch (Exception cleanupFailure)
        {
            failure.Data["DotnetInspector.Queries.WorkspaceCleanupFailure"] = cleanupFailure;
        }
    }

    sealed record ParticipantResult(
        AssemblyContextGroup SelectedGroup,
        AssemblyContextParticipant SelectedParticipant,
        AssemblyContextGroup QueryGroup,
        AssemblyContextParticipant QueryParticipant);
}
