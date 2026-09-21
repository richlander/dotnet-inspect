using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;
using NuGet.Versioning;

namespace DotnetInspect.Web;

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

    internal RealizedMemberCoordinate.Platform PlatformCoordinate(
        string family,
        string assembly)
    {
        RealizedMemberCoordinate.Platform[] coordinates =
        [
            .. Context.AvailablePlatformAssemblies.Where(candidate =>
                candidate.Family.Equals(family, StringComparison.Ordinal)
                && string.Equals(
                    candidate.Assembly,
                    assembly,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        return coordinates.Length == 1
            ? coordinates[0]
            : throw new InvalidOperationException(
                $"Platform family '{family}' assembly '{assembly}' has no unique available coordinate.");
    }

    public ValueTask DisposeAsync()
    {
        _context = null;
        return BrowserInspectionScope.CloseWorkspaceAsync(_workspace);
    }
}

[SupportedOSPlatform("browser")]
internal sealed record BrowserPlatformScopeResolution(
    BrowserPlatformScope Scope,
    WorkspaceContextMember Participant,
    RealizedMemberCoordinate.Platform Coordinate,
    BrowserScopeLease<BrowserPlatformScope> ScopeLease,
    string? ContextId = null) : IAsyncDisposable
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
    internal const string RuntimeFamily = "runtime";
    internal const string AspNetCoreFamily = "aspnetcore";
    const string RuntimePack = "netcore.app";
    const string AspNetCorePack = "aspnetcore.app";
    const string DefaultRuntimeAssembly = "System.Private.CoreLib";
    const int MaxRetainedTargets = BrowserPackageWorkspace.MaxOpenScopes;

    static readonly Dictionary<string, TargetState> Targets =
        new(StringComparer.Ordinal);
    static readonly Dictionary<string, DemoTarget> DemoTargets =
        new(StringComparer.Ordinal);
    static readonly Dictionary<string, Task> TargetTails =
        new(StringComparer.Ordinal);
    static long _targetClock;
    static long _demoScopeClock;

    internal static BrowserPlatformScopeResolution LeaseRetainedAssembly(
        string framework, string version, string assembly)
    {
        string name = AssemblySimpleName(assembly);
        BrowserPlatformScope[] demoScopes = DemoTargets.Values
            .Select(target => target.Scope)
            .Distinct()
            .Where(scope => BrowserPackageWorkspace.IsScopeRetained(scope)
                && scope.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase)
                && scope.Coordinates.Any(coordinate =>
                    coordinate.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(coordinate.Assembly, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        BrowserPlatformScope[] scopes = (demoScopes.Length != 0
                ? demoScopes
                : Targets.Values.Select(state => state.Scope).OfType<BrowserPlatformScope>())
            .Distinct()
            .Where(scope => BrowserPackageWorkspace.IsScopeRetained(scope)
                && scope.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase)
                && scope.Coordinates.Any(coordinate =>
                    coordinate.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(coordinate.Assembly, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (scopes.Length != 1)
            throw new InvalidOperationException(
                $"ContextUnavailable: no unique retained platform workspace contains {name} {version} / {framework}.");
        BrowserPlatformScope retained = scopes[0];
        RealizedMemberCoordinate.Platform[] coordinates = retained.Coordinates.Where(coordinate =>
            coordinate.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
            && string.Equals(coordinate.Assembly, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (coordinates.Length != 1)
            throw new InvalidOperationException(
                "ContextUnavailable: the retained platform assembly selection is ambiguous.");
        RealizedMemberCoordinate.Platform selected = coordinates[0];
        return new(retained, retained.Participant(selected.Family, name),
            selected, BrowserPackageWorkspace.LeaseScope(retained));
    }

    internal static Task<BrowserPlatformScopeResolution> OpenRetainedContextAssemblyAsync(
        string contextId,
        string targetFramework,
        string platformVersion,
        string assembly,
        string pack,
        CancellationToken cancellationToken = default)
    {
        string targetKey = TargetKey(targetFramework, platformVersion);
        string family = Family(pack);
        string name = AssemblySimpleName(assembly);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => EnqueueAsync(
                targetKey,
                async () =>
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    await using BrowserPlatformScopeResolution current =
                        LeaseDemoContext(targetKey, contextId);
                    BrowserPlatformScopeResolution selected = current;
                    if (!current.Scope.Coordinates.Any(coordinate =>
                            coordinate.Family == family
                            && string.Equals(coordinate.Assembly, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        selected = await ExpandContextCoreAsync(
                            targetKey,
                            current,
                            [new(assembly, pack)],
                            ProductionHost,
                            deadline).ConfigureAwait(false);
                    }

                    try
                    {
                        return new BrowserPlatformScopeResolution(
                            selected.Scope,
                            selected.Scope.Participant(family, name),
                            AssertSingleCoordinate(selected.Scope, family, name),
                            BrowserPackageWorkspace.LeaseScope(selected.Scope),
                            contextId);
                    }
                    finally
                    {
                        if (!ReferenceEquals(selected, current))
                            await selected.DisposeAsync().ConfigureAwait(false);
                    }
                },
                deadline.Token),
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);
    }

    static BrowserPlatformScopeResolution LeaseDemoContext(
        string targetKey,
        string contextId)
    {
        if (!DemoTargets.TryGetValue(targetKey, out DemoTarget? target)
            || !string.Equals(target.ContextId, contextId, StringComparison.Ordinal)
            || !BrowserPackageWorkspace.IsScopeRetained(target.Scope))
        {
            throw new InvalidOperationException(
                "ContextUnavailable: the selected Platform demo context is no longer retained.");
        }

        RealizedMemberCoordinate.Platform coordinate = target.Scope.Coordinates[0];
        return new(
            target.Scope,
            target.Scope.Participant(coordinate.Family, coordinate.Assembly!),
            coordinate,
            BrowserPackageWorkspace.LeaseScope(target.Scope),
            contextId);
    }

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

    internal static async Task<CompiledDocumentationOutcome>
        QueryMemberDocumentationAsync(
            string targetFramework,
            string platformVersion,
            string assemblyFileName,
            string pack,
            string documentationId,
            CancellationToken cancellationToken = default) =>
        await QueryMemberDocumentationAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack,
                documentationId,
                BrowserPackageWorkspace.NetworkClient,
                BrowserPackageWorkspace.Gallery,
                BrowserPackageWorkspace.PackageSourceAuthorization,
                BrowserPackageWorkspace.PackageOperationTimeout,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<CompiledDocumentationOutcome>
        QueryMemberDocumentationAsync(
            string targetFramework,
            string platformVersion,
            string assemblyFileName,
            string pack,
            string documentationId,
            HttpClient workspaceClient,
            IPackageSourceClient packageClient,
            IPackageSourceAuthorization sourceAuthorization,
            TimeSpan operationTimeout,
            CancellationToken cancellationToken = default)
    {
        await using BrowserPlatformScopeResolution resolution =
            await OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyFileName,
                    pack,
                    workspaceClient,
                    sourceAuthorization,
                    operationTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        AssemblyReferenceIdentity assemblyIdentity =
            resolution.Participant.Participant.Assembly.Identity;
        PlatformFamily family = resolution.Coordinate.Family switch
        {
            RuntimeFamily => PlatformFamily.DotNetRuntime,
            AspNetCoreFamily => PlatformFamily.AspNetCore,
            _ => throw new InvalidOperationException(
                "The selected Browser Platform family is unsupported."),
        };
        var target = new PlatformFamilyTarget(
            family,
            PlatformTargetFramework.Parse(
                resolution.Coordinate.Framework),
            PlatformVersion.Parse(
                resolution.Coordinate.Version));

        return await BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => QueryMemberDocumentationCoreAsync(
                target,
                assemblyIdentity,
                documentationId,
                packageClient,
                sourceAuthorization,
                deadline),
            operationTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CompiledDocumentationOutcome>
        QueryMemberDocumentationCoreAsync(
            PlatformFamilyTarget target,
            AssemblyReferenceIdentity assemblyIdentity,
            string documentationId,
            IPackageSourceClient sourceClient,
            IPackageSourceAuthorization sourceAuthorization,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        await using PackageSourceSettlementLease sourceLease =
            PackageSourceSettlementService.IssueLease(
                authority =>
                    ReferenceEquals(
                        authority.Association,
                        sourceClient.Source.Association)
                        ? sourceClient
                        : throw new InvalidOperationException(
                            "The Platform documentation request selected another configured source."));
        var source = new PackagePlatformSource(
            sourceAuthorization,
            new PackagePayloadAcquisitionPlan(
                static (_, _) =>
                    BrowserPackageWorkspace.SessionPackageStore,
                BrowserPackageWorkspace.PackageLimits,
                new BrowserPackageWorkspace
                    .BrowserPackageOperationTransferPolicy(
                        BrowserPackageWorkspace.PackageTransferPolicy,
                        deadline)));
        var adapter = new PackagePlatformHouseAdapter(
            source,
            "browser-platform-documentation");
        TimeSpan sourceTimeout =
            BrowserPackageWorkspace.SourceSettlementOperationTimeout(
                deadline.Remaining);
        var work = new PlatformHouseWorkBudget(
            maxSourceOperations: 1,
            maxTargetCandidates: 0,
            maxAssemblies: 1,
            maxXmlDocuments: 1,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes:
                BrowserInspectionScope.MaxRetainedImageBytes
                + 8L * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: sourceTimeout);
        var request =
            new PlatformCompiledDocumentationInspectionRequest(
                target,
                assemblyIdentity,
                [documentationId],
                PlatformCompiledDocumentationSubjectSelection.RequireAll);
        InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome> envelope =
                await PlatformCompiledDocumentationInspection
                    .ExecutePackageBackedAsync(
                        request,
                        adapter,
                        sourceLease.IssueOperationLease(
                            deadline.Token,
                            sourceTimeout,
                            sourceTimeout),
                        work,
                        new PlatformCompiledDocumentationQueryLimits
                        {
                            ApiSurface =
                                BrowserApiSurfacePolicy.ExtractionBounds,
                        },
                        deadline.Token)
                    .ConfigureAwait(false);
        return envelope.Content switch
        {
            PlatformCompiledDocumentationInspectionOutcome.Completed
                completed =>
                GetSingleDocumentationOutcome(
                    completed.Document),
            PlatformCompiledDocumentationInspectionOutcome.NotAvailable
                notAvailable =>
                throw new InvalidOperationException(
                    "Package-backed Platform compiled documentation could "
                        + $"not be settled at "
                        + $"{notAvailable.Failure.Stage}: "
                        + notAvailable.Failure.Summary),
            _ => throw new InvalidOperationException(
                "Unknown Platform compiled-documentation inspection outcome."),
        };
    }

    private static CompiledDocumentationOutcome
        GetSingleDocumentationOutcome(
            PlatformCompiledDocumentationDocument document) =>
        document.Outcomes.Length == 1
            ? document.Outcomes[0]
            : throw new InvalidOperationException(
                "The single-subject Platform documentation inspection "
                    + $"returned {document.Outcomes.Length} outcomes.");

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

    internal static Task<BrowserPlatformScopeResolution> OpenAssembliesAsync(
        string targetFramework,
        string? platformVersion,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        OpenAssembliesAsync(
            targetFramework,
            platformVersion,
            assemblies,
            new Host(client, sourceAuthorization),
            operationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenContextAsync(
        WorkspacePlan plan,
        WorkspaceContextInput context,
        string focusFamily,
        string focusAssembly,
        CancellationToken cancellationToken = default) =>
        OpenContextAsync(
            plan,
            context,
            focusFamily,
            focusAssembly,
            ProductionHost,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> OpenContextAsync(
        WorkspacePlan plan,
        WorkspaceContextInput context,
        string focusFamily,
        string focusAssembly,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        OpenContextAsync(
            plan,
            context,
            focusFamily,
            focusAssembly,
            new Host(client, sourceAuthorization),
            operationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> ExpandContextAsync(
        BrowserPlatformScopeResolution current,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        CancellationToken cancellationToken = default) =>
        ExpandContextAsync(
            current,
            assemblies,
            ProductionHost,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformScopeResolution> ExpandContextAsync(
        BrowserPlatformScopeResolution current,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        HttpClient client,
        IPackageSourceAuthorization sourceAuthorization,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default) =>
        ExpandContextAsync(
            current,
            assemblies,
            new Host(client, sourceAuthorization),
            operationTimeout,
            cancellationToken);

    static Task<BrowserPlatformScopeResolution> OpenContextAsync(
        WorkspacePlan plan,
        WorkspaceContextInput context,
        string focusFamily,
        string focusAssembly,
        Host host,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(focusFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(focusAssembly);
        if (!plan.Contexts.Any(candidate => ReferenceEquals(candidate, context)))
        {
            throw new ArgumentException(
                "The selected context must be the exact input retained by the Workspace plan.",
                nameof(context));
        }

        PlatformPlanContext platform =
            PlatformPlanContext.Create(context, focusFamily, focusAssembly);
        string targetKey = TargetKey(platform.Framework, platform.Version);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => EnqueueAsync(
                targetKey,
                () => OpenContextCoreAsync(
                    targetKey,
                    plan,
                    context,
                    platform,
                    host,
                    deadline),
                deadline.Token),
            operationTimeout,
            cancellationToken);
    }

    static Task<BrowserPlatformScopeResolution> ExpandContextAsync(
        BrowserPlatformScopeResolution current,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        Host host,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Count == 0)
        {
            throw new ArgumentException(
                "A Platform demo scope expansion requires at least one assembly.",
                nameof(assemblies));
        }

        string targetFramework = current.Scope.Framework;
        string platformVersion = current.Coordinate.Version;
        string targetKey = TargetKey(targetFramework, platformVersion);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            deadline => EnqueueAsync(
                targetKey,
                () => ExpandContextCoreAsync(
                    targetKey,
                    current,
                    assemblies,
                    host,
                    deadline),
                deadline.Token),
            operationTimeout,
            cancellationToken);
    }

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

    static async Task<BrowserPlatformScopeResolution> OpenContextCoreAsync(
        string targetKey,
        WorkspacePlan plan,
        WorkspaceContextInput context,
        PlatformPlanContext platform,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        deadline.Token.ThrowIfCancellationRequested();
        using var packageLeases =
            new BrowserPackageWorkspace.PackageLeaseSet();

        DemoTargets.TryGetValue(
            targetKey,
            out DemoTarget? previous);
        await using BrowserScopeLease<BrowserPlatformScope>? retainedLease =
            previous is not null
            && BrowserPackageWorkspace.IsScopeRetained(previous.Scope)
                ? BrowserPackageWorkspace.LeaseScope(previous.Scope)
                : null;
        await using ScopeReservation reservation =
            await BrowserPackageWorkspace.ReserveScopeAsync(deadline.Token)
                .ConfigureAwait(false);
        await using PlatformLoadAttempt attempt =
            await LoadContextAttemptAsync(
                plan,
                context,
                host,
                deadline,
                packageLeases).ConfigureAwait(false);
        if (attempt.Failure is not null)
            throw Failure(attempt.Failure);

        return await PublishDemoScopeAsync(
            targetKey,
            attempt.ReleaseScope(),
            attempt.PackageKeys,
            reservation,
            platform.FocusFamily,
            platform.FocusAssembly,
            Guid.NewGuid().ToString("N")).ConfigureAwait(false);
    }

    static async Task<BrowserPlatformScopeResolution> ExpandContextCoreAsync(
        string targetKey,
        BrowserPlatformScopeResolution current,
        IReadOnlyList<BrowserPlatformAssemblyRequest> assemblies,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        deadline.Token.ThrowIfCancellationRequested();
        await using BrowserPlatformScopeResolution? retained =
            current.ContextId is { } contextId
                ? LeaseDemoContext(targetKey, contextId)
                : null;
        using var packageLeases =
            new BrowserPackageWorkspace.PackageLeaseSet();
        var selections = ImmutableArray.CreateBuilder<PlatformSelection>();
        foreach (BrowserPlatformAssemblyRequest request in assemblies)
        {
            ArgumentNullException.ThrowIfNull(request);
            selections.Add(
                new PlatformSelection(
                    Family(request.Pack),
                    AssemblySimpleName(request.AssemblyFileName)));
        }
        await using ScopeReservation reservation =
            await BrowserPackageWorkspace.ReserveScopeAsync(deadline.Token)
                .ConfigureAwait(false);
        BrowserPlatformScope basis = (retained ?? current).Scope;
        ImmutableArray<RealizedMemberCoordinate.Platform> coordinates =
            basis.Coordinates;
        foreach (PlatformSelection selection in selections)
        {
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
                await using PlatformLoadAttempt declared =
                    await LoadDeclaredAttemptAsync(
                        basis.Framework,
                        current.Coordinate.Version,
                        selection.Family,
                        selection.Assembly,
                        host,
                        deadline,
                        packageLeases).ConfigureAwait(false);
                if (declared.Failure is not null)
                    throw Failure(declared.Failure);
                coordinates = coordinates.Add(
                    AssertSingleCoordinate(
                        declared.Scope!,
                        selection.Family,
                        selection.Assembly));
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

        (BrowserPlatformScope candidate, ImmutableHashSet<string> packageKeys) =
            await LoadRealizedAsync(
                coordinates,
                host,
                deadline,
                packageLeases).ConfigureAwait(false);
        RealizedMemberCoordinate.Platform focus =
            current.Coordinate;
        return await PublishDemoScopeAsync(
            targetKey,
            candidate,
            packageKeys,
            reservation,
            focus.Family,
            current.Participant.Participant.Assembly.Identity.Name,
            current.ContextId ?? Guid.NewGuid().ToString("N"))
            .ConfigureAwait(false);
    }

    static async Task<BrowserPlatformScopeResolution> PublishDemoScopeAsync(
        string targetKey,
        BrowserPlatformScope candidate,
        ImmutableHashSet<string> packageKeys,
        ScopeReservation reservation,
        string focusFamily,
        string focusAssembly,
        string contextId)
    {
        RealizedMemberCoordinate.Platform selected =
            AssertSingleCoordinate(
                candidate,
                focusFamily,
                focusAssembly);
        string scopeKey = ScopeKey(candidate.Coordinates);
        BrowserScopeLease<BrowserPlatformScope> lease =
            await BrowserPackageWorkspace.RegisterScopeAsync(
                    reservation,
                    $"{scopeKey}|demo:{Interlocked.Increment(ref _demoScopeClock)}",
                    candidate,
                    packageKeys,
                    ForgetDemoScope)
                .ConfigureAwait(false);
        BrowserPlatformScope registered = lease.Scope;
        WorkspaceContextMember participant =
            registered.Participant(
                focusFamily,
                focusAssembly);
        DemoTargets.TryGetValue(
            targetKey,
            out DemoTarget? previous);
        DemoTargets[targetKey] = new(contextId, registered);
        if (previous is not null
            && !ReferenceEquals(previous.Scope, registered))
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(previous.Scope)
                .ConfigureAwait(false);
        }

        return new BrowserPlatformScopeResolution(
            registered,
            participant,
            selected,
            lease,
            contextId);
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
        if (ReferenceEquals(host, ProductionHost) && platformVersion is not null)
        {
            foreach (string family in selections.Select(selection => selection.Family).Distinct())
            {
                BrowserPackage package = await BrowserPlatformCatalog.AcquireRuntimeAsync(
                    targetFramework, family, platformVersion,
                    deadline.Remaining, deadline.Token).ConfigureAwait(false);
                packageLeases.Lease(package.CacheKey);
            }
        }
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

        ImmutableArray<RealizedMemberCoordinate.Platform> coordinates =
            state.Coordinates;
        if (selections.Length == 1)
        {
            PlatformSelection selection = selections[0];
            coordinates = [.. coordinates.Where(candidate =>
                !string.Equals(
                    candidate.Assembly,
                    selection.Assembly,
                    StringComparison.OrdinalIgnoreCase)
                || candidate.Family.Equals(
                    selection.Family,
                    StringComparison.Ordinal))];
        }

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
        }

        PlatformSelection selected = selections[^1];
        RealizedMemberCoordinate.Platform? requested =
            coordinates.FirstOrDefault(candidate =>
                candidate.Family.Equals(
                    selected.Family,
                    StringComparison.Ordinal)
                && string.Equals(
                    candidate.Assembly,
                    selected.Assembly,
                    StringComparison.OrdinalIgnoreCase));
        bool allRequested = selections.All(selection =>
            coordinates.Any(candidate =>
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
            return await AttemptAsync(
                workspace,
                outcome,
                store.PackageKeys).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure).ConfigureAwait(false);
            throw;
        }
    }

    static async Task<PlatformLoadAttempt> LoadContextAttemptAsync(
        WorkspacePlan plan,
        WorkspaceContextInput context,
        Host host,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
        BrowserPackageWorkspace.PackageLeaseSet packageLeases)
    {
        var workspace = new InspectionWorkspace(plan);
        var store = new TrackingPackageStore(packageLeases);
        try
        {
            WorkspaceContextLoadOutcome outcome =
                await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    context,
                    Options(store, host, deadline),
                    deadline.Token).ConfigureAwait(false);
            return await AttemptAsync(
                workspace,
                outcome,
                store.PackageKeys).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure)
                .ConfigureAwait(false);
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
            return await AttemptAsync(
                workspace,
                outcome,
                store.PackageKeys).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure).ConfigureAwait(false);
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
            PlatformLoadAttempt attempt = await AttemptAsync(
                workspace,
                outcome,
                store.PackageKeys).ConfigureAwait(false);
            return attempt.Failure is null
                ? (attempt.Scope!, attempt.PackageKeys)
                : throw Failure(attempt.Failure);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure).ConfigureAwait(false);
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

    static async Task<PlatformLoadAttempt> AttemptAsync(
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

        await BrowserInspectionScope.CloseWorkspaceAsync(workspace).ConfigureAwait(false);
        return outcome is WorkspaceContextLoadOutcome.Failed failed
            ? new PlatformLoadAttempt(
                scope: null,
                packageKeys,
                failed)
            : throw new InvalidOperationException(
                "Platform workspace loading returned an unknown outcome.");
    }

    static async ValueTask CloseAfterFailureAsync(
        InspectionWorkspace workspace,
        Exception failure)
    {
        List<Exception> cleanupFailures = [];
        await BrowserInspectionScope.TryCloseAsync(workspace, cleanupFailures)
            .ConfigureAwait(false);
        if (cleanupFailures.Count > 0)
            throw new BrowserScopeConstructionException(failure, cleanupFailures);
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

    internal static bool IsSupportedFamily(string family) =>
        family is RuntimeFamily or AspNetCoreFamily;

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
        + VersionKey(platformVersion);

    static string VersionKey(string? platformVersion) =>
        platformVersion is null
            ? "latest"
            : NuGetVersion.TryParse(
                platformVersion,
                out NuGetVersion? parsed)
                ? parsed.ToNormalizedString().ToLowerInvariant()
                : platformVersion.ToLowerInvariant();

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

    static void ForgetDemoScope(BrowserPlatformScope scope)
    {
        string? key = DemoTargets
            .Where(candidate => ReferenceEquals(candidate.Value.Scope, scope))
            .Select(candidate => candidate.Key)
            .FirstOrDefault();
        if (key is not null)
            DemoTargets.Remove(key);
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

    sealed record DemoTarget(string ContextId, BrowserPlatformScope Scope);

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

    readonly record struct PlatformPlanContext(
        string Framework,
        string Version,
        string FocusFamily,
        string FocusAssembly)
    {
        internal static PlatformPlanContext Create(
            WorkspaceContextInput context,
            string focusFamily,
            string focusAssembly)
        {
            WorkspaceMemberCoordinate.PlatformMember[] members =
            [
                .. context.Members
                    .OfType<WorkspaceMemberCoordinate.PlatformMember>(),
            ];
            if (members.Length != context.Members.Count)
            {
                throw new InvalidOperationException(
                    "A Browser Platform home demo context cannot contain non-Platform members.");
            }
            if (members.Length == 0)
            {
                throw new InvalidOperationException(
                    "A Browser Platform home demo context has no members.");
            }

            WorkspaceMemberCoordinate.PlatformMember? focus =
                members.FirstOrDefault(member =>
                    member.Family.Equals(
                        focusFamily,
                        StringComparison.Ordinal)
                    && string.Equals(
                        member.Assembly,
                        focusAssembly,
                        StringComparison.OrdinalIgnoreCase));
            if (focus is null)
            {
                throw new InvalidOperationException(
                    "The Browser Platform home demo focus is not a member of its selected context.");
            }

            string framework = focus.Framework
                ?? context.Framework
                ?? throw new InvalidOperationException(
                    "A Browser Platform home demo requires an exact target framework.");
            string version = focus.Version
                ?? throw new InvalidOperationException(
                    "A Browser Platform home demo requires an exact Platform version.");
            return new PlatformPlanContext(
                framework,
                version,
                focusFamily,
                focusAssembly);
        }
    }

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
