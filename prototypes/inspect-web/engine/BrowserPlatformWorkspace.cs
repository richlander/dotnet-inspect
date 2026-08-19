using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace InspectWeb.Engine;

/// <summary>
/// One cumulative Browser platform scope for a target framework.
/// </summary>
/// <remarks>
/// The scope contains only product-realized platform participants and exposes
/// them only through group-owned product queries. Its exact coordinates pin
/// the version and producer selected for each platform family.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed class BrowserPlatformScope(
    InspectionWorkspace workspace,
    WorkspaceContextLoadOutcome.Loaded context,
    IReadOnlyDictionary<string, string> platformPacks) : IDisposable
{
    readonly InspectionWorkspace _workspace = workspace;
    readonly ImmutableDictionary<string, string> _platformPacks =
        platformPacks.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
    WorkspaceContextLoadOutcome.Loaded? _context = context;

    internal WorkspaceContextLoadOutcome.Loaded Context =>
        _context
        ?? throw new ObjectDisposedException(nameof(BrowserPlatformScope));

    internal ImmutableArray<RealizedMemberCoordinate.Platform> Coordinates { get; } =
    [
        .. context.Members
            .Select(member => member.Realized)
            .OfType<RealizedMemberCoordinate.Platform>()
            .Distinct(),
    ];

    internal ImmutableArray<WorkspaceContextMember> Members =>
        Context.Members;

    internal string Framework =>
        Context.Framework
        ?? throw new InvalidOperationException(
            "A Browser platform scope has no effective target framework.");

    internal TResult Use<TResult>(
        Func<AssemblyContextGroup, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query(Context.Group);
    }

    internal TResult UseParticipant<TResult>(
        WorkspaceContextMember member,
        Func<AssemblyContextGroup, AssemblyContextParticipant, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(query);
        if (!Members.Any(candidate => ReferenceEquals(
                candidate.Participant.Assembly.Registration,
                member.Participant.Assembly.Registration)))
        {
            throw new ArgumentException(
                "The participant does not belong to this platform scope.",
                nameof(member));
        }

        return query(Context.Group, member.Participant);
    }

    internal WorkspaceContextMember Participant(
        string family,
        string assembly)
    {
        WorkspaceContextMember? member = Members.FirstOrDefault(candidate =>
            candidate.Realized is RealizedMemberCoordinate.Platform platform
            && platform.Family.Equals(family, StringComparison.Ordinal)
            && string.Equals(
                candidate.Participant.Assembly.Identity.Name,
                assembly,
                StringComparison.OrdinalIgnoreCase));
        return member
            ?? throw new InvalidOperationException(
                $"Platform family '{family}' assembly '{assembly}' is not resident in this workspace.");
    }

    internal string? PlatformPackForAssembly(string assembly) =>
        _platformPacks.GetValueOrDefault(assembly);

    public void Dispose()
    {
        if (_context is null)
            return;

        _context = null;
        _workspace.Dispose();
    }
}

internal sealed record BrowserPlatformScopeResolution(
    BrowserPlatformScope Scope,
    WorkspaceContextMember Participant,
    RealizedMemberCoordinate.Platform Coordinate);

/// <summary>
/// Browser adapter over <see cref="WorkspaceContextLoader"/> for lazily selected
/// runtime and ASP.NET Core implementation-pack assemblies.
/// </summary>
/// <remarks>
/// One state entry per target framework accumulates selected assemblies. The
/// first load of each family records the exact version and producer; later
/// assemblies are re-acquired from that coordinate, and every successful
/// expansion replaces the old group atomically. The shared package registry
/// accounts the retained archives and evicts this scope under the same
/// four-workspace bound as package scopes.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserPlatformWorkspace
{
    const string RuntimeFamily = "runtime";
    const string AspNetCoreFamily = "aspnetcore";
    const string RuntimePack = "netcore.app";
    const string AspNetCorePack = "aspnetcore.app";
    const string DefaultRuntimeAssembly = "System.Private.CoreLib";

    static readonly Dictionary<string, TargetState> Targets =
        new(StringComparer.Ordinal);
    static readonly Dictionary<string, Task> TargetTails =
        new(StringComparer.Ordinal);

    internal static Task<BrowserPlatformScopeResolution> OpenRuntimeAsync(
        string targetFramework,
        CancellationToken cancellationToken = default) =>
        OpenAsync(
            targetFramework,
            RuntimeFamily,
            DefaultRuntimeAssembly,
            ProductionHost,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenRuntimeAsync(
        string targetFramework,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        OpenAsync(
            targetFramework,
            RuntimeFamily,
            DefaultRuntimeAssembly,
            new Host(client, sourceAuthorization),
            operationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenAssemblyAsync(
        string targetFramework,
        string assemblyFileName,
        string pack,
        CancellationToken cancellationToken = default) =>
        OpenAsync(
            targetFramework,
            Family(pack),
            AssemblySimpleName(assemblyFileName),
            ProductionHost,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenAssemblyAsync(
        string targetFramework,
        string assemblyFileName,
        string pack,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        OpenAsync(
            targetFramework,
            Family(pack),
            AssemblySimpleName(assemblyFileName),
            new Host(client, sourceAuthorization),
            operationTimeout,
            cancellationToken);

    static Task<BrowserPlatformScopeResolution> OpenAsync(
        string targetFramework,
        string family,
        string assembly,
        Host host,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        string targetKey = TargetKey(targetFramework);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => EnqueueAsync(
                targetKey,
                () => OpenCoreAsync(
                    targetKey,
                    targetFramework,
                    family,
                    assembly,
                    host,
                    deadline),
                deadline.Token),
            operationTimeout,
            cancellationToken);
    }

    static async Task<BrowserPlatformScopeResolution> OpenCoreAsync(
        string targetKey,
        string targetFramework,
        string family,
        string assembly,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        deadline.Token.ThrowIfCancellationRequested();
        Targets.TryGetValue(targetKey, out TargetState? state);
        state ??= new TargetState();

        RealizedMemberCoordinate.Platform? requested =
            state.Coordinates.FirstOrDefault(candidate =>
                candidate.Family.Equals(family, StringComparison.Ordinal)
                && string.Equals(
                    candidate.Assembly,
                    assembly,
                    StringComparison.OrdinalIgnoreCase));
        if (requested is not null
            && state.Scope is { } retained
            && BrowserPackageWorkspace.IsScopeRetained(retained))
        {
            BrowserPackageWorkspace.TouchScope(retained);
            return new BrowserPlatformScopeResolution(
                retained,
                retained.Participant(family, assembly),
                requested);
        }

        ImmutableArray<RealizedMemberCoordinate.Platform> coordinates =
            state.Coordinates;
        BrowserPlatformScope? candidate = null;
        ImmutableHashSet<string> packageKeys = [];
        if (requested is null)
        {
            EnsureAssemblyCapacity(coordinates.Length + 1);
            RealizedMemberCoordinate.Platform? familyCoordinate =
                coordinates.FirstOrDefault(candidate =>
                    candidate.Family.Equals(family, StringComparison.Ordinal));
            if (familyCoordinate is null)
            {
                (BrowserPlatformScope Declared, ImmutableHashSet<string> PackageKeys) declared =
                    await LoadDeclaredAsync(
                        targetFramework,
                        family,
                        assembly,
                        host,
                        deadline).ConfigureAwait(false);
                try
                {
                    requested = AssertSingleCoordinate(
                        declared.Declared,
                        family,
                        assembly);
                }
                catch
                {
                    declared.Declared.Dispose();
                    throw;
                }
                coordinates = coordinates.Add(requested);
                if (state.Coordinates.IsEmpty)
                {
                    candidate = declared.Declared;
                    packageKeys = declared.PackageKeys;
                }
                else
                {
                    declared.Declared.Dispose();
                }
            }
            else
            {
                requested = new RealizedMemberCoordinate.Platform(
                    familyCoordinate.Family,
                    familyCoordinate.Version,
                    familyCoordinate.Producer,
                    familyCoordinate.Framework,
                    assembly);
                coordinates = coordinates.Add(requested);
            }
        }

        if (candidate is null)
        {
            (candidate, packageKeys) =
                await LoadRealizedAsync(
                    coordinates,
                    host,
                    deadline).ConfigureAwait(false);
        }

        string scopeKey = ScopeKey(coordinates);
        BrowserPlatformScope registered =
            BrowserPackageWorkspace.RegisterScope(
                scopeKey,
                candidate,
                packageKeys);
        BrowserPlatformScope? previous = state.Scope;
        state.Coordinates = coordinates;
        state.Scope = registered;
        Targets[targetKey] = state;
        if (previous is not null
            && !ReferenceEquals(previous, registered))
        {
            BrowserPackageWorkspace.RemoveScope(previous);
        }

        return new BrowserPlatformScopeResolution(
            registered,
            registered.Participant(family, assembly),
            requested);
    }

    static async Task<(
        BrowserPlatformScope Declared,
        ImmutableHashSet<string> PackageKeys)> LoadDeclaredAsync(
        string targetFramework,
        string family,
        string assembly,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        var workspace = new InspectionWorkspace();
        var store = new TrackingPackageStore();
        try
        {
            WorkspaceContextLoadOutcome outcome =
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = targetFramework,
                        Members =
                        [
                            WorkspaceMemberCoordinate.Platform(
                                family,
                                assembly),
                        ],
                    },
                    Options(store, host, deadline),
                    deadline.Token).ConfigureAwait(false);
            return (
                Scope(
                    workspace,
                    outcome,
                    store.PlatformPacks),
                store.PackageKeys);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    static async Task<(
        BrowserPlatformScope Scope,
        ImmutableHashSet<string> PackageKeys)> LoadRealizedAsync(
        ImmutableArray<RealizedMemberCoordinate.Platform> coordinates,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        var workspace = new InspectionWorkspace();
        var store = new TrackingPackageStore();
        try
        {
            WorkspaceContextLoadOutcome outcome =
                await WorkspaceContextLoader.LoadRealizedAsync(
                    workspace,
                    coordinates,
                    Options(store, host, deadline),
                    deadline.Token).ConfigureAwait(false);
            return (
                Scope(
                    workspace,
                    outcome,
                    store.PlatformPacks),
                store.PackageKeys);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    static WorkspaceContextLoadOptions Options(
        TrackingPackageStore store,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline) =>
        new()
        {
            HttpClient = host.Client,
            SourceAuthorization = host.SourceAuthorization,
            PackageStore = store,
            PackageTransferPolicy =
                new BrowserPackageWorkspace.BrowserPackageOperationTransferPolicy(
                    store,
                    deadline),
            PayloadLimits = BrowserPackageWorkspace.PackageLimits,
            MaxRetainedImageBytes =
                BrowserInspectionScope.MaxRetainedImageBytes,
            UseVersionCache = false,
        };

    static BrowserPlatformScope Scope(
        InspectionWorkspace workspace,
        WorkspaceContextLoadOutcome outcome,
        IReadOnlyDictionary<string, string> platformPacks) =>
        outcome switch
        {
            WorkspaceContextLoadOutcome.Loaded loaded =>
                new BrowserPlatformScope(
                    workspace,
                    loaded,
                    platformPacks),
            WorkspaceContextLoadOutcome.Failed failed =>
                throw Failure(failed),
            _ => throw new InvalidOperationException(
                "Platform workspace loading returned an unknown outcome."),
        };

    static InvalidOperationException Failure(
        WorkspaceContextLoadOutcome.Failed failed) =>
        new(
            string.Join(
                "; ",
                failed.Failures.Select(
                    failure => $"{failure.Kind}: {failure.Message}")));

    static RealizedMemberCoordinate.Platform AssertSingleCoordinate(
        BrowserPlatformScope scope,
        string family,
        string assembly)
    {
        RealizedMemberCoordinate.Platform[] coordinates =
        [
            .. scope.Coordinates.Where(candidate =>
                candidate.Family.Equals(family, StringComparison.Ordinal)
                && string.Equals(
                    candidate.Assembly,
                    assembly,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        return coordinates.Length == 1
            ? coordinates[0]
            : throw new InvalidOperationException(
                "Platform acquisition did not realize exactly the selected assembly coordinate.");
    }

    internal static async Task<T> EnqueueAsync<T>(
        string key,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        Task predecessor;
        var completion =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        lock (TargetTails)
        {
            predecessor = TargetTails.TryGetValue(key, out Task? pending)
                ? pending
                : Task.CompletedTask;
            TargetTails[key] = completion.Task;
        }

        bool completionDeferred = false;
        try
        {
            try
            {
                await predecessor.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                completionDeferred = true;
                _ = CompleteAfterPredecessorAsync(
                    key,
                    predecessor,
                    completion);
                throw;
            }
            catch
            {
            }

            return await operation().ConfigureAwait(false);
        }
        finally
        {
            if (!completionDeferred)
            {
                CompleteTail(
                    key,
                    completion);
            }
        }
    }

    static async Task CompleteAfterPredecessorAsync(
        string key,
        Task predecessor,
        TaskCompletionSource completion)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
        }
        catch
        {
        }

        CompleteTail(
            key,
            completion);
    }

    static void CompleteTail(
        string key,
        TaskCompletionSource completion)
    {
        completion.TrySetResult();
        lock (TargetTails)
        {
            if (TargetTails.TryGetValue(key, out Task? current)
                && ReferenceEquals(current, completion.Task))
            {
                TargetTails.Remove(key);
            }
        }
    }

    static string Family(string pack) =>
        pack switch
        {
            RuntimePack => RuntimeFamily,
            AspNetCorePack => AspNetCoreFamily,
            _ => throw new InvalidOperationException(
                $"Platform pack '{pack}' is not supported."),
        };

    internal static string Pack(string family) =>
        family switch
        {
            RuntimeFamily => RuntimePack,
            AspNetCoreFamily => AspNetCorePack,
            _ => throw new InvalidOperationException(
                $"Platform family '{family}' is not supported."),
        };

    static string AssemblySimpleName(string assemblyFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyFileName);
        if (!assemblyFileName.EndsWith(
                ".dll",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A platform assembly selection must be a DLL file name.");
        }

        string assembly = assemblyFileName[..^4];
        if (!RealizedMemberCoordinate.IsAssemblySimpleName(assembly))
        {
            throw new InvalidOperationException(
                "A platform assembly selection must contain one assembly simple name.");
        }

        return assembly;
    }

    static string TargetKey(string targetFramework) =>
        targetFramework.ToLowerInvariant();

    static string ScopeKey(
        ImmutableArray<RealizedMemberCoordinate.Platform> coordinates) =>
        "platform|" + string.Join(
            "|",
            coordinates
                .OrderBy(
                    coordinate => coordinate.Family,
                    StringComparer.Ordinal)
                .ThenBy(
                    coordinate => coordinate.Assembly,
                    StringComparer.Ordinal)
                .Select(coordinate =>
                    $"{coordinate.Family}@{coordinate.Version}"
                    + $"/{coordinate.Producer}/{coordinate.Framework}"
                    + $"#{coordinate.Assembly}"));

    internal static void EnsureAssemblyCapacity(int assemblyCount)
    {
        if (assemblyCount < 1
            || assemblyCount > BrowserInspectionScope.MaxAssembliesPerRole)
        {
            throw new InvalidOperationException(
                "The Browser platform workspace exceeds the assembly-count limit.");
        }
    }

    sealed class TargetState
    {
        internal ImmutableArray<RealizedMemberCoordinate.Platform> Coordinates
        {
            get;
            set;
        } = [];

        internal BrowserPlatformScope? Scope { get; set; }
    }

    static Host ProductionHost { get; } =
        new(
            BrowserPackageWorkspace.NetworkClient,
            BrowserPackageWorkspace.PackageSourceAuthorization);

    sealed record Host(
        HttpClient Client,
        IPackageSourceAuthorization SourceAuthorization);

    sealed class TrackingPackageStore
        : IPackageStore, IPackagePayloadTransferPolicy
    {
        readonly ImmutableHashSet<string>.Builder _packageKeys =
            ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        readonly HashSet<string> _recordedPackageKeys =
            new(StringComparer.Ordinal);
        readonly Dictionary<string, string> _platformPacks =
            new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _ambiguousAssemblies =
            new(StringComparer.OrdinalIgnoreCase);

        internal ImmutableHashSet<string> PackageKeys =>
            _packageKeys.ToImmutable();

        internal ImmutableDictionary<string, string> PlatformPacks =>
            _platformPacks.ToImmutableDictionary(
                StringComparer.OrdinalIgnoreCase);

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
        {
            IPackageContent? content =
                BrowserPackageWorkspace.SessionPackageStore.TryGetCached(
                    packageName,
                    version,
                    allowedSourceKeys,
                    log);
            if (content is not null)
            {
                string packageKey = BrowserPackageWorkspace.PackageKey(
                    packageName,
                    version);
                _packageKeys.Add(packageKey);
                RecordPlatformAssemblies(
                    packageKey,
                    packageName,
                    content);
            }

            return content;
        }

        public async ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
        {
            IPackageContent content =
                await BrowserPackageWorkspace.SessionPackageStore.CommitAsync(
                    packageName,
                    version,
                    sourceKey,
                    nupkg,
                    cancellationToken).ConfigureAwait(false);
            string packageKey = BrowserPackageWorkspace.PackageKey(
                packageName,
                version);
            _packageKeys.Add(packageKey);
            RecordPlatformAssemblies(
                packageKey,
                packageName,
                content);
            return content;
        }

        public IPackagePayloadReservation Reserve(
            PackagePayloadTransfer transfer) =>
            BrowserPackageWorkspace.PackageTransferPolicy.Reserve(transfer);

        void RecordPlatformAssemblies(
            string packageKey,
            string packageName,
            IPackageContent content)
        {
            if (!_recordedPackageKeys.Add(packageKey))
                return;

            string? pack = packageName.StartsWith(
                    "microsoft.netcore.app.runtime.",
                    StringComparison.OrdinalIgnoreCase)
                ? RuntimePack
                : packageName.StartsWith(
                    "microsoft.aspnetcore.app.runtime.",
                    StringComparison.OrdinalIgnoreCase)
                    ? AspNetCorePack
                    : null;
            if (pack is null)
                return;

            foreach (string path in content.EnumerateEntries())
            {
                if (!path.StartsWith(
                        "runtimes/",
                        StringComparison.OrdinalIgnoreCase)
                    || !path.Contains(
                        "/lib/",
                        StringComparison.OrdinalIgnoreCase)
                    || !path.EndsWith(
                        ".dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separator = path.LastIndexOf('/');
                string assembly = path[(separator + 1)..^4];
                if (!RealizedMemberCoordinate.IsAssemblySimpleName(assembly)
                    || _ambiguousAssemblies.Contains(assembly))
                {
                    continue;
                }

                if (_platformPacks.TryGetValue(
                        assembly,
                        out string? existing)
                    && !existing.Equals(
                        pack,
                        StringComparison.Ordinal))
                {
                    _platformPacks.Remove(assembly);
                    _ambiguousAssemblies.Add(assembly);
                    continue;
                }

                _platformPacks[assembly] = pack;
            }
        }
    }
}
