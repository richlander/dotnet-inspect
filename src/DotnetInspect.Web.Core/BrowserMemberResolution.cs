using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

/// <summary>
/// Resolves a reference-surface body selector onto its implementation participant.
/// </summary>
/// <remarks>
/// Metadata tokens are image-local, so the product resolver validates the token and falls back to
/// the opaque structural selector when <c>ref/</c> and <c>lib/</c> row numbers differ. Metadata,
/// Analysis, source, call-graph, and catalog facades all start from the same gesture — an exact
/// package coordinate plus one browser-issued member selector — so the resolution runs once here
/// rather than once per capability.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserMemberResolution
{
    internal sealed record Resolved(
        BrowserWorkspaceParticipant SurfaceParticipant,
        BrowserWorkspaceParticipant ImplementationParticipant,
        AssemblyContextMemberBody Member);

    /// <summary>
    /// One resolved member and the protected use of the workspace it was resolved in. The lease
    /// holds that workspace for the whole of the caller's query, including its asynchronous
    /// return, so the caller disposes the resolution rather than the scope.
    /// </summary>
    internal sealed record ScopedResolution(
        BrowserScopeLease<BrowserInspectionScope> Lease,
        BrowserWorkspaceParticipant SurfaceParticipant,
        BrowserWorkspaceParticipant ImplementationParticipant,
        AssemblyContextMemberBody Member) : IAsyncDisposable
    {
        internal BrowserInspectionScope Scope => Lease.Scope;

        public ValueTask DisposeAsync() => Lease.DisposeAsync();
    }

    internal sealed record ScopedDeclarationResolution(
        BrowserScopeLease<BrowserInspectionScope> Lease,
        AssemblyContextMemberDeclaration Member) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Lease.DisposeAsync();
    }

    internal sealed record ScopedPlatformDeclarationResolution(
        BrowserPlatformScopeResolution Resolution,
        AssemblyContextMemberDeclaration Member) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Resolution.DisposeAsync();
    }

    internal sealed record ScopedPlatformResolution(
        BrowserPlatformScopeResolution Resolution,
        AssemblyContextMemberBody Member) : IAsyncDisposable
    {
        internal BrowserPlatformScope Scope => Resolution.Scope;
        internal WorkspaceContextMember Participant => Resolution.Participant;

        public ValueTask DisposeAsync() => Resolution.DisposeAsync();
    }

    internal sealed record ScopedPlatformTypeResolution(
        BrowserPlatformScopeResolution Resolution,
        ApiType Type) : IAsyncDisposable
    {
        internal BrowserPlatformScope Scope => Resolution.Scope;
        internal WorkspaceContextMember Participant => Resolution.Participant;

        public ValueTask DisposeAsync() => Resolution.DisposeAsync();
    }

    /// <summary>
    /// Resolves one exact package/version/framework coordinate, reuses its workspace, and returns
    /// the reference-preferred participant for one product-selected compile asset.
    /// </summary>
    internal static async Task<(
        BrowserScopeLease<BrowserInspectionScope> Lease,
        BrowserWorkspaceParticipant Participant)> SurfaceParticipantAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName)
    {
        BrowserScopeLease<BrowserInspectionScope> lease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        try
        {
            BrowserInspectionScope scope = lease.Scope;
            BrowserPackageCoordinate coordinate = scope.Coordinates[0];
            return (lease, scope.SurfaceParticipant(
                coordinate,
                coordinate.CompileAsset(assemblyName)));
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<ScopedResolution> ImplementationMemberAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken,
        CancellationToken cancellationToken = default)
    {
        BrowserScopeLease<BrowserInspectionScope> lease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework,
                cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            BrowserInspectionScope scope = lease.Scope;
            BrowserPackageCoordinate coordinate = scope.Coordinates[0];
            Resolved resolved = ResolveImplementationMember(
                scope,
                coordinate,
                assemblyName,
                typeId,
                memberName,
                selectorKey,
                metadataToken);
            return new ScopedResolution(
                lease,
                resolved.SurfaceParticipant,
                resolved.ImplementationParticipant,
                resolved.Member);
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<ScopedDeclarationResolution> DeclarationMemberAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken,
        bool implementationMember,
        CancellationToken cancellationToken = default)
    {
        BrowserScopeLease<BrowserInspectionScope> lease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework,
                cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            BrowserInspectionScope scope = lease.Scope;
            BrowserPackageCoordinate coordinate = scope.Coordinates[0];
            AssemblyContextMemberDeclaration member = implementationMember
                ? ResolveImplementationDeclaration(
                    scope,
                    coordinate,
                    assemblyName,
                    typeId,
                    memberName,
                    selectorKey,
                    metadataToken)
                : ResolveSurfaceDeclaration(
                    scope,
                    coordinate,
                    assemblyName,
                    typeId,
                    memberName,
                    selectorKey,
                    metadataToken);
            return new ScopedDeclarationResolution(lease, member);
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<ScopedPlatformDeclarationResolution>
        PlatformDeclarationMemberAsync(
            string targetFramework,
            string platformVersion,
            string assemblyName,
            string pack,
            string typeId,
            string memberName,
            string selectorKey,
            int metadataToken,
            CancellationToken cancellationToken = default)
    {
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyName,
                pack,
                cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextMemberDeclaration member =
                resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, selected) => ResolveDeclaration(
                    group,
                    selected,
                    typeId,
                    memberName,
                    selectorKey,
                    metadataToken,
                    "Platform declaration"));
            return new ScopedPlatformDeclarationResolution(
                resolution,
                member);
        }
        catch
        {
            await resolution.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<ScopedPlatformResolution>
        PlatformImplementationMemberAsync(
            string targetFramework,
            string platformVersion,
            string assemblyName,
            string pack,
            string typeId,
            string memberName,
            string selectorKey,
            int metadataToken,
            string? contextId = null,
            CancellationToken cancellationToken = default)
    {
        BrowserPlatformScopeResolution resolution =
            contextId is null
                ? await BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    cancellationToken)
                : await BrowserPlatformWorkspace.OpenRetainedContextAssemblyAsync(
                    contextId,
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextMemberBody member =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    (group, selected) => ResolveImplementationMember(
                        group,
                        selected,
                        typeId,
                        memberName,
                        selectorKey,
                        metadataToken));
            return new ScopedPlatformResolution(resolution, member);
        }
        catch
        {
            await resolution.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<ScopedPlatformTypeResolution>
        PlatformImplementationTypeAsync(
            string targetFramework,
            string platformVersion,
            string assemblyName,
            string pack,
            string typeId,
            string? contextId = null,
            CancellationToken cancellationToken = default)
    {
        BrowserPlatformScopeResolution resolution =
            contextId is null
                ? await BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    cancellationToken)
                : await BrowserPlatformWorkspace.OpenRetainedContextAssemblyAsync(
                    contextId,
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApiType type = resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, selected) => RequireSelection(
                    AssemblyContextMemberSelectionQuery.ExecuteType(
                        group,
                        selected,
                        typeId,
                        BrowserApiSurfacePolicy.Limits),
                    "Implementation type selection"));
            return new ScopedPlatformTypeResolution(resolution, type);
        }
        catch
        {
            await resolution.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static Resolved ResolveImplementationMember(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate,
        string assemblyName,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);
        PackageCompileAsset surfaceAsset = coordinate.CompileAsset(assemblyName);
        BrowserWorkspaceParticipant surfaceParticipant =
            scope.SurfaceParticipant(coordinate, surfaceAsset);
        BrowserWorkspaceParticipant participant =
            scope.ImplementationParticipant(surfaceParticipant);
        AssemblyContextMemberBody resolution =
            scope.UseImplementationParticipant(
                participant,
                (group, member) => ResolveImplementationMember(
                    group,
                    member,
                    typeId,
                    memberName,
                    selectorKey,
                    metadataToken));
        return new Resolved(surfaceParticipant, participant, resolution);
    }

    internal static ApiSurface ImplementationSurface(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        // One participant, under the same browser bounds as the package load: a body selector is
        // resolved against the implementation surface, and an over-budget implementation must
        // fail visibly rather than resolve against a silently shortened surface.
        AssemblyContextApiSurfaceResult implementationSurfaces =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    group,
                    ApiSurfaceScope.IncludeAll,
                    BrowserApiSurfacePolicy.Limits,
                    [participant]);
        if (implementationSurfaces.Truncation is { } truncation)
        {
            throw new InvalidOperationException(
                $"The implementation surface exceeds the browser projection "
                + $"bounds, so the selected body cannot be resolved. "
                + BrowserApiSurfacePolicy.TruncationNotice(truncation));
        }

        return BrowserSurfaceProjection.Require(
            implementationSurfaces.Assemblies.Assemblies.Single(),
            "Implementation surface").Surface;
    }

    static AssemblyContextMemberDeclaration ResolveSurfaceDeclaration(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate,
        string assemblyName,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        PackageCompileAsset surfaceAsset = coordinate.CompileAsset(assemblyName);
        BrowserWorkspaceParticipant participant =
            scope.SurfaceParticipant(coordinate, surfaceAsset);
        return scope.UseSurfaceParticipant(
            participant,
            (group, selected) => ResolveDeclaration(
                group,
                selected,
                typeId,
                memberName,
                selectorKey,
                metadataToken,
                "Surface declaration"));
    }

    static AssemblyContextMemberDeclaration ResolveImplementationDeclaration(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate,
        string assemblyName,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        PackageCompileAsset surfaceAsset = coordinate.CompileAsset(assemblyName);
        BrowserWorkspaceParticipant surfaceParticipant =
            scope.SurfaceParticipant(coordinate, surfaceAsset);
        BrowserWorkspaceParticipant participant =
            scope.ImplementationParticipant(surfaceParticipant);
        return scope.UseImplementationParticipant(
            participant,
            (group, selected) => ResolveDeclaration(
                group,
                selected,
                typeId,
                memberName,
                selectorKey,
                metadataToken,
                "Implementation declaration"));
    }

    static AssemblyContextMemberDeclaration ResolveDeclaration(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string operation) =>
        RequireSelection(
            AssemblyContextMemberSelectionQuery.ExecuteDeclaration(
                group,
                participant,
                new(
                    typeIdentity,
                    memberName,
                    selectorKey,
                    metadataToken == 0 ? null : metadataToken),
                BrowserApiSurfacePolicy.Limits),
            operation);

    internal static AssemblyContextMemberBody ResolveImplementationMember(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken) =>
        RequireSelection(
            AssemblyContextMemberSelectionQuery.ExecuteBody(
                group,
                participant,
                new(
                    typeId,
                    memberName,
                    selectorKey,
                    metadataToken == 0 ? null : metadataToken),
                BrowserApiSurfacePolicy.Limits),
            "Implementation member selection");

    internal static AssemblyContextMemberBody ResolveImplementationMember(
        ApiSurface implementation,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken) =>
        ApiSurfaceMemberSelection.SelectBody(
            implementation,
            new(
                typeId,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken));

    internal static TValue RequireSelection<TValue>(
        AssemblyContextEntry<TValue> entry,
        string operation)
    {
        if (entry is AssemblyContextEntry<TValue>.Available available)
            return available.Value;
        if (entry is AssemblyContextEntry<TValue>.Failed
            {
                Error: InvalidOperationException error,
            })
        {
            throw new InvalidOperationException(
                $"{operation} failed: {error.Message}",
                error);
        }

        return BrowserSurfaceProjection.Require(entry, operation);
    }
}
