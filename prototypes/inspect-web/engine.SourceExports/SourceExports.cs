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
using InspectWeb.Engine.SourceFacade;

[SupportedOSPlatform("browser")]
public static partial class SourceExports
{
    const long MiB = 1024L * 1024;
    static readonly SymbolAcquisitionLimits SourceSymbolLimits =
        new(
            maxSymbolPackageBytes: 24 * MiB,
            maxPortablePdbBytes: 8 * MiB,
            maxSymbolPackageEntries: 2048,
            maxExpandedPdbBytes: 24 * MiB);

    [JSExport]
    public static void CancelSourceQuery() =>
        BrowserSourceOperationCoordinator.CancelCurrent();

    [JSExport]
    public static async Task<string> QueryMemberSource(
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
        BrowserSource source = await QueryMemberSourceCore(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken,
            styleOptionsJson);
        return JsonSerializer.Serialize(
            source,
            BrowserSourceJsonContext.Default.BrowserSource);
    }

    [JSExport]
    public static async Task<string> QueryTypeSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string styleOptionsJson)
    {
        using BrowserSourceOperationLease operation =
            await BrowserSourceOperationCoordinator.BeginAsync();
        (
            BrowserScopeLease<BrowserInspectionScope> scopeLease,
            BrowserWorkspaceParticipant participant,
            ApiType type
        ) = await SourceTypeAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            operation.CancellationToken);
        using (scopeLease)
        {
            BrowserInspectionScope scope = scopeLease.Scope;
            var request = AssemblyTypeSourceRequest.From(
                type,
                BrowserStyleOptions.Resolve(styleOptionsJson));
            AssemblyTypeSourceEntry result = await scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    member,
                    request,
                    CreateSourceContext(),
                    operation.CancellationToken));

            return JsonSerializer.Serialize(
                Adapt(result, participant),
                BrowserSourceJsonContext.Default.BrowserSource);
        }
    }

    [JSExport]
    public static async Task<string> QueryTypeMemberSource(
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
        BrowserSource source = await QueryMemberSourceCore(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken,
            styleOptionsJson);
        return JsonSerializer.Serialize(
            source,
            BrowserSourceJsonContext.Default.BrowserSource);
    }

    static async Task<BrowserSource> QueryMemberSourceCore(
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
        using BrowserSourceOperationLease operation =
            await BrowserSourceOperationCoordinator.BeginAsync();
        BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken,
                operation.CancellationToken);
        BrowserInspectionScope scope = resolved.Scope;
        BrowserWorkspaceParticipant participant = resolved.ImplementationParticipant;
        CallGraphMemberResolution resolution = resolved.Member;
        operation.CancellationToken.ThrowIfCancellationRequested();
        using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            BrowserPackageWorkspace.LeaseScope(scope);
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
                    CreateSourceContext(),
                    operation.CancellationToken));

        return Adapt(result, participant);
    }

    static async Task<(
        BrowserScopeLease<BrowserInspectionScope> ScopeLease,
        BrowserWorkspaceParticipant Participant,
        ApiType Type)> SourceTypeAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeIdentity,
            CancellationToken cancellationToken)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        BrowserScopeLease<BrowserInspectionScope> scopeLease =
            BrowserPackageWorkspace.LeaseScope(scope);
        try
        {
            BrowserPackageCoordinate coordinate = scope.Coordinates[0];
            PackageCompileAsset surfaceAsset = coordinate.CompileAsset(assemblyName);
            BrowserWorkspaceParticipant surfaceParticipant =
                scope.SurfaceParticipant(coordinate, surfaceAsset);
            _ = coordinate.ImplementationAsset(assemblyName);
            BrowserWorkspaceParticipant participant =
                scope.ImplementationParticipant(surfaceParticipant);

            AssemblyContextApiSurfaceResult projected =
                scope.UseImplementationParticipant(
                    participant,
                    (group, member) => AssemblyContextApiSurfaceQuery.ExecuteBounded(
                        group,
                        ApiSurfaceScope.IncludeAll,
                        BrowserApiSurfacePolicy.Limits,
                        [member]));
            cancellationToken.ThrowIfCancellationRequested();
            if (projected.Truncation is { } truncation)
            {
                throw new InvalidOperationException(
                    $"The source surface for '{typeIdentity}' exceeds the browser projection "
                    + "bounds. "
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

            return (scopeLease, participant, matches[0]);
        }
        catch
        {
            scopeLease.Dispose();
            throw;
        }
    }

    internal static AssemblyContextSourceQueryContext CreateSourceContext()
    {
        var sourceStore = new InMemorySourceContentStore();
        return new AssemblyContextSourceQueryContext(
            BrowserPackageWorkspace.NetworkClient,
            new InMemoryPdbStore(maxRetainedBytes: 24 * MiB),
            BrowserPackageWorkspace.PackageSourceAuthorization,
            new SourceFetcher(
                BrowserPackageWorkspace.NetworkClient,
                sourceStore,
                BrowserSourceFetchPolicy.Instance))
        {
            SymbolAcquisitionLimits = SourceSymbolLimits,
        };
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
                throw SourceUnavailable(
                    unavailable.Failure,
                    unavailable.PdbAttempt is { } pdb
                        ? PdbSourceLimitation(pdb.Lines)
                        : null),
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
                throw SourceUnavailable(
                    unavailable.Failure,
                    unavailable.PdbAttempt is { } pdb
                        ? PdbSourceLimitation(pdb.Lines)
                        : null),
            _ => throw new InvalidOperationException(
                "Unknown assembly type source result."),
        };

    static BrowserSource Adapt(
        AssemblyMemberSource source,
        BrowserWorkspaceParticipant participant) =>
        source switch
        {
            AssemblyMemberSource.Pdb pdb => new BrowserSource(
                "pdb",
                PdbSourceProvenance(pdb.Provenance),
                pdb.Inspection.Document?.ResolvedUrl,
                null,
                pdb.Text),
            AssemblyMemberSource.Decompiled decompiled => new BrowserSource(
                "decompiled",
                DecompiledProvenance(participant),
                null,
                PdbSourceLimitation(decompiled.PdbAttempt.Lines),
                decompiled.Text),
            _ => throw new InvalidOperationException(
                "Unknown available member source result."),
        };

    static BrowserSource Adapt(
        AssemblyTypeSource source,
        BrowserWorkspaceParticipant participant) =>
        source switch
        {
            AssemblyTypeSource.Pdb pdb => new BrowserSource(
                "pdb",
                PdbSourceProvenance(pdb.Provenance),
                pdb.Inspection.Document?.ResolvedUrl,
                null,
                pdb.Text),
            AssemblyTypeSource.Decompiled decompiled => new BrowserSource(
                "decompiled",
                DecompiledProvenance(participant),
                null,
                PdbSourceLimitation(decompiled.PdbAttempt.Lines),
                decompiled.Text),
            _ => throw new InvalidOperationException(
                "Unknown available type source result."),
        };

    static string PdbSourceProvenance(
        AssemblyPdbSourceProvenance provenance)
    {
        if (provenance.RepositoryUrl is { Length: > 0 } repository
            && provenance.Revision is { Length: > 0 } revision)
        {
            return $"PDB-checksum-verified source fetched through SourceLink from {repository} at {revision}";
        }
        if (provenance.RepositoryUrl is { Length: > 0 } repositoryOnly)
            return $"PDB-checksum-verified source fetched through SourceLink from {repositoryOnly}";
        if (provenance.Revision is { Length: > 0 } revisionOnly)
            return $"PDB-checksum-verified source fetched through SourceLink at {revisionOnly}";
        return "PDB-checksum-verified source fetched through SourceLink";
    }

    static string DecompiledProvenance(
        BrowserWorkspaceParticipant participant) =>
        $"dotnet-inspect from {participant.Coordinate.PackageId} "
        + $"{participant.Coordinate.Version} {participant.Asset.Path}";

    static string? PdbSourceLimitation(
        ILInspector.Findings.FindingInspection<string> inspection) =>
        inspection.Value switch
        {
            ILInspector.Findings.FindingInspection<string>.Absent absent =>
                absent.Detail,
            ILInspector.Findings.FindingInspection<string>.Failed failed =>
                failed.Error.Reason,
            _ => null,
        };

    internal static InvalidOperationException SourceUnavailable(
        AssemblySourceFailure failure,
        string? pdbSourceLimitation = null) =>
        new(
            $"{failure.Kind}: {failure.Detail}"
            + (pdbSourceLimitation is { Length: > 0 }
                ? $" PDB source unavailable: {pdbSourceLimitation}"
                : ""),
            failure.Error);
}
