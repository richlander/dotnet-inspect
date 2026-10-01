using System.Runtime.ExceptionServices;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal sealed class PlatformFindSearchWorkspace : IAsyncDisposable
{
    private readonly InspectionWorkspace _workspace;
    private readonly AssemblyContextGroup _group;
    private readonly Dictionary<
        AssemblyAcquisitionRegistration,
        SearchAssemblySource> _sources;
    private bool _disposed;

    private PlatformFindSearchWorkspace(
        InspectionWorkspace workspace,
        AssemblyContextGroup group,
        Dictionary<
            AssemblyAcquisitionRegistration,
            SearchAssemblySource> sources)
    {
        _workspace = workspace;
        _group = group;
        _sources = sources;
    }

    internal static async ValueTask<PlatformFindSearchWorkspace> OpenAsync(
        FindOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspacePlan plan = FindSourceCollector.CreateWorkspacePlan(options);
        IReadOnlyList<PlatformLibraryPopulationDeclaration> populations =
            GetPlatformPopulations(plan);
        string? dotnetRoot =
            PlatformTypeCatalogRouting.FindActiveDotnetRoot();
        var snapshots = new List<PlatformAssemblySnapshot>();
        foreach (PlatformLibraryPopulationDeclaration population
            in populations)
        {
            PlatformFamily family = population.Family;
            PlatformPopulationArtifactMaterializationOutcome realization =
                await PlatformTypeCatalogRouting.RealizePopulationAsync(
                        dotnetRoot,
                        population,
                        context,
                        options.SourceOptions ?? new NuGetSourceOptions(),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (realization
                is PlatformPopulationArtifactMaterializationOutcome.Terminal
                    terminal)
            {
                CliPlatformTypeCatalogOutcome.NotCompleted failure =
                    PlatformTypeCatalogRouting.HouseFailure(terminal);
                throw new InvalidOperationException(
                    $"The default platform Workspace could not realize "
                        + $"{DisplayFamily(family)} ({failure.Kind}).");
            }

            var completed =
                (PlatformPopulationArtifactMaterializationOutcome.Completed)
                    realization;
            Exception? snapshotFailure = null;
            try
            {
                SnapshotFocusAssemblies(
                    completed,
                    snapshots,
                    cancellationToken);
            }
            catch (Exception failure)
            {
                snapshotFailure = failure;
            }

            IReadOnlyList<PlatformHouseFailureKind> cleanupFailures =
                await PlatformTypeCatalogRouting.RetireAsync(completed)
                    .ConfigureAwait(false);
            if (cleanupFailures.Count != 0)
            {
                throw new InvalidOperationException(
                    $"The default platform Workspace could not retire "
                        + $"{DisplayFamily(family)} authorities "
                        + $"({string.Join(", ", cleanupFailures)}).",
                    snapshotFailure);
            }
            if (snapshotFailure is not null)
                ExceptionDispatchInfo.Capture(snapshotFailure).Throw();
        }

        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException(
                "The default platform Workspace realized no Focus assemblies.");
        }

        var workspace = new InspectionWorkspace(plan);
        AssemblyContextGroup? group = null;
        try
        {
            AssemblyContextParticipant[] participants =
            [
                .. snapshots.Select(
                    static snapshot =>
                        new AssemblyContextParticipant(
                            snapshot.Assembly,
                            NoResolverAssemblyBindingPolicy.Instance)),
            ];
            group =
                workspace.CreateAssemblyContextGroup(
                    participants,
                    new AssemblyContextGroupOptions
                    {
                        MaxRetainedImageBytes =
                            snapshots.Sum(
                                static snapshot =>
                                    snapshot.ContentLength),
                    });
            var sources = new Dictionary<
                AssemblyAcquisitionRegistration,
                SearchAssemblySource>();
            foreach (PlatformAssemblySnapshot snapshot in snapshots)
            {
                sources.Add(
                    snapshot.Assembly.Registration,
                    snapshot.Source);
            }
            return new(workspace, group, sources);
        }
        catch (Exception operationFailure)
        {
            try
            {
                await DisposeResourcesAsync(group, workspace)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    operationFailure,
                    cleanupFailure);
            }
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
            throw;
        }
    }

    internal static IReadOnlyList<PlatformLibraryPopulationDeclaration>
        GetPlatformPopulations(WorkspacePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var populations = new List<PlatformLibraryPopulationDeclaration>();
        var identities = new HashSet<PlatformLibraryPopulationDeclaration>();

        foreach (WorkspaceRegistration registration in plan.Registrations)
        {
            switch (registration)
            {
                case WorkspaceRegistration.PackagePrefix:
                    break;
                case WorkspaceRegistration.ExactLibrary:
                    throw new NotSupportedException(
                        "The implicit Find Workspace plan contains an exact "
                            + "Library population that Find does not yet "
                            + "realize.");
                case WorkspaceRegistration.Ecosystem ecosystem:
                    if (!ecosystem.Declaration.CorePackages.IsEmpty)
                    {
                        throw new NotSupportedException(
                            $"The implicit Find Workspace ecosystem "
                                + $"'{ecosystem.Declaration.Id.Value}' contains "
                                + "core package populations that Find does not "
                                + "yet realize.");
                    }

                    foreach (WorkspaceEcosystemPopulationDeclaration declared
                        in ecosystem.Declaration.Populations)
                    {
                        switch (declared)
                        {
                            case WorkspaceEcosystemPopulationDeclaration
                                .PackagePrefix:
                                break;
                            case WorkspaceEcosystemPopulationDeclaration
                                .Platform platform:
                                if (!identities.Add(platform.Population))
                                {
                                    throw new InvalidOperationException(
                                        "The implicit Find Workspace plan "
                                            + "declares the same Platform "
                                            + "population more than once.");
                                }

                                populations.Add(platform.Population);
                                break;
                            case WorkspaceEcosystemPopulationDeclaration
                                .ExactLibrary:
                                throw new NotSupportedException(
                                    $"The implicit Find Workspace ecosystem "
                                        + $"'{ecosystem.Declaration.Id.Value}' "
                                        + "contains an exact Library "
                                        + "population that Find does not yet "
                                        + "realize.");
                            default:
                                throw new InvalidOperationException(
                                    "Unknown Workspace ecosystem population "
                                        + "declaration.");
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Workspace registration declaration.");
            }
        }

        if (populations.Count == 0)
        {
            throw new InvalidOperationException(
                "The implicit Find Workspace plan does not declare any "
                    + "Platform populations.");
        }

        return populations;
    }

    internal bool RunTypeInventories(
        bool includeAll,
        Action<AssemblyContextEntry<AssemblyTypeInventory>> consume,
        Func<bool>? stop = null,
        Func<
            AssemblyContextSubject,
            AssemblyTypeInventoryEntry,
            bool>? stopAfterType = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(consume);
        return AssemblyContextTypeInventoryQuery.ExecuteEach(
            _group,
            includeAll,
            consume,
            stop,
            stopAfterType);
    }

    internal AssemblyContextResult<AssemblyMemberMatches> QueryMembers(
        IReadOnlyList<string> patterns,
        bool includeAll,
        int? limit)
    {
        ThrowIfDisposed();
        return AssemblyContextMemberMatchesQuery.Execute(
            _group,
            patterns,
            includeAll,
            limit);
    }

    internal AssemblyContextResult<AssemblyMemberMatches> QueryMemberWindow(
        IReadOnlyList<string> patterns,
        bool includeAll,
        MemberSearchWindow window)
    {
        ThrowIfDisposed();
        return AssemblyContextMemberMatchesQuery.ExecuteWindow(
            _group,
            patterns,
            window,
            includeAll);
    }

    internal bool QueryMembersEach(
        IReadOnlyList<string> patterns,
        bool includeAll,
        Action<AssemblyContextEntry<AssemblyMemberMatches>> consume,
        Func<bool> stop)
    {
        ThrowIfDisposed();
        return AssemblyContextMemberMatchesQuery.ExecuteEach(
            _group,
            patterns,
            includeAll,
            consume,
            stop);
    }

    internal SearchAssemblySource SourceFor(
        AssemblyContextSubject subject)
    {
        ThrowIfDisposed();
        return _sources.TryGetValue(
                subject.Registration,
                out SearchAssemblySource? source)
            ? source
            : throw new InvalidOperationException(
                "The platform Find query returned an unknown assembly "
                    + "participant.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await DisposeResourcesAsync(_group, _workspace)
            .ConfigureAwait(false);
    }

    private static async ValueTask DisposeResourcesAsync(
        AssemblyContextGroup? group,
        InspectionWorkspace workspace)
    {
        Exception? groupFailure = null;
        try
        {
            group?.Dispose();
        }
        catch (Exception failure)
        {
            groupFailure = failure;
        }

        Exception? workspaceFailure = null;
        try
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            workspaceFailure = failure;
        }

        if (groupFailure is not null && workspaceFailure is not null)
            throw new AggregateException(groupFailure, workspaceFailure);
        if (groupFailure is not null)
            ExceptionDispatchInfo.Capture(groupFailure).Throw();
        if (workspaceFailure is not null)
            ExceptionDispatchInfo.Capture(workspaceFailure).Throw();
    }

    private static void SnapshotFocusAssemblies(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed,
        ICollection<PlatformAssemblySnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        for (int index = 0;
            index < completed.Population.Value.Members.Count;
            index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlatformPopulationMember member =
                completed.Population.Value.Members[index];
            if (member.Role != PlatformPopulationMemberRole.Focus)
                continue;

            LibraryReference library =
                member.PlatformLibrary.Library;
            LibraryContentOwner owner =
                completed.Population.Owners[index];
            if (owner.IssueOperationLease(library)
                is not LibraryOperationLeaseIssueOutcome.Issued issued)
            {
                throw new ObjectDisposedException(
                    nameof(LibraryContentOwner),
                    "The PlatformHouse Library owner could not issue a "
                        + "Find snapshot lease.");
            }

            using LibraryOperationLease lease = issued.Lease;
            PlatformAssemblySnapshot snapshot =
                lease.Snapshot(
                    library.ApiAssembly,
                    member,
                    static (view, state, _) =>
                        CreateAssemblySnapshot(view, state),
                    cancellationToken);
            snapshots.Add(snapshot);
        }
    }

    private static PlatformAssemblySnapshot CreateAssemblySnapshot(
        scoped LibraryContentView view,
        PlatformPopulationMember member)
    {
        byte[] content = view.Content.ToArray();
        string framework = member.Target.Family switch
        {
            PlatformFamily.DotNetRuntime =>
                "Microsoft.NETCore.App",
            PlatformFamily.AspNetCore =>
                "Microsoft.AspNetCore.App",
            _ => throw new InvalidOperationException(
                $"Unsupported Platform family '{member.Target.Family}'."),
        };
        string producer =
            view.Reference.Provenance
                is PlatformLibraryArtifactProvenance provenance
                    ? provenance.Contribution.Capability.Name
                    : "PlatformHouse";
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                view.Reference.Registration,
                () => new MemoryStream(content, writable: false),
                AssemblyResolutionProvenance.Platform(
                    framework,
                    member.Target.Version.Value,
                    producer))
            ?? throw new BadImageFormatException(
                "The Platform reference population contains a non-managed "
                    + "Library.");
        if (view.Reference.AssemblyIdentity is not { } expected
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                assembly.Identity,
                expected.Identity))
        {
            throw new BadImageFormatException(
                "Metadata decoded an identity different from the Platform "
                    + "population member.");
        }

        return new(
            assembly,
            SearchAssemblySource.FromPlatformPopulation(
                member,
                assembly),
            content.LongLength);
    }

    private static string DisplayFamily(PlatformFamily family) =>
        family switch
        {
            PlatformFamily.DotNetRuntime => "Runtime",
            PlatformFamily.AspNetCore => "ASP.NET Core",
            _ => family.ToString(),
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record PlatformAssemblySnapshot(
        ResolvedAssemblyReference Assembly,
        SearchAssemblySource Source,
        long ContentLength);
}
