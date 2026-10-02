using System.Collections.Immutable;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// CLI-owned execution over one explicit package Root committed to an
/// invocation-scoped Workspace.
/// </summary>
internal sealed class ConfiguredPackageSearchWorkspace : IAsyncDisposable
{
    private const long MaxAssemblyImageBytes =
        512L * 1024 * 1024;
    private static readonly ApiSurfaceExtractionBounds
        NamespaceInspectionBounds = new(
            maxTypes: 500_000,
            maxMembers: 0,
            maxInspectionFailures: 10_000,
            maxTypeForwarders: 100_000,
            maxMetadataRows: int.MaxValue,
            maxRetainedTextCharacters: int.MaxValue);
    private static readonly AssemblyContextLibraryMaterializationLimits
        NamespaceMaterializationLimits = new(
            MaxAssemblyImageBytes,
            MaxAssemblyImageBytes);

    readonly InspectionWorkspace _workspace;
    readonly PackageArtifactRootCorrespondence _correspondence;
    readonly ArtifactRootGenerationReference _generation;
    readonly string _packageDisplay;
    readonly SearchPackageStores _stores;
    bool _closed;

    ConfiguredPackageSearchWorkspace(
        InspectionWorkspace workspace,
        PackageArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        string packageDisplay,
        SearchPackageStores stores)
    {
        _workspace = workspace;
        _correspondence = correspondence;
        _generation = generation;
        _packageDisplay = packageDisplay;
        _stores = stores;
    }

    internal static bool IsEligible(
        SearchSourceSelection? selection,
        AssemblySetRequest request,
        string? targetFramework,
        int? resultLimit = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (resultLimit is not null
            || string.IsNullOrWhiteSpace(targetFramework)
            || string.Equals(targetFramework, "all", StringComparison.OrdinalIgnoreCase)
            || selection is not
            {
                UsesImplicitPlatform: false,
                Frameworks.Count: 0,
                OtherSources.Count: 0,
                Packages: [SourceSelector.PackageReference package],
            }
            || request.Packages.Count != 1
            || request.Assemblies.Count != 0
            || request.PlatformAssemblies.Count != 0
            || request.PlatformFrameworks.Count != 0
            || request.Projects.Count != 0
            || request.Directories.Count != 0
            || package.Version is null)
        {
            return false;
        }

        return PackageCoordinateResolver.Validate(
            new PackageCoordinate(package.PackageId, package.Version)) is null;
    }

    internal static async ValueTask<ConfiguredPackageSearchWorkspace?>
        OpenAsync(
            HttpClient httpClient,
            AssemblySetRequest request,
            string targetFramework,
            Action<string>? log,
            CancellationToken cancellationToken = default,
            WorkspacePlan? workspacePlan = null)
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

        var workspace = new InspectionWorkspace(
            workspacePlan ?? WorkspacePlan.Empty);
        var stores = new SearchPackageStores();
        try
        {
            if (!InspectionGraphCommand.TryCreateMembers(
                    [packageSpec],
                    out WorkspaceMemberCoordinate[] members))
            {
                await CloseAsync(workspace, stores).ConfigureAwait(false);
                return null;
            }

            if (members is not [WorkspaceMemberCoordinate.PackageMember
                {
                    Version: not null,
                } member])
            {
                CommandError.WriteWarning(
                    $"Could not load package Root '{packageSpec}': "
                    + "configured package search requires an exact version.");
                await CloseAsync(workspace, stores).ConfigureAwait(false);
                return null;
            }

            (PackageRootBinding? acquired, PackagePayloadOrigin origin) =
                DotnetInspector.Networking.HttpClientFactory.IsOffline
                    ? await AcquireOfflineRootAsync(
                        httpClient,
                        members,
                        packageSpec,
                        request,
                        targetFramework,
                        log,
                        cancellationToken).ConfigureAwait(false)
                    : await AcquireRootAsync(
                        httpClient,
                        stores,
                        member,
                        request,
                        targetFramework,
                        log,
                        cancellationToken).ConfigureAwait(false);
            if (acquired is not { } binding)
            {
                await CloseAsync(workspace, stores).ConfigureAwait(false);
                return null;
            }
            WorkspaceScopeReadResult read =
                await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
            if (read is WorkspaceScopeReadResult.Unavailable unavailable)
            {
                CommandError.WriteWarning(
                    $"Could not open package Root '{packageSpec}': "
                    + unavailable.RuntimeFailure);
                await CloseAsync(workspace, stores).ConfigureAwait(false);
                return null;
            }

            WorkspaceScopeOperationResult admission =
                await workspace.AddPackagesAsync(
                    ((WorkspaceScopeReadResult.Available)read)
                        .Snapshot.Revision,
                    [binding],
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    cancellationToken).ConfigureAwait(false);
            if (admission
                is not WorkspaceScopeOperationResult.Committed committed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CommandError.WriteWarning(
                    $"Could not commit package Root '{packageSpec}': "
                    + Describe(admission));
                await CloseAsync(workspace, stores).ConfigureAwait(false);
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
                + $"({binding.Root.AssetSelection.Status}; payload {origin}).");
            return new(
                workspace,
                correspondence,
                ready.Generation,
                $"{binding.Root.PackageId}@{binding.Root.PackageVersion}",
                stores);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure)
                .ConfigureAwait(false);
            stores.Dispose();
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

    internal async ValueTask<
        InspectionEnvelope<PackageNamespaceDiscoveryOutcome>?>
        InspectNamespaceAsync(
            string @namespace,
            MetadataNamespaceMatch namespaceMatch,
            CancellationToken cancellationToken = default)
    {
        ArtifactRootResult<
            InspectionEnvelope<PackageNamespaceDiscoveryOutcome>> execution =
                await _workspace.ExecutePackageRootQueryAsync(
                    _correspondence,
                    _generation,
                    (realization, token) =>
                        PackageNamespaceDiscoveryInspection.ExecuteAsync(
                            realization,
                            new(
                                @namespace,
                                NamespaceInspectionBounds,
                                NamespaceMaterializationLimits,
                                namespaceMatch),
                            token),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (execution
            is ArtifactRootResult<
                InspectionEnvelope<
                    PackageNamespaceDiscoveryOutcome>>.Available available)
        {
            return available.Value;
        }

        var rejected =
            (ArtifactRootResult<
                InspectionEnvelope<
                    PackageNamespaceDiscoveryOutcome>>.Rejected)execution;
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
        await CloseAsync(_workspace, _stores).ConfigureAwait(false);
    }

    /// <summary>
    /// Offline, configured-authority acquisition is unavailable (as it is for
    /// the package command), so the Root comes from the local package cache
    /// through the workspace loader.
    /// </summary>
    static async ValueTask<(PackageRootBinding? Binding, PackagePayloadOrigin Origin)>
        AcquireOfflineRootAsync(
            HttpClient httpClient,
            WorkspaceMemberCoordinate[] members,
            string packageSpec,
            AssemblySetRequest request,
            string targetFramework,
            Action<string>? log,
            CancellationToken cancellationToken)
    {
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
                    UseVersionCache = false,
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
            return (null, default);
        }

        return (
            ((WorkspacePackageRootAcquisitionOutcome.Acquired)acquisition).Root,
            PackagePayloadOrigin.Cache);
    }

    /// <summary>
    /// Acquires the exact package through the House with ranged access: an
    /// authorized cache answers first, an uncached archive is read by range
    /// for exactly the compile realization's selection, and a source that
    /// cannot range falls back to the complete download. When the Root's own
    /// compatible selection reaches past what the ranged read materialized,
    /// the complete archive is acquired instead so the search stays whole.
    /// </summary>
    static async ValueTask<(PackageRootBinding? Binding, PackagePayloadOrigin Origin)>
        AcquireRootAsync(
            HttpClient httpClient,
            SearchPackageStores stores,
            WorkspaceMemberCoordinate.PackageMember member,
            AssemblySetRequest request,
            string targetFramework,
            Action<string>? log,
            CancellationToken cancellationToken)
    {
        PackageHouseTargetContext target;
        try
        {
            target = PackageHouseTargetContext.Exact(targetFramework);
        }
        catch (ArgumentException ex)
        {
            CommandError.WriteWarning(
                $"Could not load package Root '{member.PackageId}@{member.Version}': "
                + ex.Message);
            return (null, default);
        }

        await using var composition =
            new DesktopPackageSourceComposition(httpClient.Timeout);
        (PackageRootBinding? binding, AcquiredPackageSourcePayload? payload) =
            await AcquireAndBindAsync(
                composition,
                stores,
                member,
                request,
                target,
                targetFramework,
                log,
                PackagePayloadAccess.Ranged,
                cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return (null, default);
        if (payload.Content is RangedPackageContent ranged
            && !CoversSelection(binding!.Root.AssetSelection, ranged))
        {
            log?.Invoke(
                $"The ranged read of {member.PackageId}@{member.Version} did not "
                + "materialize the Root's compile selection; acquiring the complete archive.");
            (binding, payload) = await AcquireAndBindAsync(
                composition,
                stores,
                member,
                request,
                target,
                targetFramework,
                log,
                PackagePayloadAccess.Complete,
                cancellationToken).ConfigureAwait(false);
            if (payload is null)
                return (null, default);
        }

        return (binding, payload.Origin);
    }

    static async Task<(PackageRootBinding? Binding, AcquiredPackageSourcePayload? Payload)>
        AcquireAndBindAsync(
            DesktopPackageSourceComposition composition,
            SearchPackageStores stores,
            WorkspaceMemberCoordinate.PackageMember member,
            AssemblySetRequest request,
            PackageHouseTargetContext target,
            string targetFramework,
            Action<string>? log,
            PackagePayloadAccess access,
            CancellationToken cancellationToken)
    {
        ConfiguredPackagePayloadResult result =
            await composition.AcquirePinnedAsync(
                member.PackageId,
                member.Version!,
                stores.GetStore,
                request.SourceOptions,
                log,
                cancellationToken,
                limits: PackagePayloadLimits.Default,
                compileTargetContext: target,
                access: access,
                assetDemand: PackageAssetDemand.Surface).ConfigureAwait(false);
        if (result.Payload is not { } payload)
        {
            CommandError.WriteWarning(
                $"Could not load package Root '{member.PackageId}@{member.Version}': "
                + Describe(result));
            return (null, null);
        }

        PackageRootBinding binding =
            PackageRootBinding.CreateFromSourceWithCompatibleSelection(
                payload,
                targetFramework,
                member.PackageId);
        if (binding.Root.AssetSelection.Status
                == PackageCompileAssetSelectionStatus.NoMatchingTargetFramework
            && PackageAssetSelector.Select(payload.Content, targetFramework)
                is PackageAssetSelection.Selected compatible)
        {
            // The requested framework has no exact compile selection; the
            // compatible implementation universe is the selection target,
            // while the acquired coordinate keeps the requested framework.
            binding = PackageRootBinding.CreateFromSourceWithCompatibleSelection(
                payload,
                compatible.Universe.TargetFramework,
                member.PackageId);
        }
        // Search reads the public surface only: the Root admits no
        // implementation role, so nothing beyond the surface folder is read.
        return (binding.WithAssetDemand(PackageAssetDemand.Surface), payload);
    }

    static bool CoversSelection(
        PackageCompileAssetSelection selection,
        RangedPackageContent ranged) =>
        !selection.IsSelected
        || selection.Assets.All(asset => ranged.IsMaterialized(asset.Path));

    static string Describe(ConfiguredPackagePayloadResult result)
    {
        if (result.Failures.Count > 0)
        {
            return string.Join(
                "; ",
                result.Failures.Select(failure =>
                    $"{failure.Kind}: {failure.Message}"));
        }
        if (result.NotFoundAuthorities.Count > 0)
        {
            return "the package was not found on "
                + string.Join(
                    ", ",
                    result.NotFoundAuthorities.Select(authority =>
                        PackageSourceDisplay.ForDiagnostics(authority.Source)))
                + ".";
        }
        return "no configured package source supplied the package archive.";
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

    static async Task CloseAsync(
        InspectionWorkspace workspace,
        SearchPackageStores stores)
    {
        try
        {
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            if (!report.ArtifactSessionCleanupFailures.IsEmpty)
            {
                throw new AggregateException(
                    report.ArtifactSessionCleanupFailures);
            }
        }
        finally
        {
            // Temporary authority content stays readable until the
            // Workspace that admitted it has closed.
            stores.Dispose();
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

/// <summary>
/// One authority-scoped desktop store per configured authority for one
/// search or one package document export, and the temporary root that holds
/// authorities without a durable cache identity. The root is deleted when
/// the search or export closes.
/// </summary>
internal sealed class SearchPackageStores : IDisposable
{
    readonly Dictionary<ConfiguredPackageAuthority, IPackageStore> _stores =
        new(ReferenceEqualityComparer.Instance);
    string? _temporaryRoot;

    internal IPackageStore GetStore(
        ConfiguredPackageAuthority authority,
        PackageProducerIdentity producer)
    {
        if (!_stores.TryGetValue(authority, out IPackageStore? store))
        {
            store = new AuthorityScopedFileSystemPackageStore(
                authority,
                producer,
                () => _temporaryRoot ??=
                    Directory.CreateTempSubdirectory("inspect-search").FullName);
            _stores.Add(authority, store);
        }
        return store;
    }

    public void Dispose()
    {
        _stores.Clear();
        DotnetInspector.Packages.PackageExtractor.Cleanup(_temporaryRoot);
        _temporaryRoot = null;
    }
}
