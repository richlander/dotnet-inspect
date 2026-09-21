using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using CSharpText;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using InertText;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Source;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
public static partial class SourceExports
{
    static readonly BrowserManagedOperationBridge TypeSourceOperations = new();
    internal static TypeSourcePdbLatencyHedge BrowserTypeSourcePdbLatencyHedge { get; } =
        new(
            portablePdbPreferenceWindow: TimeSpan.FromSeconds(1));
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
    public static string CancelTypeSourceQuery(string operationId, string reason)
    {
        BrowserTypeSourceCancellation result = BrowserTypeSourceCancellation.From(
            TypeSourceOperations.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                BrowserTypeSourceCancellation.ParseReason(reason)));
        return JsonSerializer.Serialize(
            result,
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation);
    }

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
        BrowserMemberSource source = await QueryMemberSourceCore(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken,
            styleOptionsJson,
            includeParts: true);
        return JsonSerializer.Serialize(
            source,
            BrowserSourceJsonContext.Default.BrowserMemberSource);
    }

    [JSExport]
    public static async Task<string> QueryTypeSource(
        string operationId,
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string styleOptionsJson,
        string view = "source")
    {
        BrowserManagedOperationId id = BrowserManagedOperationId.From(operationId);
        BrowserManagedOperationResult<BrowserTypeCodeView, string, string> result =
            await TypeSourceOperations.RunAsync<BrowserTypeCodeView, string, string, object>(
                id,
                eventCallback: null,
                async (token, _) =>
                {
                    using BrowserSourceOperationLease operation =
                        await BrowserSourceOperationCoordinator.BeginAsync(
                            token,
                            reason => TypeSourceOperations.RequestCancellation(id, reason));
                    try
                    {
                        return new BrowserManagedOperationBodyResult<BrowserTypeCodeView, string, string>.Succeeded(
                            (await QueryTypeSourceCore(
                                packageId, version, targetFramework, assemblyName,
                                typeIdentity, styleOptionsJson, view,
                                requestEvidence: false,
                                cancellationToken: token)).View);
                    }
                    catch (TypeSourceUnavailableException error)
                    {
                        return new BrowserManagedOperationBodyResult<BrowserTypeCodeView, string, string>.Failed(
                            error.Message, error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));
        return JsonSerializer.Serialize(
            BrowserTypeSourceResult.From(result),
            BrowserSourceJsonContext.Default.BrowserTypeSourceResult);
    }

#if DEBUG
    [JSExport]
    public static async Task<string> QueryTypeSourceEvidence(
        string operationId,
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string styleOptionsJson)
    {
        BrowserManagedOperationId id =
            BrowserManagedOperationId.From(operationId);
        BrowserManagedOperationResult<
            BrowserTypeSourceEvidenceAttachment,
            string,
            string> result =
            await TypeSourceOperations.RunAsync<
                BrowserTypeSourceEvidenceAttachment,
                string,
                string,
                object>(
                id,
                eventCallback: null,
                async (token, _) =>
                {
                    using BrowserSourceOperationLease operation =
                        await BrowserSourceOperationCoordinator
                            .BeginAsync(
                                token,
                                reason =>
                                    TypeSourceOperations
                                        .RequestCancellation(
                                            id,
                                            reason));
                    try
                    {
                        BrowserTypeSourceExecution execution =
                            await QueryTypeSourceCore(
                                packageId,
                                version,
                                targetFramework,
                                assemblyName,
                                typeIdentity,
                                styleOptionsJson,
                                view: "source",
                                requestEvidence: true,
                                cancellationToken: token);
                        return new BrowserManagedOperationBodyResult<
                            BrowserTypeSourceEvidenceAttachment,
                            string,
                            string>.Succeeded(
                                execution.Evidence
                                ?? throw new InvalidOperationException(
                                    "Debug Type Source evidence was not captured."));
                    }
                    catch (TypeSourceUnavailableException error)
                    {
                        return new BrowserManagedOperationBodyResult<
                            BrowserTypeSourceEvidenceAttachment,
                            string,
                            string>.Failed(
                                error.Message,
                                error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));
        return JsonSerializer.Serialize(
            BrowserTypeSourceEvidenceResult.From(result),
            BrowserSourceJsonContext.Default
                .BrowserTypeSourceEvidenceResult);
    }
#endif

    static async Task<BrowserTypeSourceExecution> QueryTypeSourceCore(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string styleOptionsJson,
        string view,
        bool requestEvidence,
        CancellationToken cancellationToken)
    {
        if (view is "api-declarations" or "all-declarations")
        {
            return new(
                await QueryTypeApiDeclarationsCore(
                    packageId, version, targetFramework, assemblyName,
                    typeIdentity,
                    view == "all-declarations"
                        ? TypeApiDeclarationScope.All
                        : TypeApiDeclarationScope.ApiVisible,
                    cancellationToken),
                Evidence: null);
        }
        if (view != "source")
            throw new ArgumentException($"Unknown type code view '{view}'.", nameof(view));

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
            cancellationToken);
        await using (scopeLease)
        {
            BrowserInspectionScope scope = scopeLease.Scope;
            var request = AssemblyTypeSourceRequest.From(
                type,
                BrowserStyleOptions.Resolve(styleOptionsJson));
            var builder =
                new EvidenceInspectionBuilder<
                    AssemblyTypeSourceEntry,
                    TypeSourcePdbAcquisitionEvidence>();
            builder.RequestEvidence(requestEvidence);
            (
                InspectionEnvelope<AssemblyTypeSourceEntry> inspection,
                EvidenceInspectionEnvelope<
                    AssemblyTypeSourceEntry,
                    TypeSourcePdbAcquisitionEvidence>? evidence) =
                await scope.UseImplementationParticipant(
                    participant,
                    (group, member) =>
                        builder.BuildAsync(
                            (
                                Group: group,
                                Participant: member,
                                Request: request,
                                Context: CreateSourceContext(),
                                Hedge:
                                    BrowserTypeSourcePdbLatencyHedge),
                            static (state, token) =>
                                new ValueTask<
                                    InspectionEnvelope<
                                        AssemblyTypeSourceEntry>>(
                                    TypeSourceInspection
                                        .ExecuteWithPdbLatencyHedgeAsync(
                                            state.Group,
                                            state.Participant,
                                            state.Request,
                                            state.Context,
                                            state.Hedge,
                                            token)),
                            static (state, token) =>
                                new ValueTask<
                                    EvidenceInspectionEnvelope<
                                        AssemblyTypeSourceEntry,
                                        TypeSourcePdbAcquisitionEvidence>>(
                                    TypeSourceInspection
                                        .ExecuteWithPdbLatencyHedgeAndEvidenceAsync(
                                            state.Group,
                                            state.Participant,
                                            state.Request,
                                            state.Context,
                                            state.Hedge,
                                            token)),
                            cancellationToken));

            var browserInspection =
                new BrowserTypeCodeView.Source(
                    Adapt(
                        inspection.Content,
                        participant),
                    inspection.Share,
                    inspection.Diagnostics);
            return new(
                browserInspection,
                evidence is null
                    ? null
                    : new(
                        browserInspection,
                        evidence.Evidence));
        }
    }

    private sealed record BrowserTypeSourceExecution(
        BrowserTypeCodeView View,
        BrowserTypeSourceEvidenceAttachment? Evidence);

    static async Task<BrowserTypeCodeView> QueryTypeApiDeclarationsCore(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        TypeApiDeclarationScope declarationScope,
        CancellationToken cancellationToken)
    {
        if (MetadataTypeDefinitionName.ParseSerialized(typeIdentity)
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            throw new TypeSourceUnavailableException(
                $"'{typeIdentity}' is not an exact metadata type identity.");
        }

        await using BrowserScopeLease<BrowserInspectionScope> lease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId, version, targetFramework, cancellationToken);
        BrowserInspectionScope scope = lease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserWorkspaceParticipant participant = scope.SurfaceParticipant(
            coordinate, coordinate.CompileAsset(assemblyName));
        InspectionEnvelope<TypeApiDeclarationResult> inspection =
            scope.UseSurfaceParticipant(
                participant,
                (group, member) => TypeApiDeclarationInspection.Execute(
                    group, member, valid.Name, declarationScope,
                    BrowserApiSurfacePolicy.Limits, cancellationToken));
        return new BrowserTypeCodeView.ApiDeclarations(inspection);
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
        BrowserMemberSource source = await QueryMemberSourceCore(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken,
            styleOptionsJson,
            includeParts: false);
        return JsonSerializer.Serialize(
            source.Source,
            BrowserSourceJsonContext.Default.BrowserSource);
    }

    static async Task<BrowserMemberSource> QueryMemberSourceCore(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson,
        bool includeParts)
    {
        using BrowserSourceOperationLease operation =
            await BrowserSourceOperationCoordinator.BeginAsync();
        await using BrowserMemberResolution.ScopedResolution resolved =
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
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            BrowserPackageWorkspace.LeaseScope(scope);
        ApiMember selectedMember = resolution.Member;
        if (selectedMember.MetadataToken != resolution.BodyToken)
        {
            selectedMember = ApiMemberAccessors.Create(selectedMember, resolution.Type)
                .SingleOrDefault(member => member.MetadataToken == resolution.BodyToken)
                ?? throw new InvalidOperationException(
                    $"Whole-member source for '{typeIdentity}.{memberName}' is unavailable because "
                    + "the selected body has no exact physical accessor projection.");
        }

        AssemblyMemberSourceRequest request = AssemblyMemberSourceRequest.From(
            resolution.Type,
            selectedMember,
            BrowserStyleOptions.Resolve(styleOptionsJson));
        if (includeParts)
            request = request.WithAuthoredParts(allowDecompiledFallback: true);
        InspectionEnvelope<AssemblyMemberSourceEntry> inspection =
            await scope.UseImplementationParticipant(
                participant,
                (group, member) => MemberSourceInspection.ExecuteAsync(
                    group,
                    member,
                    request,
                    CreateSourceContext(),
                    operation.CancellationToken));

        return AdaptMember(inspection.Content, participant, includeParts);
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
        BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework,
                cancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                throw new TypeSourceUnavailableException(
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
                throw new TypeSourceUnavailableException(
                    $"The selected participant does not contain one exact type '{typeIdentity}'.");
            }

            return (scopeLease, participant, matches[0]);
        }
        catch
        {
            await scopeLease.DisposeAsync().ConfigureAwait(false);
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
            new SourceFetch(
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
        AdaptMember(result, participant, includeParts: false).Source;

    internal static BrowserMemberSource AdaptMember(
        AssemblyMemberSourceEntry result,
        BrowserWorkspaceParticipant participant,
        bool includeParts) =>
        result switch
        {
            AssemblyMemberSourceEntry.Available available =>
                AdaptMember(available.Source, participant, includeParts),
            AssemblyMemberSourceEntry.Rejected rejected =>
                throw new InvalidOperationException(
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}"),
            AssemblyMemberSourceEntry.Unavailable unavailable =>
                throw SourceUnavailable(
                    unavailable.Failure,
                    unavailable.PdbAttempt is { } pdb
                        ? PdbSourceLimitation(pdb.Lines)
                        : null,
                    unavailable.DecompiledAttempt is { IsAvailable: false } attempt
                            ? attempt.DiagnosticSummary
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
                throw new TypeSourceUnavailableException(
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}"),
            AssemblyTypeSourceEntry.Unavailable unavailable =>
                throw new TypeSourceUnavailableException(SourceUnavailable(
                    unavailable.Failure,
                    unavailable.PdbAttempt is { } pdb
                        ? PdbSourceLimitation(pdb.Lines)
                        : null,
                    unavailable.DecompiledAttempt is { IsAvailable: false } attempt
                        ? attempt.DiagnosticSummary
                        : null).Message, unavailable.Failure.Error),
            _ => throw new InvalidOperationException(
                "Unknown assembly type source result."),
        };

    sealed class TypeSourceUnavailableException(string message, Exception? inner = null)
        : InvalidOperationException(message, inner);

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

    internal static BrowserMemberSource AdaptMember(
        AssemblyMemberSource source,
        BrowserWorkspaceParticipant participant,
        bool includeParts)
    {
        BrowserSource browserSource = Adapt(source, participant);
        BrowserMemberSourcePart[] parts = includeParts
            && source is AssemblyMemberSource.Pdb
            {
                MemberDocument: { } document,
            }
                ? ProjectMemberParts(
                    document.Text,
                    document.Parts,
                    browserSource.Text.Length)
                : [];
        return new BrowserMemberSource(browserSource, parts);
    }

    internal static BrowserMemberSourcePart[] ProjectMemberParts(
        string documentText,
        MemberTextParts parts,
        int memberTextLength)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        ArgumentNullException.ThrowIfNull(parts);
        if (memberTextLength < 0)
            throw new ArgumentOutOfRangeException(nameof(memberTextLength));
        if (parts.Member.Length != memberTextLength)
        {
            throw new InvalidOperationException(
                "The authored member span does not match the transported member text.");
        }

        int memberStart = parts.Member.Start;
        BrowserMemberSourceSpan Rebase(MemberTextPart part)
        {
            int start = checked(part.Start - memberStart);
            if (start < 0 || part.End > parts.Member.End)
            {
                throw new InvalidOperationException(
                    "An authored member part falls outside the transported member text.");
            }

            return new BrowserMemberSourceSpan(
                start,
                part.Length,
                part.Lines.StartLine,
                part.Lines.EndLine,
                MemberSourcePartsProjection.GetLeadingIndentation(
                    documentText,
                    part));
        }

        return
        [
            .. MemberSourcePartsProjection.CreateCatalog(parts)
                .Select(part => new BrowserMemberSourcePart(
                    BrowserKind(part.Kind),
                    [.. part.Spans.Select(Rebase)])),
        ];

        static BrowserMemberSourcePartKind BrowserKind(
            MemberSourcePartKind kind) =>
            kind switch
            {
                MemberSourcePartKind.Member =>
                    BrowserMemberSourcePartKind.Member,
                MemberSourcePartKind.XmlDocs =>
                    BrowserMemberSourcePartKind.XmlDocumentation,
                MemberSourcePartKind.Attributes =>
                    BrowserMemberSourcePartKind.Attributes,
                MemberSourcePartKind.Signature =>
                    BrowserMemberSourcePartKind.Signature,
                MemberSourcePartKind.Body =>
                    BrowserMemberSourcePartKind.Body,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
    }

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

    static InertString PdbSourceProvenance(
        AssemblyPdbSourceProvenance provenance)
    {
        if (provenance.RepositoryUrl is { Length: > 0 } repository
            && provenance.Revision is { Length: > 0 } revision)
        {
            return new InertString(
                TextPolicy.Field,
                $"PDB-checksum-verified source fetched through SourceLink from {repository} at {revision}");
        }
        if (provenance.RepositoryUrl is { Length: > 0 } repositoryOnly)
        {
            return new InertString(
                TextPolicy.Field,
                $"PDB-checksum-verified source fetched through SourceLink from {repositoryOnly}");
        }
        if (provenance.Revision is { Length: > 0 } revisionOnly)
        {
            return new InertString(
                TextPolicy.Field,
                $"PDB-checksum-verified source fetched through SourceLink at {revisionOnly}");
        }
        return new InertString(
            TextPolicy.Field,
            "PDB-checksum-verified source fetched through SourceLink");
    }

    static InertString DecompiledProvenance(
        BrowserWorkspaceParticipant participant) =>
        PackageProvenance("dotnet-inspect from", participant);

    static InertString PackageProvenance(
        string prefix,
        BrowserWorkspaceParticipant participant) =>
        new(
            TextPolicy.Field,
            $"{prefix} {participant.Coordinate.PackageId} "
            + $"{participant.Coordinate.Version} {participant.Asset.Path}");

    static string? PdbSourceLimitation(
        Inspector.Findings.FindingInspection<string> inspection) =>
        inspection.Value switch
        {
            Inspector.Findings.FindingInspection<string>.Absent absent =>
                absent.Detail,
            Inspector.Findings.FindingInspection<string>.Failed failed =>
                failed.Error.Reason,
            _ => null,
        };

    internal static InvalidOperationException SourceUnavailable(
        AssemblySourceFailure failure,
        string? pdbSourceLimitation = null,
        string? decompiledSourceLimitation = null) =>
        new(
            $"{failure.Kind}: {failure.Detail}"
            + (pdbSourceLimitation is { Length: > 0 }
                ? $" PDB source unavailable: {pdbSourceLimitation}"
                : "")
            + (decompiledSourceLimitation is { Length: > 0 }
                ? $" Decompiled source unavailable: {decompiledSourceLimitation}"
                : ""),
            failure.Error);
}
