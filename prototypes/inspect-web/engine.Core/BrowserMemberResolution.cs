using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace InspectWeb.Engine;

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

    internal sealed record ScopedResolution(
        BrowserInspectionScope Scope,
        BrowserWorkspaceParticipant SurfaceParticipant,
        BrowserWorkspaceParticipant ImplementationParticipant,
        Analysis.CallGraphMemberResolution Member);

    /// <summary>
    /// Resolves one exact package/version/framework coordinate, reuses its workspace, and returns
    /// the reference-preferred participant for one product-selected compile asset.
    /// </summary>
    internal static async Task<(
        BrowserInspectionScope Scope,
        BrowserWorkspaceParticipant Participant)> SurfaceParticipantAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];

        return (scope, scope.SurfaceParticipant(
            coordinate,
            coordinate.CompileAsset(assemblyName)));
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
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
            scope,
            resolved.SurfaceParticipant,
            resolved.ImplementationParticipant,
            resolved.Member);
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
        // One participant, under the same browser bounds as the package load: a body selector is
        // resolved against the implementation surface, and an over-budget implementation must
        // fail visibly rather than resolve against a silently shortened surface.
        AssemblyContextApiSurfaceResult implementationSurfaces =
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    group,
                    ApiSurfaceScope.IncludeAll,
                    BrowserApiSurfacePolicy.Limits,
                    [member]));
        if (implementationSurfaces.Truncation is { } truncation)
        {
            throw new InvalidOperationException(
                $"The implementation surface for '{typeId}' exceeds the browser projection "
                + $"bounds, so the selected body cannot be resolved. "
                + BrowserApiSurfacePolicy.TruncationNotice(truncation));
        }

        AssemblyApiSurface implementation = BrowserSurfaceProjection.Require(
            implementationSurfaces.Assemblies.Assemblies.Single(),
            $"Implementation surface for '{typeId}'");
        Analysis.CallGraphMemberResolution resolution =
            Analysis.CallGraphMemberResolver.ResolveDefinitionIdentity(
                implementation.Surface,
                typeId,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken)
            ?? throw new InvalidOperationException(
                $"The implementation of '{typeId}.{memberName}' does not contain the selected "
                + "API body.");
        return new Resolved(surfaceParticipant, participant, resolution);
    }
}
