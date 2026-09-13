using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

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
        Analysis.CallGraphMemberResolution Member);

    internal sealed record DeclarationResolved(
        ApiType Type,
        ApiMember Member);

    /// <summary>
    /// One resolved member and the protected use of the workspace it was resolved in. The lease
    /// holds that workspace for the whole of the caller's query, including its asynchronous
    /// return, so the caller disposes the resolution rather than the scope.
    /// </summary>
    internal sealed record ScopedResolution(
        BrowserScopeLease<BrowserInspectionScope> Lease,
        BrowserWorkspaceParticipant SurfaceParticipant,
        BrowserWorkspaceParticipant ImplementationParticipant,
        Analysis.CallGraphMemberResolution Member) : IAsyncDisposable
    {
        internal BrowserInspectionScope Scope => Lease.Scope;

        public ValueTask DisposeAsync() => Lease.DisposeAsync();
    }

    internal sealed record ScopedDeclarationResolution(
        BrowserScopeLease<BrowserInspectionScope> Lease,
        DeclarationResolved Member) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Lease.DisposeAsync();
    }

    internal sealed record ScopedPlatformDeclarationResolution(
        BrowserPlatformScopeResolution Resolution,
        DeclarationResolved Member) : IAsyncDisposable
    {
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
            DeclarationResolved member = implementationMember
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
            DeclarationResolved member = resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, selected) => ResolveDeclaration(
                    ParticipantSurface(group, selected, "platform"),
                    typeId,
                    memberName,
                    selectorKey,
                    metadataToken));
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
        Analysis.CallGraphMemberResolution resolution = scope.UseImplementationParticipant(
            participant,
            (group, member) => ResolveImplementationMember(
                ImplementationSurface(group, member), typeId, memberName, selectorKey, metadataToken));
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

    static DeclarationResolved ResolveSurfaceDeclaration(
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
        ApiSurface surface = scope.UseSurfaceParticipant(
            participant,
            (group, selected) => ParticipantSurface(group, selected, "surface"));
        return ResolveDeclaration(
            surface,
            typeId,
            memberName,
            selectorKey,
            metadataToken);
    }

    static DeclarationResolved ResolveImplementationDeclaration(
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
                ImplementationSurface(group, selected),
                typeId,
                memberName,
                selectorKey,
                metadataToken));
    }

    static DeclarationResolved ResolveDeclaration(
        ApiSurface surface,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        ApiType[] typeMatches =
        [
            .. surface.Types.Where(candidate =>
                candidate.DefinitionName?.ToEscapedFullName()
                    .Equals(typeIdentity, StringComparison.Ordinal) == true),
        ];
        if (typeMatches.Length != 1)
        {
            throw new InvalidOperationException(
                $"The selected surface does not contain one exact type identity "
                + $"for '{typeIdentity}'.");
        }

        ApiType type = typeMatches[0];
        ApiMember[] named =
        [
            .. type.Members.Where(candidate =>
                candidate.Name.Equals(memberName, StringComparison.Ordinal)),
        ];
        if (metadataToken != 0)
        {
            ApiMember[] tokenMatches =
            [
                .. named.Where(candidate =>
                    (candidate.DeclarationMetadataToken
                        ?? candidate.MetadataToken) == metadataToken),
            ];
            if (tokenMatches.Length == 1)
                return new DeclarationResolved(type, tokenMatches[0]);
            if (tokenMatches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"The selected declaration token for "
                    + $"'{typeIdentity}.{memberName}' is ambiguous.");
            }
        }

        ApiMember[] selectorMatches =
        [
            .. named.Where(candidate =>
                Analysis.CallGraphMemberResolver.CreateSelector(type, candidate)
                    .Key.Equals(selectorKey, StringComparison.Ordinal)),
        ];
        return selectorMatches.Length == 1
            ? new DeclarationResolved(type, selectorMatches[0])
            : throw new InvalidOperationException(
                $"The surface of '{typeIdentity}.{memberName}' does not contain "
                + "one exact selected API declaration.");
    }

    static ApiSurface ParticipantSurface(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string role)
    {
        AssemblyContextApiSurfaceResult surfaces =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.IncludeAll,
                BrowserApiSurfacePolicy.Limits,
                [participant]);
        if (surfaces.Truncation is { } truncation)
        {
            throw new InvalidOperationException(
                $"The {role} surface exceeds the browser projection bounds, so "
                + "the selected declaration cannot be resolved. "
                + BrowserApiSurfacePolicy.TruncationNotice(truncation));
        }

        return BrowserSurfaceProjection.Require(
            surfaces.Assemblies.Assemblies.Single(),
            $"{role} surface").Surface;
    }

    internal static Analysis.CallGraphMemberResolution ResolveImplementationMember(
        ApiSurface implementation,
        string typeId,
        string memberName,
        string selectorKey,
        int metadataToken) =>
            Analysis.CallGraphMemberResolver.ResolveDefinitionIdentity(
                implementation,
                typeId,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken)
            ?? throw new InvalidOperationException(
                $"The implementation of '{typeId}.{memberName}' does not contain the selected "
                + "API body.");
}
