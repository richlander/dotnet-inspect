using System.Collections.Immutable;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

/// <summary>
/// CLI-owned execution over one explicit package Root committed to an
/// invocation-scoped Workspace.
/// </summary>
internal sealed class ConfiguredPackageSearchWorkspace : IAsyncDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly PackageArtifactRootCorrespondence _correspondence;
    readonly ArtifactRootGenerationReference _generation;
    readonly string _packageDisplay;
    bool _closed;

    ConfiguredPackageSearchWorkspace(
        InspectionWorkspace workspace,
        PackageArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        string packageDisplay)
    {
        _workspace = workspace;
        _correspondence = correspondence;
        _generation = generation;
        _packageDisplay = packageDisplay;
    }

    internal static bool IsEligible(
        SearchSourceSelection? selection,
        AssemblySetRequest request,
        string? targetFramework,
        int? resultLimit = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return resultLimit is null
            && !string.IsNullOrWhiteSpace(targetFramework)
            && !string.Equals(targetFramework, "all", StringComparison.OrdinalIgnoreCase)
            && selection is
            {
                UsesImplicitPlatform: false,
                Frameworks.Count: 0,
                OtherSources.Count: 0,
                Packages: [SourceSelector.PackageReference],
            }
            && request.Packages.Count == 1
            && request.Assemblies.Count == 0
            && request.PlatformAssemblies.Count == 0
            && request.PlatformFrameworks.Count == 0
            && request.Projects.Count == 0
            && request.Directories.Count == 0;
    }

    internal static async ValueTask<ConfiguredPackageSearchWorkspace?>
        OpenAsync(
            HttpClient httpClient,
            AssemblySetRequest request,
            string targetFramework,
            Action<string>? log,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        if (request.Packages is not [string packageSpec])
        {
            throw new ArgumentException(
                "Configured package search requires exactly one package.",
                nameof(request));
        }

        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        try
        {
            if (!InspectionGraphCommand.TryCreateMembers(
                    [packageSpec],
                    out WorkspaceMemberCoordinate[] members))
            {
                await CloseAsync(workspace).ConfigureAwait(false);
                return null;
            }

            WorkspacePackageRootAcquisitionOutcome acquisition =
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                    new WorkspaceContextInput
                    {
                        Framework = targetFramework,
                        Members = members,
                    },
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = httpClient,
                        SourceAuthorization =
                            new SourcePolicyPackageSourceAuthorization(
                                request.SourceOptions),
                        PackageStore = new FileSystemPackageStore(),
                        UseVersionCache = true,
                        Log = log,
                    },
                    cancellationToken).ConfigureAwait(false);
            if (acquisition
                is WorkspacePackageRootAcquisitionOutcome.Failed failed)
            {
                foreach (WorkspaceContextLoadFailure failure
                    in failed.Failures)
                {
                    CommandError.WriteWarning(
                        $"Could not load package Root '{packageSpec}': "
                        + $"{failure.Kind}: {failure.Message}");
                }
                await CloseAsync(workspace).ConfigureAwait(false);
                return null;
            }

            PackageRootBinding binding =
                ((WorkspacePackageRootAcquisitionOutcome.Acquired)acquisition)
                    .Root;
            WorkspaceScopeReadResult read =
                await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
            if (read is WorkspaceScopeReadResult.Unavailable unavailable)
            {
                CommandError.WriteWarning(
                    $"Could not open package Root '{packageSpec}': "
                    + unavailable.RuntimeFailure);
                await CloseAsync(workspace).ConfigureAwait(false);
                return null;
            }

            WorkspaceScopeOperationResult replacement =
                await workspace.ReplaceScopeAsync(
                    ((WorkspaceScopeReadResult.Available)read)
                        .Snapshot.Revision,
                    [binding],
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    cancellationToken).ConfigureAwait(false);
            if (replacement
                is not WorkspaceScopeOperationResult.Committed committed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CommandError.WriteWarning(
                    $"Could not commit package Root '{packageSpec}': "
                    + Describe(replacement));
                await CloseAsync(workspace).ConfigureAwait(false);
                return null;
            }

            WorkspacePackageOccurrenceDescriptor root =
                committed.Snapshot.Packages.Single();
            if (root.Occurrence.Correspondence
                    is not PackageArtifactRootCorrespondence correspondence
                || root.Realization.Status
                    is not ArtifactRootRealizationStatus.Ready ready)
            {
                throw new InvalidOperationException(
                    "The committed package Root did not publish query admission.");
            }

            log?.Invoke(
                $"Using committed package Root for search: "
                + $"{binding.Root.PackageId}@{binding.Root.PackageVersion} "
                + $"({binding.Root.AssetSelection.Status}).");
            return new(
                workspace,
                correspondence,
                ready.Generation,
                $"{binding.Root.PackageId}@{binding.Root.PackageVersion}");
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure)
                .ConfigureAwait(false);
            throw;
        }
    }

    internal async ValueTask<
        ConfiguredPackageSearchQueryResult<TResult>?> QuerySurfaceAsync<TResult>(
        Func<PackageSearchQueryContext, TResult> query,
        CancellationToken cancellationToken = default)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArtifactRootResult<ConfiguredPackageSearchQueryResult<TResult>>
            execution =
                await _workspace.ExecutePackageRootQueryAsync(
                    _correspondence,
                    _generation,
                    (realization, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (!realization.HasAssemblyContexts)
                        {
                            return ValueTask.FromResult(
                                new ConfiguredPackageSearchQueryResult<TResult>(
                                    Sources: null,
                                    Result: null));
                        }

                        var sources = new PackageSearchQuerySources(
                            realization.SurfaceParticipants);
                        var context = new PackageSearchQueryContext(
                            realization.SurfaceGroup,
                            sources);
                        TResult result = query(context);
                        token.ThrowIfCancellationRequested();
                        return ValueTask.FromResult(
                            new ConfiguredPackageSearchQueryResult<TResult>(
                                sources,
                                result));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (execution
            is ArtifactRootResult<
                ConfiguredPackageSearchQueryResult<TResult>>.Available available)
        {
            return available.Value;
        }

        var rejected =
            (ArtifactRootResult<
                ConfiguredPackageSearchQueryResult<TResult>>.Rejected)execution;
        CommandError.WriteWarning(
            $"Could not query package Root '{_packageDisplay}': "
            + rejected.Failure);
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        _closed = true;
        await CloseAsync(_workspace).ConfigureAwait(false);
    }

    static string Describe(WorkspaceScopeOperationResult result) =>
        result switch
        {
            WorkspaceScopeOperationResult.Rejected rejected =>
                $"Rejected: {rejected.Reason}",
            WorkspaceScopeOperationResult.Failed failed =>
                $"Failed: {failed.Failure}",
            WorkspaceScopeOperationResult.Cancelled =>
                "Cancelled",
            WorkspaceScopeOperationResult.Superseded =>
                "Superseded",
            WorkspaceScopeOperationResult.Unavailable unavailable =>
                $"Unavailable: {unavailable.RuntimeFailure}",
            WorkspaceScopeOperationResult.NoEffect =>
                "No effect",
            _ => throw new InvalidOperationException(
                "Unsupported Workspace Scope result."),
        };

    static async Task CloseAsync(InspectionWorkspace workspace)
    {
        InspectionWorkspaceCloseReport report =
            await workspace.CloseAsync().ConfigureAwait(false);
        if (!report.ArtifactSessionCleanupFailures.IsEmpty)
        {
            throw new AggregateException(
                report.ArtifactSessionCleanupFailures);
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
            {
                failure.Data[
                    "Inspector.Artifacts.Workspaces.CleanupFailures"] =
                    report.ArtifactSessionCleanupFailures;
            }
        }
        catch (Exception cleanupFailure)
        {
            failure.Data[
                "DotnetInspector.Queries.WorkspaceCleanupFailure"] =
                cleanupFailure;
        }
    }
}

internal sealed record ConfiguredPackageSearchQueryResult<TResult>(
    PackageSearchQuerySources? Sources,
    TResult? Result)
    where TResult : class;

internal sealed record PackageSearchQueryContext(
    AssemblyContextGroup Group,
    PackageSearchQuerySources Sources)
{
}

internal sealed class PackageSearchQuerySources
{
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        SearchAssemblySource> _sources;

    internal PackageSearchQuerySources(
        ImmutableArray<PackageAssemblyRoleParticipant> participants)
    {
        _sources = new(
            participants.Length,
            ReferenceEqualityComparer.Instance);
        foreach (PackageAssemblyRoleParticipant participant
            in participants)
        {
            _sources.Add(
                participant.Participant.Assembly.Registration,
                SearchAssemblySource.FromPackage(
                    participant.Package,
                    participant.Asset));
        }
    }

    internal int AssemblyCount => _sources.Count;

    internal SearchAssemblySource SourceFor(
        AssemblyContextSubject subject) =>
        _sources.TryGetValue(
            subject.Registration,
            out SearchAssemblySource? source)
            ? source
            : throw new InspectionQueryException(
                $"No committed package asset corresponds to "
                + $"'{subject.Identity.Name}'.");
}
