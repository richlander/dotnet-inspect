using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

using InspectWeb.Engine;
using InspectWeb.Engine.CallGraphFacade;

/// <summary>
/// Platform call-graph expansion. The traversal progressively acquires the platform assemblies it
/// needs through the shared platform workspace and returns the same browser call-graph contract as
/// package traversal.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class CallGraphExports
{
    [JSExport]
    public static async Task<string> ExpandPlatformCallGraph(
        string targetFramework,
        string platformVersion,
        string assembly,
        string pack,
        string assemblyVersion,
        string? assemblyCulture,
        string? assemblyPublicKeyToken,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);
        BrowserCallGraph graph;
        await using (PlatformGraphBuild build =
            await BuildPlatformGraphAsync(
                targetFramework,
                platformVersion,
                assembly,
                pack,
                assemblyVersion,
                assemblyCulture,
                assemblyPublicKeyToken,
                typeFullName,
                memberName,
                selectorKey,
                metadataToken))
        {
            BrowserPlatformScopeResolution resolution = build.Resolution;
            MemberCallGraphView view = build.View;
            CallGraphProjection projection = build.Projection;
            int callerAssemblies = resolution.Scope.Members
                .Select(candidate =>
                    candidate.Participant.Assembly.Identity.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            graph = new BrowserCallGraph(
                    BrowserCallGraphProjection.Mermaid(projection),
                    BrowserCallGraphWireProjection.Project(
                        BrowserCallGraphProjection.Tree(view.CallerRoot)),
                    BrowserCallGraphWireProjection.Project(
                        BrowserCallGraphProjection.Tree(view.CalleeRoot)),
                    new BrowserCallGraphScope(
                        Packages: 0,
                        resolution.Scope.Members.Length,
                        callerAssemblies,
                        view.Tier.ToString()),
                    [
                        .. BrowserCallGraphProjection.Targets(
                            projection.Nodes,
                            resolution.Scope.Members.Select(candidate =>
                                candidate.Participant.Assembly.Identity),
                            resolution.Scope.PlatformPackForAssembly)
                            .Select(BrowserCallGraphWireProjection.Project),
                    ],
                    BrowserCallGraphWireProjection.Project(
                        BrowserCallGraphProjection.Diagnostics(
                            view.Diagnostics,
                            projection.HasUnexploredTraversalBoundary,
                            projection.HasAnalysisFailureBoundary)),
                    NoBody:
                        view.CalleeRoot is null
                        && view.CallerRoot is null);
        }

        return JsonSerializer.Serialize(
            graph,
            BrowserCallGraphJsonContext.Default.BrowserCallGraph);
    }

    public static Task<string> ExpandPlatformCallGraph(
        string targetFramework,
        string assembly,
        string pack,
        string assemblyVersion,
        string? assemblyCulture,
        string? assemblyPublicKeyToken,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken) =>
        ExpandPlatformCallGraph(
            targetFramework,
            "",
            assembly,
            pack,
            assemblyVersion,
            assemblyCulture,
            assemblyPublicKeyToken,
            typeFullName,
            memberName,
            selectorKey,
            metadataToken);

    static async Task<PlatformGraphBuild> BuildPlatformGraphAsync(
        string targetFramework,
        string platformVersion,
        string assembly,
        string pack,
        string assemblyVersion,
        string? assemblyCulture,
        string? assemblyPublicKeyToken,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyVersion);
        if (!Version.TryParse(assemblyVersion, out Version? expectedVersion))
        {
            throw new ArgumentException(
                $"Platform assembly version '{assemblyVersion}' is invalid.",
                nameof(assemblyVersion));
        }

        string assemblyFileName = assembly.EndsWith(
                ".dll",
                StringComparison.OrdinalIgnoreCase)
            ? assembly
            : $"{assembly}.dll";
        await using var owner = new PlatformScopeOwner(
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack));
        string rootFamily = owner.Current.Coordinate.Family;
        string rootAssembly =
            owner.Current.Participant.Participant.Assembly.Identity.Name;
        var expectedIdentity = new AssemblyReferenceIdentity(
            assemblyFileName[..^4],
            expectedVersion,
            assemblyCulture,
            assemblyPublicKeyToken);
        AssemblyReferenceIdentity rootIdentity =
            owner.Current.Participant.Participant.Assembly.Identity;
        if (!expectedIdentity.IsEquivalentTo(rootIdentity))
        {
            throw new InvalidOperationException(
                $"Platform call-graph target assembly '{expectedIdentity.Name}' "
                + "does not match the acquired assembly identity.");
        }

        for (int expansion = 0;
            expansion < BrowserInspectionScope.MaxAssembliesPerRole;
            expansion++)
        {
            PlatformMemberFocus focus =
                await ResolvePlatformMemberAsync(
                    owner,
                    targetFramework,
                    platformVersion,
                    rootFamily,
                    rootAssembly,
                    typeFullName,
                    memberName,
                    selectorKey,
                    metadataToken);
            BrowserPlatformScopeResolution current = owner.Current;
            MemberCallGraphView view = current.Scope.Use(group =>
            {
                using var session = new MemberCallGraphSession(
                    group,
                    focus.Participant.Participant.Assembly,
                    focus.Member.BodyToken);
                return session.HasCrossLibraryScope
                    ? session.CrossLibrary()
                    : session.Callers();
            });
            CallGraphProjection projection =
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot);
            AssemblyReferenceIdentity[] required =
                RequiredPlatformAssemblies(
                    projection,
                    current.Scope);
            if (required.Length == 0)
            {
                return new PlatformGraphBuild(
                    owner.Detach(),
                    view,
                    projection);
            }

            BrowserPlatformAssemblyRequest[] requests =
            [
                .. required.Select(identity =>
                {
                    string targetPack =
                    current.Scope.PlatformPackForAssembly(
                        identity.Name)
                    ?? throw new InvalidOperationException(
                        $"Platform assembly '{identity.Name}' is required to "
                        + "resolve a call-graph target, but no authorized "
                        + "platform pack supplies it.");
                    return new BrowserPlatformAssemblyRequest(
                        $"{identity.Name}.dll",
                        targetPack);
                }),
            ];
            await owner.ReplaceAsync(
                await BrowserPlatformWorkspace.OpenAssembliesAsync(
                    targetFramework,
                    platformVersion,
                    requests));
        }

        throw new InvalidOperationException(
            "Platform call-graph type resolution exceeded the browser "
            + "assembly-count limit.");
    }

    static async Task<PlatformMemberFocus> ResolvePlatformMemberAsync(
        PlatformScopeOwner owner,
        string targetFramework,
        string platformVersion,
        string rootFamily,
        string rootAssembly,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        MetadataTypeDefinitionName type =
            MetadataTypeDefinitionName.ParseSerialized(
                typeFullName) switch
            {
                MetadataTypeDefinitionNameResult.Valid valid =>
                    valid.Name,
                MetadataTypeDefinitionNameResult.Rejected =>
                    throw new InvalidOperationException(
                        $"Call-graph type identity '{typeFullName}' is invalid."),
                _ => throw new InvalidOperationException(
                    "Unknown metadata type-name result."),
            };

        for (int expansion = 0;
            expansion < BrowserInspectionScope.MaxAssembliesPerRole;
            expansion++)
        {
            BrowserPlatformScopeResolution current = owner.Current;
            WorkspaceContextMember root =
                current.Scope.Participant(
                    rootFamily,
                    rootAssembly);
            if (TryResolvePlatformMember(
                    current.Scope,
                    root,
                    typeFullName,
                    memberName,
                    selectorKey,
                    metadataToken)
                is { } direct)
            {
                return new PlatformMemberFocus(
                    root,
                    direct);
            }

            AssemblyContextTypeResolutionResult result =
                current.Scope.Use(group =>
                    AssemblyContextTypeResolutionQuery.Execute(
                        group,
                        root.Participant,
                        type,
                        AssemblyResolutionScope.Platform));
            TypeResolutionOutcome outcome = result switch
            {
                AssemblyContextTypeResolutionResult.Available
                    available => available.Outcome,
                AssemblyContextTypeResolutionResult.Rejected rejected =>
                    throw new InvalidOperationException(
                        $"Platform assembly "
                        + $"'{rejected.Assembly.Identity.Name}' could not "
                        + $"participate in type resolution "
                        + $"({rejected.Failure.Kind}: "
                        + $"{rejected.Failure.Detail})."),
                _ => throw new InvalidOperationException(
                    "Unknown assembly-context type-resolution result."),
            };
            if (outcome
                is TypeResolutionOutcome.Resolved resolved)
            {
                WorkspaceContextMember[] definitions =
                [
                    .. current.Scope.Members.Where(candidate =>
                        candidate.Participant.Assembly.Identity
                            .IsEquivalentTo(
                                resolved.Definition.Assembly
                                    .Assembly.Identity)),
                ];
                if (definitions.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Type '{typeFullName}' resolved to "
                        + $"'{resolved.Definition.Assembly.Assembly.Identity.Name}', "
                        + "but the platform workspace did not retain one "
                        + "unambiguous definition participant.");
                }

                Analysis.CallGraphMemberResolution? member =
                    TryResolvePlatformMember(
                        current.Scope,
                        definitions[0],
                        typeFullName,
                        memberName,
                        selectorKey,
                        metadataToken);
                if (member is null)
                {
                    throw new InvalidOperationException(
                        $"The resolved implementation of "
                        + $"'{typeFullName}.{memberName}' does not contain "
                        + "the selected API body.");
                }

                return new PlatformMemberFocus(
                    definitions[0],
                    member);
            }

            AssemblyReferenceIdentity? target =
                outcome.TerminalAssemblyIdentity;
            if (target is null)
            {
                throw new InvalidOperationException(
                    $"The platform workspace could not resolve "
                    + $"'{typeFullName}.{memberName}' "
                    + $"({ResolutionFailure(outcome)}).");
            }
            WorkspaceContextMember[] sameName =
                ResidentAssemblies(
                    current.Scope,
                    target.Name);
            if (sameName.Any(candidate =>
                    candidate.Participant.Assembly.Identity
                        .IsEquivalentTo(target)))
            {
                throw new InvalidOperationException(
                    $"The platform workspace could not resolve "
                    + $"'{typeFullName}.{memberName}' "
                    + $"({ResolutionFailure(outcome)}).");
            }
            ThrowIfIdentityConflict(target, sameName);

            string targetPack =
                current.Scope.PlatformPackForAssembly(target.Name)
                ?? throw new InvalidOperationException(
                    $"Platform assembly '{target.Name}' is required to "
                    + $"resolve '{typeFullName}', but no authorized "
                    + "platform pack supplies it.");
            await owner.ReplaceAsync(
                await BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    $"{target.Name}.dll",
                    targetPack));
        }

        throw new InvalidOperationException(
            "Platform member type resolution exceeded the browser "
            + "assembly-count limit.");
    }

    static Analysis.CallGraphMemberResolution? TryResolvePlatformMember(
        BrowserPlatformScope scope,
        WorkspaceContextMember participant,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        AssemblyContextApiSurfaceResult implementation =
            scope.UseParticipant(
                participant,
                (group, selected) =>
                    AssemblyContextApiSurfaceQuery.ExecuteBounded(
                        group,
                        ApiSurfaceScope.IncludeAll,
                        BrowserApiSurfacePolicy.Limits,
                        [selected]));
        if (implementation.Truncation is { } truncation)
        {
            throw new InvalidOperationException(
                $"The implementation surface for '{typeFullName}' exceeds the "
                + "browser projection bounds, so the selected body cannot be "
                + "resolved. "
                + BrowserApiSurfacePolicy.TruncationNotice(truncation));
        }

        AssemblyApiSurface surface = BrowserSurfaceProjection.Require(
            implementation.Assemblies.Assemblies.Single(),
            $"Implementation surface for '{typeFullName}'");
        return Analysis.CallGraphMemberResolver
            .ResolveDefinitionIdentity(
                surface.Surface,
                typeFullName,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken);
    }

    static AssemblyReferenceIdentity[] RequiredPlatformAssemblies(
        CallGraphProjection projection,
        BrowserPlatformScope scope)
    {
        var required = new List<AssemblyReferenceIdentity>();
        foreach (CallGraphNode node in projection.Nodes)
        {
            AssemblyReferenceIdentity? identity =
                node.DefinitionAssemblyIdentity is null
                    ? node.ResolutionAssemblyIdentity
                    : null;
            if (identity is null)
                continue;

            WorkspaceContextMember[] sameName =
                ResidentAssemblies(scope, identity.Name);
            if (sameName.Any(candidate =>
                    candidate.Participant.Assembly.Identity
                        .IsEquivalentTo(identity)))
            {
                continue;
            }
            ThrowIfIdentityConflict(identity, sameName);
            if (scope.PlatformPackForAssembly(identity.Name)
                    is null
                || required.Any(candidate =>
                    candidate.IsEquivalentTo(identity)))
            {
                continue;
            }

            required.Add(identity);
        }
        return [.. required];
    }

    static WorkspaceContextMember[] ResidentAssemblies(
        BrowserPlatformScope scope,
        string assembly) =>
    [
        .. scope.Members.Where(candidate =>
            candidate.Participant.Assembly.Identity.Name.Equals(
                assembly,
                StringComparison.OrdinalIgnoreCase)),
    ];

    static void ThrowIfIdentityConflict(
        AssemblyReferenceIdentity required,
        WorkspaceContextMember[] residents)
    {
        if (residents.Length == 0)
            return;

        string retained = string.Join(
            ", ",
            residents.Select(candidate =>
                $"{candidate.Participant.Assembly.Identity.Name} "
                + $"{candidate.Participant.Assembly.Identity.Version}"));
        throw new InvalidOperationException(
            $"Platform type resolution requires '{required.Name} "
            + $"{required.Version}', but the workspace retains "
            + $"a different identity: {retained}.");
    }

    static string ResolutionFailure(
        TypeResolutionOutcome outcome) =>
        outcome switch
        {
            TypeResolutionOutcome.NotFound =>
                "the acquired assembly does not declare or forward the type",
            TypeResolutionOutcome.UnboundBinding =>
                "the next forwarding assembly is not resident",
            TypeResolutionOutcome.Unavailable =>
                "a required assembly is unavailable",
            TypeResolutionOutcome.Ambiguous =>
                "the definition is ambiguous",
            TypeResolutionOutcome.Rejected =>
                "the forwarding relationship was rejected",
            _ => "type resolution failed",
        };

    sealed record PlatformMemberFocus(
        WorkspaceContextMember Participant,
        Analysis.CallGraphMemberResolution Member);

    sealed class PlatformScopeOwner(
        BrowserPlatformScopeResolution current) : IAsyncDisposable
    {
        BrowserPlatformScopeResolution? _current = current;

        internal BrowserPlatformScopeResolution Current =>
            _current
            ?? throw new ObjectDisposedException(nameof(PlatformScopeOwner));

        internal async ValueTask ReplaceAsync(
            BrowserPlatformScopeResolution replacement)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            BrowserPlatformScopeResolution previous = Current;
            _current = replacement;
            await previous.DisposeAsync().ConfigureAwait(false);
        }

        internal BrowserPlatformScopeResolution Detach()
        {
            BrowserPlatformScopeResolution result = Current;
            _current = null;
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            BrowserPlatformScopeResolution? current = _current;
            _current = null;
            if (current is not null)
                await current.DisposeAsync().ConfigureAwait(false);
        }
    }

    sealed record PlatformGraphBuild(
        BrowserPlatformScopeResolution Resolution,
        MemberCallGraphView View,
        CallGraphProjection Projection) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Resolution.DisposeAsync();
    }

}
