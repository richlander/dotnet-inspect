using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using InspectWeb.Engine;

[SupportedOSPlatform("browser")]
public static partial class BrowserInspectionEngine
{
    [JSExport]
    public static Task<string> QueryMemberSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson) =>
        QueryMemberSourceCore(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken,
            styleOptionsJson);

    [JSExport]
    public static async Task<string> QueryTypeSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string styleOptionsJson)
    {
        (
            BrowserInspectionScope scope,
            BrowserWorkspaceParticipant participant,
            bool implementation,
            ApiType type
        ) = await SourceTypeAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity);
        var request = AssemblyTypeSourceRequest.From(
            type,
            BrowserStyleOptions.Resolve(styleOptionsJson));
        AssemblyTypeSourceEntry result = await UseSourceParticipant(
            scope,
            participant,
            implementation,
            (group, member) => AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                member,
                request,
                CreateSourceContext()));

        return JsonSerializer.Serialize(
            Adapt(result, participant),
            BrowserJsonContext.Default.BrowserSource);
    }

    [JSExport]
    public static Task<string> QueryTypeMemberSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson) =>
        QueryMemberSourceCore(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken,
            styleOptionsJson);

    static async Task<string> QueryMemberSourceCore(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson)
    {
        (
            BrowserInspectionScope scope,
            BrowserWorkspaceParticipant participant,
            CallGraphMemberResolution resolution
        ) = await ImplementationMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken);
        if (resolution.Member.MetadataToken != resolution.BodyToken)
        {
            throw new InvalidOperationException(
                $"Whole-member source for '{typeIdentity}.{memberName}' is unavailable because "
                + "the selected body is an accessor rather than a method definition.");
        }

        var request = AssemblyMemberSourceRequest.From(
            resolution.Type,
            resolution.Member,
            BrowserStyleOptions.Resolve(styleOptionsJson));
        AssemblyMemberSourceEntry result =
            await scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextSourceQuery.ExecuteMemberAsync(
                    group,
                    member,
                    request,
                    CreateSourceContext()));

        return JsonSerializer.Serialize(
            Adapt(result, participant),
            BrowserJsonContext.Default.BrowserSource);
    }

    static async Task<(
        BrowserInspectionScope Scope,
        BrowserWorkspaceParticipant Participant,
        bool Implementation,
        ApiType Type)> SourceTypeAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeIdentity)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        PackageCompileAsset surfaceAsset = coordinate.CompileAsset(assemblyName);
        BrowserWorkspaceParticipant surfaceParticipant =
            scope.SurfaceParticipant(coordinate, surfaceAsset);
        bool implementation =
            coordinate.Selection.FindImplementationAsset(surfaceAsset) is not null;
        BrowserWorkspaceParticipant participant = implementation
            ? scope.ImplementationParticipant(surfaceParticipant)
            : surfaceParticipant;

        AssemblyContextApiSurfaceResult projected = UseSourceParticipant(
            scope,
            participant,
            implementation,
            (group, member) => AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.IncludeAll,
                BrowserApiSurfacePolicy.Limits,
                [member]));
        if (projected.Truncation is { } truncation)
        {
            throw new InvalidOperationException(
                $"The source surface for '{typeIdentity}' exceeds the browser projection bounds. "
                + BrowserApiSurfacePolicy.TruncationNotice(truncation));
        }

        AssemblyApiSurface surface = BrowserSurfaceProjection.Require(
            projected.Assemblies.Assemblies.Single(),
            $"Source surface for '{typeIdentity}'");
        ApiType[] matches =
        [
            .. surface.Surface.Types
                .Where(candidate =>
                    candidate.DefinitionName?.ToEscapedFullName()
                        .Equals(typeIdentity, StringComparison.Ordinal) == true)
                .Take(2),
        ];
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"The selected participant does not contain one exact type '{typeIdentity}'.");
        }

        return (scope, participant, implementation, matches[0]);
    }

    static TResult UseSourceParticipant<TResult>(
        BrowserInspectionScope scope,
        BrowserWorkspaceParticipant participant,
        bool implementation,
        Func<AssemblyContextGroup, AssemblyContextParticipant, TResult> query) =>
        implementation
            ? scope.UseImplementationParticipant(participant, query)
            : scope.UseSurfaceParticipant(participant, query);

    internal static AssemblyContextSourceQueryContext CreateSourceContext()
    {
        var sourceStore = new InMemorySourceContentStore();
        return new AssemblyContextSourceQueryContext(
            BrowserPackageWorkspace.NetworkClient,
            new InMemoryPdbStore(),
            BrowserPackageWorkspace.PackageSourceAuthorization,
            new SourceFetcher(
                BrowserPackageWorkspace.NetworkClient,
                sourceStore));
    }

    internal static BrowserSource Adapt(
        AssemblyMemberSourceEntry result,
        BrowserWorkspaceParticipant participant) =>
        result switch
        {
            AssemblyMemberSourceEntry.Available available =>
                Adapt(available.Source, participant),
            AssemblyMemberSourceEntry.Rejected rejected =>
                throw new InvalidOperationException(
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}"),
            AssemblyMemberSourceEntry.Unavailable unavailable =>
                throw SourceUnavailable(unavailable.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly member source result."),
        };

    internal static BrowserSource Adapt(
        AssemblyTypeSourceEntry result,
        BrowserWorkspaceParticipant participant) =>
        result switch
        {
            AssemblyTypeSourceEntry.Available available =>
                Adapt(available.Source, participant),
            AssemblyTypeSourceEntry.Rejected rejected =>
                throw new InvalidOperationException(
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}"),
            AssemblyTypeSourceEntry.Unavailable unavailable =>
                throw SourceUnavailable(unavailable.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly type source result."),
        };

    static BrowserSource Adapt(
        AssemblyMemberSource source,
        BrowserWorkspaceParticipant participant) =>
        source switch
        {
            AssemblyMemberSource.Authored authored => new BrowserSource(
                "original",
                AuthoredProvenance(authored.Provenance),
                authored.Inspection.Document?.ResolvedUrl,
                authored.Text),
            AssemblyMemberSource.Decompiled decompiled => new BrowserSource(
                "decompiled",
                DecompiledProvenance(participant),
                null,
                decompiled.Text),
            _ => throw new InvalidOperationException(
                "Unknown available member source result."),
        };

    static BrowserSource Adapt(
        AssemblyTypeSource source,
        BrowserWorkspaceParticipant participant) =>
        source switch
        {
            AssemblyTypeSource.Authored authored => new BrowserSource(
                "original",
                AuthoredProvenance(authored.Provenance),
                authored.Inspection.Document?.ResolvedUrl,
                authored.Text),
            AssemblyTypeSource.Decompiled decompiled => new BrowserSource(
                "decompiled",
                DecompiledProvenance(participant),
                null,
                decompiled.Text),
            _ => throw new InvalidOperationException(
                "Unknown available type source result."),
        };

    static string AuthoredProvenance(
        AssemblyAuthoredSourceProvenance provenance)
    {
        if (provenance.RepositoryUrl is { Length: > 0 } repository
            && provenance.Revision is { Length: > 0 } revision)
        {
            return $"Checksum-verified SourceLink source from {repository} at {revision}";
        }
        if (provenance.RepositoryUrl is { Length: > 0 } repositoryOnly)
            return $"Checksum-verified SourceLink source from {repositoryOnly}";
        if (provenance.Revision is { Length: > 0 } revisionOnly)
            return $"Checksum-verified SourceLink source at {revisionOnly}";
        return "Checksum-verified SourceLink source";
    }

    static string DecompiledProvenance(
        BrowserWorkspaceParticipant participant) =>
        $"dotnet-inspect from {participant.Coordinate.PackageId} "
        + $"{participant.Coordinate.Version} {participant.Asset.Path}";

    internal static InvalidOperationException SourceUnavailable(
        AssemblySourceFailure failure) =>
        new(
            $"{failure.Kind}: {failure.Detail}",
            failure.Error);
}
