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
    IReadOnlyDictionary<string, string> platformPacks) : IAsyncDisposable
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

    public ValueTask DisposeAsync()
    {
        if (_context is null)
            return ValueTask.CompletedTask;

        _context = null;
        _workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

[SupportedOSPlatform("browser")]
internal sealed record BrowserPlatformScopeResolution(
    BrowserPlatformScope Scope,
    WorkspaceContextMember Participant,
    RealizedMemberCoordinate.Platform Coordinate,
    BrowserScopeLease<BrowserPlatformScope> ScopeLease) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => ScopeLease.DisposeAsync();
}

internal sealed record BrowserPlatformAssemblyRequest(
    string AssemblyFileName,
    string Pack);

/// <summary>
/// Browser adapter over <see cref="WorkspaceContextLoader"/> for lazily selected
/// runtime and ASP.NET Core implementation-pack assemblies.
/// </summary>
/// <remarks>
/// One state entry per target framework accumulates selected assemblies. The
/// first load of each family records the exact version and producer; later
/// assemblies are re-acquired from that coordinate, and every successful
/// expansion replaces the old group atomically. Each queued operation and
/// returned resolution pins its scope until disposed; replacement defers
/// old-scope disposal until every in-flight operation releases that pin. The
/// shared package registry accounts the retained archives and evicts this scope
/// under the same four-workspace bound as package scopes.
/// <c>BrowserEngineBoundaryTests.PlatformWorkspace_ReplacementDefersDisposalUntilLastLeaseEnds</c>
/// gates the replacement lifetime.
/// <c>BrowserEngineBoundaryTests.PlatformWorkspace_UnknownFamilyProbePinsCumulativeState</c>
/// and
/// <c>BrowserEngineBoundaryTests.PlatformWorkspace_FailedUnknownFamilyProbePreservesCumulativeState</c>
/// gate cumulative state across probe suspension, scope pressure, and failure.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserPlatformWorkspace
{
    const string RuntimeFamily = "runtime";
    const string AspNetCoreFamily = "aspnetcore";
    const string RuntimePack = "netcore.app";
    const string AspNetCorePack = "aspnetcore.app";
    const string DefaultRuntimeAssembly = "System.Private.CoreLib";
    const int MaxRetainedTargets = BrowserPackageWorkspace.MaxOpenScopes;

    static readonly Dictionary<string, TargetState> Targets =
        new(StringComparer.Ordinal);
    static readonly Dictionary<string, Task> TargetTails =
        new(StringComparer.Ordinal);
    static long _targetClock;

    internal static Task<BrowserPlatformScopeResolution> OpenRuntimeAsync(
        string targetFramework,
        CancellationToken cancellationToken = default) =>
        OpenRuntimeAsync(
            targetFramework,
            platformVersion: null,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenRuntimeAsync(
        string targetFramework,
        string? platformVersion,
        CancellationToken cancellationToken = default) =>
        OpenAsync(
            targetFramework,
            platformVersion,
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
        OpenRuntimeAsync(
            targetFramework,
            platformVersion: null,
            client,
            sourceAuthorization,
            operationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenRuntimeAsync(
        string targetFramework,
        string? platformVersion,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        OpenAsync(
            targetFramework,
            platformVersion,
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
        OpenAssemblyAsync(
            targetFramework,
            platformVersion: null,
            assemblyFileName,
            pack,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenAssemblyAsync(
        string targetFramework,
        string? platformVersion,
        string assemblyFileName,
        string pack,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(pack)
            ? OpenUnattributedAsync(
                targetFramework,
                platformVersion,
                AssemblySimpleName(assemblyFileName),
                ProductionHost,
                BrowserPackageWorkspace.PackageOperationTimeout,
                cancellationToken)
            : OpenAsync(
                targetFramework,
                platformVersion,
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
        OpenAssemblyAsync(
            targetFramework,
            platformVersion: null,
            assemblyFileName,
            pack,
            client,
            sourceAuthorization,
            operationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenAssemblyAsync(
        string targetFramework,
        string? platformVersion,
        string assemblyFileName,
        string pack,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(pack)
            ? OpenUnattributedAsync(
                targetFramework,
                platformVersion,
                AssemblySimpleName(assemblyFileName),
                new Host(client, sourceAuthorization),
                operationTimeout,
                cancellationToken)
            : OpenAsync(
                targetFramework,
                platformVersion,
                Family(pack),
                AssemblySimpleName(assemblyFileName),
                new Host(client, sourceAuthorization),
                operationTimeout,
                cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenAssembliesAsync(
        string targetFramework,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        CancellationToken cancellationToken = default) =>
        OpenAssembliesAsync(
            targetFramework,
            platformVersion: null,
            assemblies,
            ProductionHost,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenAssembliesAsync(
        string targetFramework,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        OpenAssembliesAsync(
            targetFramework,
            platformVersion: null,
            assemblies,
            new Host(client, sourceAuthorization),
            operationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenAssembliesAsync(
        string targetFramework,
        string? platformVersion,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        CancellationToken cancellationToken = default) =>
        OpenAssembliesAsync(
            targetFramework,
            platformVersion,
            assemblies,
            ProductionHost,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    static Task<BrowserPlatformScopeResolution> OpenAsync(
        string targetFramework,
        string? platformVersion,
        string family,
        string assembly,
        Host host,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
        => OpenAsync(
            targetFramework,
            platformVersion,
            [new PlatformSelection(family, assembly)],
            host,
            operationTimeout,
            cancellationToken);

    static Task<BrowserPlatformScopeResolution> OpenAssembliesAsync(
        string targetFramework,
        string? platformVersion,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        Host host,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Count == 0)
        {
            throw new ArgumentException(
                "A Platform workspace expansion requires at least one assembly.",
                nameof(assemblies));
        }

        var selections = ImmutableArray.CreateBuilder<PlatformSelection>();
        foreach (BrowserPlatformAssemblyRequest request in assemblies)
        {
            ArgumentNullException.ThrowIfNull(request);
            var selection = new PlatformSelection(
                Family(request.Pack),
                AssemblySimpleName(request.AssemblyFileName));
            PlatformSelection[] otherFamilies =
            [
                .. selections.Where(candidate =>
                    !candidate.Family.Equals(
                        selection.Family,
                        StringComparison.Ordinal)
                    && candidate.Assembly.Equals(
                        selection.Assembly,
                        StringComparison.OrdinalIgnoreCase))
                    .Take(1),
            ];
            if (otherFamilies.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Platform assembly '{selection.Assembly}' cannot be "
                    + $"selected from both '{otherFamilies[0].Family}' and "
                    + $"'{selection.Family}'.");
            }

            if (!selections.Any(candidate =>
                    candidate.Family.Equals(
                        selection.Family,
                        StringComparison.Ordinal)
                    && candidate.Assembly.Equals(
                        selection.Assembly,
                        StringComparison.OrdinalIgnoreCase)))
            {
                selections.Add(selection);
            }
        }

        return OpenAsync(
            targetFramework,
            platformVersion,
            selections.ToImmutable(),
            host,
            operationTimeout,
            cancellationToken);
    }

    static Task<BrowserPlatformScopeResolution> OpenAsync(
        string targetFramework,
        string? platformVersion,
        ImmutableArray<PlatformSelection> selections,
        Host host,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        platformVersion = NormalizeOptionalVersion(platformVersion);
        string targetKey = TargetKey(targetFramework, platformVersion);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => EnqueueAsync(
                targetKey,
                () => OpenCoreAsync(
                    targetKey,
                    targetFramework,
                    platformVersion,
                    selections,
                    host,
                    deadline),
                deadline.Token),
            operationTimeout,
            cancellationToken);
    }

    static Task<BrowserPlatformScopeResolution> OpenUnattributedAsync(
        string targetFramework,
        string? platformVersion,
        string assembly,
        Host host,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        platformVersion = NormalizeOptionalVersion(platformVersion);
        string targetKey = TargetKey(targetFramework, platformVersion);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => EnqueueAsync(
                targetKey,
                () => OpenUnattributedCoreAsync(
                    targetKey,
                    targetFramework,
                    platformVersion,
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
        string? platformVersion,
        ImmutableArray<PlatformSelection> selections,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        deadline.Token.ThrowIfCancellationRequested();
        using var packageLeases =
            new BrowserPackageWorkspace.PackageLeaseSet();
        Targets.TryGetValue(targetKey, out TargetState? state);
        state ??= new TargetState();
        await using BrowserScopeLease<BrowserPlatformScope>? retainedLease =
            LeaseRetainedScope(state);
        return await OpenCoreAsync(
            targetKey,
            targetFramework,
            platformVersion,
            selections,
            host,
            deadline,
            packageLeases,
            declaration: null,
            state: state).ConfigureAwait(false);
    }

    static async Task<BrowserPlatformScopeResolution> OpenUnattributedCoreAsync(
        string targetKey,
        string targetFramework,
        string? platformVersion,
        string assembly,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        deadline.Token.ThrowIfCancellationRequested();
        using var packageLeases =
            new BrowserPackageWorkspace.PackageLeaseSet();
        Targets.TryGetValue(targetKey, out TargetState? state);
        state ??= new TargetState();
        await using BrowserScopeLease<BrowserPlatformScope>? retainedLease =
            LeaseRetainedScope(state);

        RealizedMemberCoordinate.Platform[] known =
        [
            .. state.Coordinates.Where(coordinate =>
                string.Equals(
                    coordinate.Assembly,
                    assembly,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (known.Length > 1)
        {
            throw new InvalidOperationException(
                $"Platform assembly '{assembly}' belongs to more than one supported platform family.");
        }
        if (known.Length == 1)
        {
            return await OpenCoreAsync(
                targetKey,
                targetFramework,
                platformVersion,
                [new PlatformSelection(known[0].Family, assembly)],
                host,
                deadline,
                packageLeases,
                declaration: null,
                state: state).ConfigureAwait(false);
        }

        string? residentPack = state.Scope is { } retained
            && BrowserPackageWorkspace.IsScopeRetained(retained)
                ? retained.PlatformPackForAssembly(assembly)
                : null;
        if (residentPack is not null)
        {
            return await OpenCoreAsync(
                targetKey,
                targetFramework,
                platformVersion,
                [new PlatformSelection(Family(residentPack), assembly)],
                host,
                deadline,
                packageLeases,
                declaration: null,
                state: state).ConfigureAwait(false);
        }

        await using ScopeReservation reservation =
            await BrowserPackageWorkspace.ReserveScopeAsync(deadline.Token)
                .ConfigureAwait(false);
        var runtime = await ProbeFamilyAsync(
            state,
            targetFramework,
            platformVersion,
            RuntimeFamily,
            assembly,
            host,
            deadline,
            packageLeases).ConfigureAwait(false);
        if (runtime.Failure is not null
            && !IsAssemblyUnavailable(runtime.Failure))
        {
            throw Failure(runtime.Failure);
        }

        var aspNetCore = await ProbeFamilyAsync(
            state,
            targetFramework,
            platformVersion,
            AspNetCoreFamily,
            assembly,
            host,
            deadline,
            packageLeases).ConfigureAwait(false);
        if (aspNetCore.Failure is not null
            && !IsAssemblyUnavailable(aspNetCore.Failure))
        {
            throw Failure(aspNetCore.Failure);
        }

        if (runtime.Coordinate is not null && aspNetCore.Coordinate is not null)
        {
            throw new InvalidOperationException(
                $"Platform assembly '{assembly}' belongs to more than one supported platform family.");
        }

        RealizedMemberCoordinate.Platform selected =
            runtime.Coordinate ?? aspNetCore.Coordinate
            ?? throw new InvalidOperationException(
                $"Platform assembly '{assembly}' is not carried by any supported platform family. "
                + $"{FailureMessage(runtime.Failure!)}; "
                + FailureMessage(aspNetCore.Failure!));

        return await OpenCoreAsync(
            targetKey,
            targetFramework,
            platformVersion,
            [new PlatformSelection(selected.Family, assembly)],
            host,
            deadline,
            packageLeases,
            declaration: selected,
            state: state,
            reservation: reservation).ConfigureAwait(false);
    }

    static BrowserScopeLease<BrowserPlatformScope>? LeaseRetainedScope(
        TargetState state) =>
        state.Scope is { } retained
        && BrowserPackageWorkspace.IsScopeRetained(retained)
            ? BrowserPackageWorkspace.LeaseScope(retained)
            : null;

    static async Task<BrowserPlatformScopeResolution> OpenCoreAsync(
        string targetKey,
        string targetFramework,
        string? platformVersion,
        ImmutableArray<PlatformSelection> selections,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
        BrowserPackageWorkspace.PackageLeaseSet packageLeases,
        RealizedMemberCoordinate.Platform? declaration,
        TargetState state,
        ScopeReservation? reservation = null)
    {
        deadline.Token.ThrowIfCancellationRequested();
        state.LastAccess = ++_targetClock;

        foreach (PlatformSelection selection in selections)
        {
            RealizedMemberCoordinate.Platform? otherFamily =
                state.Coordinates.FirstOrDefault(candidate =>
                    !candidate.Family.Equals(
                        selection.Family,
                        StringComparison.Ordinal)
                    && string.Equals(
                        candidate.Assembly,
                        selection.Assembly,
                        StringComparison.OrdinalIgnoreCase));
            if (otherFamily is not null)
            {
                throw new InvalidOperationException(
                    $"Platform assembly '{selection.Assembly}' is already "
                    + $"selected from family '{otherFamily.Family}' and "
                    + $"cannot also be selected from '{selection.Family}'.");
            }
        }

        PlatformSelection selected = selections[^1];
        RealizedMemberCoordinate.Platform? requested =
            state.Coordinates.FirstOrDefault(candidate =>
                candidate.Family.Equals(
                    selected.Family,
                    StringComparison.Ordinal)
                && string.Equals(
                    candidate.Assembly,
                    selected.Assembly,
                    StringComparison.OrdinalIgnoreCase));
        bool allRequested = selections.All(selection =>
            state.Coordinates.Any(candidate =>
                candidate.Family.Equals(
                    selection.Family,
                    StringComparison.Ordinal)
                && string.Equals(
                    candidate.Assembly,
                    selection.Assembly,
                    StringComparison.OrdinalIgnoreCase)));
        if (allRequested
            && requested is not null
            && state.Scope is { } retained
            && BrowserPackageWorkspace.IsScopeRetained(retained))
        {
            BrowserPackageWorkspace.TouchScope(retained);
            WorkspaceContextMember retainedParticipant =
                retained.Participant(
                    selected.Family,
                    selected.Assembly);
            BrowserScopeLease<BrowserPlatformScope> retainedLease =
                BrowserPackageWorkspace.LeaseScope(retained);
            return new BrowserPlatformScopeResolution(
                retained,
                retainedParticipant,
                requested,
                retainedLease);
        }

        // The counted workspace entry — and with it the full image allowance — is reserved before
        // any platform image is loaded, so a platform load under construction counts against the
        // same bound as a ready workspace.
        await using ScopeReservation candidateReservation =
            reservation ?? await BrowserPackageWorkspace.ReserveScopeAsync(deadline.Token)
                .ConfigureAwait(false);
        ImmutableArray<RealizedMemberCoordinate.Platform> coordinates =
            state.Coordinates;
        BrowserPlatformScope? candidate = null;
        ImmutableHashSet<string> packageKeys = [];
        foreach (PlatformSelection selection in selections)
        {
            RealizedMemberCoordinate.Platform? otherFamily =
                coordinates.FirstOrDefault(candidate =>
                    !candidate.Family.Equals(
                        selection.Family,
                        StringComparison.Ordinal)
                    && string.Equals(
                        candidate.Assembly,
                        selection.Assembly,
                        StringComparison.OrdinalIgnoreCase));
            if (otherFamily is not null)
            {
                throw new InvalidOperationException(
                    $"Platform assembly '{selection.Assembly}' is already "
                    + $"selected from family '{otherFamily.Family}' and "
                    + $"cannot also be selected from '{selection.Family}'.");
            }

            if (coordinates.Any(candidate =>
                    candidate.Family.Equals(
                        selection.Family,
                        StringComparison.Ordinal)
                    && string.Equals(
                        candidate.Assembly,
                        selection.Assembly,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            EnsureAssemblyCapacity(coordinates.Length + 1);
            RealizedMemberCoordinate.Platform? familyCoordinate =
                coordinates.FirstOrDefault(candidate =>
                    candidate.Family.Equals(
                        selection.Family,
                        StringComparison.Ordinal));
            if (familyCoordinate is null)
            {
                if (declaration is { } discovered
                    && discovered.Family.Equals(
                        selection.Family,
                        StringComparison.Ordinal)
                    && string.Equals(
                        discovered.Assembly,
                        selection.Assembly,
                        StringComparison.OrdinalIgnoreCase))
                {
                    coordinates = coordinates.Add(discovered);
                    continue;
                }

                await using PlatformLoadAttempt declared =
                    await LoadDeclaredAttemptAsync(
                        targetFramework,
                        platformVersion,
                        selection.Family,
                        selection.Assembly,
                        host,
                        deadline,
                        packageLeases).ConfigureAwait(false);
                if (declared.Failure is not null)
                    throw Failure(declared.Failure);
                RealizedMemberCoordinate.Platform realized =
                    AssertSingleCoordinate(
                        declared.Scope!,
                        selection.Family,
                        selection.Assembly);
                coordinates = coordinates.Add(realized);
                if (state.Coordinates.IsEmpty
                    && selections.Length == 1)
                {
                    candidate = declared.ReleaseScope();
                    packageKeys = declared.PackageKeys;
                }
            }
            else
            {
                coordinates = coordinates.Add(
                    new RealizedMemberCoordinate.Platform(
                    familyCoordinate.Family,
                    familyCoordinate.Version,
                    familyCoordinate.Producer,
                    familyCoordinate.Framework,
                    selection.Assembly));
            }
        }

        if (candidate is null)
        {
            (candidate, packageKeys) =
                await LoadRealizedAsync(
                    coordinates,
                    host,
                    deadline,
                    packageLeases).ConfigureAwait(false);
        }

        requested = coordinates.Single(candidate =>
            candidate.Family.Equals(
                selected.Family,
                StringComparison.Ordinal)
            && string.Equals(
                candidate.Assembly,
                selected.Assembly,
                StringComparison.OrdinalIgnoreCase));
        string scopeKey = ScopeKey(coordinates);
        BrowserScopeLease<BrowserPlatformScope> lease =
            await BrowserPackageWorkspace.RegisterScopeAsync(
                    candidateReservation,
                    scopeKey,
                    candidate,
                    packageKeys,
                    ForgetScope)
                .ConfigureAwait(false);
        BrowserPlatformScope registered = lease.Scope;
        WorkspaceContextMember participant =
            registered.Participant(
                selected.Family,
                selected.Assembly);
        BrowserPlatformScope? previous = state.Scope;
        state.Coordinates = coordinates;
        state.Scope = registered;
        Targets[targetKey] = state;
        TrimTargetStates();
        if (previous is not null
            && !ReferenceEquals(previous, registered))
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(previous)
                .ConfigureAwait(false);
        }

        return new BrowserPlatformScopeResolution(
            registered,
            participant,
            requested,
            lease);
    }

    static async Task<(
        RealizedMemberCoordinate.Platform? Coordinate,
        WorkspaceContextLoadOutcome.Failed? Failure)> ProbeFamilyAsync(
        TargetState state,
        string targetFramework,
        string? platformVersion,
        string family,
        string assembly,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
        BrowserPackageWorkspace.PackageLeaseSet packageLeases)
    {
        RealizedMemberCoordinate.Platform? pinned =
            state.Coordinates.FirstOrDefault(coordinate =>
                coordinate.Family.Equals(family, StringComparison.Ordinal));
        // Only the realized coordinate survives a probe. Its images close before the next
        // probe or final realization reuses the operation's single counted reservation.
        await using PlatformLoadAttempt attempt = pinned is null
            ? await LoadDeclaredAttemptAsync(
                targetFramework,
                platformVersion,
                family,
                assembly,
                host,
                deadline,
                packageLeases).ConfigureAwait(false)
            : await LoadRealizedAttemptAsync(
                [
                    new RealizedMemberCoordinate.Platform(
                        pinned.Family,
                        pinned.Version,
                        pinned.Producer,
                        pinned.Framework,
                        assembly),
                ],
                host,
                deadline,
                packageLeases).ConfigureAwait(false);
        return (
            attempt.Scope is { } scope
                ? AssertSingleCoordinate(scope, family, assembly)
                : null,
            attempt.Failure);
    }

    static async Task<PlatformLoadAttempt> LoadDeclaredAttemptAsync(
        string targetFramework,
        string? platformVersion,
        string family,
        string assembly,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
        BrowserPackageWorkspace.PackageLeaseSet packageLeases)
    {
        var workspace = new InspectionWorkspace();
        var store = new TrackingPackageStore(packageLeases);
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
                                assembly,
                                platformVersion),
                        ],
                    },
                    Options(store, host, deadline),
                    deadline.Token).ConfigureAwait(false);
            return Attempt(
                workspace,
                outcome,
                store.PackageKeys);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    static async Task<PlatformLoadAttempt> LoadRealizedAttemptAsync(
        ImmutableArray<RealizedMemberCoordinate.Platform> coordinates,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
        BrowserPackageWorkspace.PackageLeaseSet packageLeases)
    {
        var workspace = new InspectionWorkspace();
        var store = new TrackingPackageStore(packageLeases);
        try
        {
            WorkspaceContextLoadOutcome outcome =
                await WorkspaceContextLoader.LoadRealizedAsync(
                    workspace,
                    coordinates,
                    Options(store, host, deadline),
                    deadline.Token).ConfigureAwait(false);
            return Attempt(
                workspace,
                outcome,
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
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
        BrowserPackageWorkspace.PackageLeaseSet packageLeases)
    {
        var workspace = new InspectionWorkspace();
        var store = new TrackingPackageStore(packageLeases);
        try
        {
            WorkspaceContextLoadOutcome outcome =
                await WorkspaceContextLoader.LoadRealizedAsync(
                    workspace,
                    coordinates,
                    Options(store, host, deadline),
                    deadline.Token).ConfigureAwait(false);
            PlatformLoadAttempt attempt = Attempt(
                workspace,
                outcome,
                store.PackageKeys);
            return attempt.Failure is null
                ? (attempt.Scope!, attempt.PackageKeys)
                : throw Failure(attempt.Failure);
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

    static PlatformLoadAttempt Attempt(
        InspectionWorkspace workspace,
        WorkspaceContextLoadOutcome outcome,
        ImmutableHashSet<string> packageKeys)
    {
        if (outcome is WorkspaceContextLoadOutcome.Loaded loaded)
        {
            return new PlatformLoadAttempt(
                Scope(workspace, loaded),
                packageKeys,
                failure: null);
        }

        workspace.Dispose();
        return outcome is WorkspaceContextLoadOutcome.Failed failed
            ? new PlatformLoadAttempt(
                scope: null,
                packageKeys,
                failed)
            : throw new InvalidOperationException(
                "Platform workspace loading returned an unknown outcome.");
    }

    static BrowserPlatformScope Scope(
        InspectionWorkspace workspace,
        WorkspaceContextLoadOutcome.Loaded loaded)
    {
        EnsureAssemblyCapacity(loaded.Members.Length);
        return new BrowserPlatformScope(
            workspace,
            loaded,
            PlatformPacks(loaded.AvailablePlatformAssemblies));
    }

    static ImmutableDictionary<string, string> PlatformPacks(
        ImmutableArray<RealizedMemberCoordinate.Platform> assemblies)
    {
        var platformPacks =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        var ambiguousAssemblies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RealizedMemberCoordinate.Platform assembly in assemblies)
        {
            if (assembly.Assembly is null
                || ambiguousAssemblies.Contains(assembly.Assembly))
            {
                continue;
            }

            string pack = assembly.Family switch
            {
                RuntimeFamily => RuntimePack,
                AspNetCoreFamily => AspNetCorePack,
                _ => throw new InvalidOperationException(
                    "The workspace loader returned an unknown platform family."),
            };
            if (platformPacks.TryGetValue(
                    assembly.Assembly,
                    out string? existing)
                && !existing.Equals(pack, StringComparison.Ordinal))
            {
                platformPacks.Remove(assembly.Assembly);
                ambiguousAssemblies.Add(assembly.Assembly);
                continue;
            }

            platformPacks[assembly.Assembly] = pack;
        }

        return platformPacks.ToImmutableDictionary(
            StringComparer.OrdinalIgnoreCase);
    }

    static InvalidOperationException Failure(
        WorkspaceContextLoadOutcome.Failed failed) =>
        new(FailureMessage(failed));

    static string FailureMessage(
        WorkspaceContextLoadOutcome.Failed failed) =>
        string.Join(
            "; ",
            failed.Failures.Select(
                failure => $"{failure.Kind}: {failure.Message}"));

    static bool IsAssemblyUnavailable(
        WorkspaceContextLoadOutcome.Failed failed) =>
        !failed.Failures.IsEmpty
        && failed.Failures.All(failure =>
            failure.Kind
                is WorkspaceContextLoadFailureKind.PlatformAssemblyUnavailable);

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

    static string? NormalizeOptionalVersion(string? platformVersion) =>
        string.IsNullOrEmpty(platformVersion)
        || platformVersion.Equals(
            "latest",
            StringComparison.OrdinalIgnoreCase)
            ? null
            : platformVersion;

    static string TargetKey(
        string targetFramework,
        string? platformVersion) =>
        $"{targetFramework.ToLowerInvariant()}@"
        + (platformVersion?.ToLowerInvariant() ?? "latest");

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

    static void ForgetScope(BrowserPlatformScope scope)
    {
        TargetState? state = Targets.Values.FirstOrDefault(
            candidate => ReferenceEquals(candidate.Scope, scope));
        if (state is not null)
            state.Scope = null;
    }

    static void TrimTargetStates()
    {
        while (Targets.Count > MaxRetainedTargets)
        {
            string? oldest = Targets
                .Where(entry => entry.Value.Scope is null)
                .OrderBy(entry => entry.Value.LastAccess)
                .Select(entry => entry.Key)
                .FirstOrDefault();
            if (oldest is null)
            {
                throw new InvalidOperationException(
                    "The Platform target-state limit cannot evict an active workspace.");
            }

            Targets.Remove(oldest);
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

        internal long LastAccess { get; set; }
    }

    sealed class PlatformLoadAttempt(
        BrowserPlatformScope? scope,
        ImmutableHashSet<string> packageKeys,
        WorkspaceContextLoadOutcome.Failed? failure) : IAsyncDisposable
    {
        BrowserPlatformScope? _scope = scope;

        internal BrowserPlatformScope? Scope => _scope;

        internal ImmutableHashSet<string> PackageKeys { get; } =
            packageKeys;

        internal WorkspaceContextLoadOutcome.Failed? Failure { get; } =
            failure;

        internal BrowserPlatformScope ReleaseScope()
        {
            BrowserPlatformScope released = _scope
                ?? throw new InvalidOperationException(
                    "A failed platform load attempt has no scope to release.");
            _scope = null;
            return released;
        }

        public async ValueTask DisposeAsync()
        {
            BrowserPlatformScope? scope = _scope;
            _scope = null;
            if (scope is not null)
                await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    static Host ProductionHost { get; } =
        new(
            BrowserPackageWorkspace.NetworkClient,
            BrowserPackageWorkspace.PackageSourceAuthorization);

    sealed record Host(
        HttpClient Client,
        IPackageSourceAuthorization SourceAuthorization);

    readonly record struct PlatformSelection(
        string Family,
        string Assembly);

    sealed class TrackingPackageStore(
        BrowserPackageWorkspace.PackageLeaseSet packageLeases)
        : IPackageStore, IPackagePayloadTransferPolicy
    {
        readonly ImmutableHashSet<string>.Builder _packageKeys =
            ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        internal ImmutableHashSet<string> PackageKeys =>
            _packageKeys.ToImmutable();

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
                packageLeases.Lease(packageKey);
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
            return content;
        }

        public async ValueTask<IPackagePayloadReservation> ReserveAsync(
            PackagePayloadTransfer transfer,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transfer);
            return new LeasingPackageReservation(
                await BrowserPackageWorkspace.PackageTransferPolicy
                    .ReserveAsync(transfer, cancellationToken)
                    .ConfigureAwait(false),
                BrowserPackageWorkspace.PackageKey(
                    transfer.Coordinate.PackageId,
                    transfer.Coordinate.Version),
                packageLeases);
        }

        sealed class LeasingPackageReservation(
            IPackagePayloadReservation inner,
            string packageKey,
            BrowserPackageWorkspace.PackageLeaseSet packageLeases)
            : IPackagePayloadReservation
        {
            public void Complete()
            {
                inner.Complete();
                packageLeases.Lease(packageKey);
            }

            public void Dispose() => inner.Dispose();
        }
    }
}
