using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

using InspectWeb.Engine;

[SupportedOSPlatform("browser")]
public static partial class BrowserInspectionEngine
{
    const string PlatformPackageName = "Microsoft.NETCore.App";

    [JSExport]
    public static async Task<string> LoadRuntimePack(
        string targetFramework)
    {
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                targetFramework);
        return ProjectPlatformSurface(resolution);
    }

    [JSExport]
    public static async Task<string> LoadRuntimePackAssembly(
        string targetFramework,
        string assemblyFileName,
        string pack)
    {
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                assemblyFileName,
                pack);
        return ProjectPlatformSurface(resolution);
    }

    [JSExport]
    public static async Task<string> QueryPlatformIntegrations(
        string targetFramework,
        string assemblyFileName,
        string pack)
    {
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                assemblyFileName,
                pack);
        AssemblyIntegrationsEntry result =
            resolution.Scope.UseParticipant(
                resolution.Participant,
                AssemblyContextIntegrationsQuery.ExecuteParticipant);
        return SerializeIntegrations(
            PlatformPackageName,
            resolution.Coordinate.Version,
            resolution.Scope.Framework,
            [result]);
    }

    [JSExport]
    public static async Task<string> QueryPlatformOpportunities(
        string targetFramework,
        string assemblyFileName,
        string pack)
    {
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                assemblyFileName,
                pack);
        AssemblyIntegrationOpportunitiesEntry result =
            resolution.Scope.UseParticipant(
                resolution.Participant,
                AssemblyContextIntegrationOpportunitiesQuery
                    .ExecuteParticipant);
        return SerializeOpportunities(
            PlatformPackageName,
            resolution.Coordinate.Version,
            resolution.Scope.Framework,
            [result]);
    }

    [JSExport]
    public static async Task<string> ExpandPlatformCallGraph(
        string targetFramework,
        string assembly,
        string pack,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                assembly.EndsWith(
                    ".dll",
                    StringComparison.OrdinalIgnoreCase)
                    ? assembly
                    : $"{assembly}.dll",
                pack);
        AssemblyContextApiSurfaceResult implementation =
            resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, participant) =>
                    AssemblyContextApiSurfaceQuery.ExecuteBounded(
                        group,
                        ApiSurfaceScope.IncludeAll,
                        BrowserApiSurfacePolicy.Limits,
                        [participant]));
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
        Analysis.CallGraphMemberResolution member =
            Analysis.CallGraphMemberResolver.ResolveDefinitionIdentity(
                surface.Surface,
                typeFullName,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken)
            ?? throw new InvalidOperationException(
                $"The implementation of '{typeFullName}.{memberName}' does not "
                + "contain the selected API body.");

        MemberCallGraphView view = resolution.Scope.Use(group =>
        {
            using var session = new MemberCallGraphSession(
                group,
                resolution.Participant.Participant.Assembly,
                member.BodyToken);
            return session.HasCrossLibraryScope
                ? session.CrossLibrary()
                : session.Callers();
        });
        CallGraphProjection projection = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);
        int callerAssemblies = resolution.Scope.Members
            .Select(candidate =>
                candidate.Participant.Assembly.Identity.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return JsonSerializer.Serialize(
            new BrowserCallGraph(
                Mermaid(projection),
                Tree(view.CallerRoot),
                Tree(view.CalleeRoot),
                new BrowserCallGraphScope(
                    Packages: 0,
                    resolution.Scope.Members.Length,
                    callerAssemblies,
                    view.Tier.ToString()),
                Targets(
                    projection.Nodes,
                    resolution.Scope.Members.Select(candidate =>
                        candidate.Participant.Assembly.Identity),
                    resolution.Scope.PlatformPackForAssembly),
                Diagnostics(
                    view.Diagnostics,
                    projection.HasUnexploredTraversalBoundary,
                    projection.HasAnalysisFailureBoundary),
                NoBody:
                    view.CalleeRoot is null
                    && view.CallerRoot is null),
            BrowserJsonContext.Default.BrowserCallGraph);
    }

    internal static string ProjectPlatformSurface(
        BrowserPlatformScopeResolution resolution)
    {
        WorkspaceContextMember participant = resolution.Participant;
        string assembly = participant.Participant.Assembly.Identity.Name;
        AssemblyContextApiSurfaceResult surfaces =
            resolution.Scope.UseParticipant(
                participant,
                (group, selected) =>
                    AssemblyContextApiSurfaceQuery.ExecuteBounded(
                        group,
                        ApiSurfaceScope.PublicWithNonPublicTypes,
                        BrowserApiSurfacePolicy.Limits,
                        [selected]));
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                surfaces,
                [
                    new BrowserSurfaceProjection.Participant(
                        participant.Participant,
                        assembly,
                        assembly,
                        $"{assembly}.dll"),
                ],
                qualifyTypeIds: true,
                platformPack:
                    BrowserPlatformWorkspace.Pack(
                        resolution.Coordinate.Family));
        if (projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"Platform assembly '{assembly}' produced no API surface. "
                + (projected.InspectionError
                    ?? "The workspace reported no failure."));
        }

        return JsonSerializer.Serialize(
            new BrowserPackageSurface(
                PlatformPackageName,
                resolution.Coordinate.Version,
                [resolution.Scope.Framework],
                resolution.Scope.Framework,
                assembly,
                projected.Assemblies,
                projected.Types,
                projected.Accessibility,
                projected.TotalMembers,
                Documents: [],
                projected.InspectionError),
            BrowserJsonContext.Default.BrowserPackageSurface);
    }
}
