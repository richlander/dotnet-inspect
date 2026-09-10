using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
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

    internal static async Task<ScopedResolution> ImplementationMethodAsync(
        string packageId,
        string version,
        string targetFramework,
        AssemblyReferenceIdentity assemblyIdentity,
        Guid moduleVersionId,
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
            BrowserWorkspaceParticipant[] exactParticipants =
            [
                .. scope.ImplementationParticipants.Where(participant =>
                    ReferenceEquals(
                        participant.Coordinate.Root.Identity,
                        coordinate.Root.Identity)
                    && participant.Assembly.Identity.IsEquivalentTo(
                        assemblyIdentity)
                    && participant.Assembly.Registration.ModuleVersionId
                        == moduleVersionId),
            ];
            BrowserWorkspaceParticipant implementationParticipant =
                exactParticipants.Length switch
                {
                    1 => exactParticipants[0],
                    0 => throw new InvalidOperationException(
                        $"Assembly '{assemblyIdentity.Name}' with MVID "
                        + $"'{moduleVersionId:D}' is no longer available in "
                        + "the selected package workspace."),
                    _ => throw new InvalidOperationException(
                        $"Assembly '{assemblyIdentity.Name}' with MVID "
                        + $"'{moduleVersionId:D}' is ambiguous in the "
                        + "selected package workspace."),
                };
            BrowserWorkspaceParticipant surfaceParticipant =
                scope.TryGetSurfaceParticipant(implementationParticipant)
                ?? implementationParticipant;
            Analysis.CallGraphMemberResolution resolution =
                scope.UseImplementationParticipant(
                    implementationParticipant,
                    (group, participant) =>
                    {
                        MetadataMethodAddress address =
                            AssemblyContextMethodAddressQuery
                                .ExecuteParticipant(
                                    group,
                                    participant,
                                    metadataToken)
                            switch
                            {
                                AssemblyContextEntry<MetadataMethodAddress>
                                    .Available available =>
                                        available.Value,
                                AssemblyContextEntry<MetadataMethodAddress>
                                    .Rejected rejected =>
                                        throw new InvalidOperationException(
                                            $"{rejected.Failure.Kind}: "
                                            + rejected.Failure.Detail),
                                AssemblyContextEntry<MetadataMethodAddress>
                                    .Failed failed =>
                                        throw failed.Error,
                                _ => throw new InvalidOperationException(
                                    "The method address query returned an "
                                    + "unsupported outcome."),
                            };
                        if (address.ModuleVersionId != moduleVersionId)
                        {
                            throw new InvalidOperationException(
                                "The Clone candidate module is no longer "
                                + "available at its original identity.");
                        }
                        return Analysis.CallGraphMemberResolver
                            .ResolveMethodDefinition(
                                ImplementationSurface(
                                    group,
                                    participant),
                                metadataToken)
                            ?? throw new InvalidOperationException(
                                $"MethodDef 0x{metadataToken:X8} is not "
                                + "available in the selected implementation "
                                + "surface.");
                    });
            return new ScopedResolution(
                lease,
                surfaceParticipant,
                implementationParticipant,
                resolution);
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
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
