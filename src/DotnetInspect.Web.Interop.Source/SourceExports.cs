using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using CSharpText;
using DotnetInspector.Libraries;
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
        string documentFingerprint,
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
            documentFingerprint,
            styleOptionsJson,
            includeParts: true);
        return JsonSerializer.Serialize(
            source,
            BrowserSourceJsonContext.Default.BrowserMemberSource);
    }

    [JSExport]
    public static async Task<string> QueryPlatformMemberSource(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string documentFingerprint,
        string styleOptionsJson,
        string? contextId = null)
    {
        BrowserMemberSource source =
            await QueryPlatformMemberSourceCore(
                targetFramework,
                platformVersion,
                assemblyName,
                pack,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken,
                documentFingerprint,
                styleOptionsJson,
                contextId);
        return JsonSerializer.Serialize(
            source,
            BrowserSourceJsonContext.Default.BrowserMemberSource);
    }

    static async Task<BrowserMemberSource> QueryPlatformMemberSourceCore(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string documentFingerprint,
        string styleOptionsJson,
        string? contextId)
    {
        if (!string.IsNullOrWhiteSpace(documentFingerprint))
        {
            return await QueryPlatformMemberDocumentSourceCore(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    typeIdentity,
                    memberName,
                    documentFingerprint,
                    styleOptionsJson,
                    includeParts: true,
                    contextId)
                .ConfigureAwait(false);
        }

        using BrowserSourceOperationLease operation =
            await BrowserSourceOperationCoordinator.BeginAsync();
        await using BrowserMemberResolution.ScopedPlatformResolution resolved =
            await BrowserMemberResolution.PlatformImplementationMemberAsync(
                targetFramework,
                platformVersion,
                assemblyName,
                pack,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken,
                contextId,
                operation.CancellationToken);
        AssemblyMemberSourceRequest request = MemberSourceRequest(
            resolved.Member,
            typeIdentity,
            memberName,
            styleOptionsJson,
            includeParts: true);
        InspectionEnvelope<AssemblyMemberSourceEntry> inspection =
            await resolved.Scope.UseParticipant(
                resolved.Participant,
                (group, participant) => MemberSourceInspection.ExecuteAsync(
                    group,
                    participant,
                    request,
                    BrowserSourceQueryContext.Create(),
                    operation.CancellationToken));
        BrowserMemberSource source = AdaptMember(
            inspection.Content,
            resolved.Participant,
            includeParts: true);
        return source;
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
        BrowserManagedOperationResult<BrowserTypeCodeView, string, string>
            result = await ExecuteTypeSourceOperationAsync(
                operationId,
                async token =>
                    (await QueryTypeSourceCore(
                        packageId,
                        version,
                        targetFramework,
                        assemblyName,
                        typeIdentity,
                        styleOptionsJson,
                        view,
                        requestEvidence: false,
                        cancellationToken: token)).View);
        return JsonSerializer.Serialize(
            BrowserTypeSourceResult.From(result),
            BrowserSourceJsonContext.Default.BrowserTypeSourceResult);
    }

    [JSExport]
    public static async Task<string> QueryPlatformTypeSource(
        string operationId,
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string styleOptionsJson,
        string view = "source",
        string? contextId = null)
    {
        BrowserManagedOperationResult<BrowserTypeCodeView, string, string>
            result = await ExecuteTypeSourceOperationAsync(
                operationId,
                token => QueryPlatformTypeSourceCore(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    typeIdentity,
                    styleOptionsJson,
                    view,
                    contextId,
                    token));
        return JsonSerializer.Serialize(
            BrowserTypeSourceResult.From(result),
            BrowserSourceJsonContext.Default.BrowserTypeSourceResult);
    }

    static async Task<
        BrowserManagedOperationResult<
            BrowserTypeCodeView,
            string,
            string>> ExecuteTypeSourceOperationAsync(
        string operationId,
        Func<CancellationToken, Task<BrowserTypeCodeView>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
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
                            await query(token));
                    }
                    catch (TypeSourceUnavailableException error)
                    {
                        return new BrowserManagedOperationBodyResult<BrowserTypeCodeView, string, string>.Failed(
                            error.Message, error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));
        return result;
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
        bool decompilerOnly = view == "decompiler-source";
        if (view != "source" && !decompilerOnly)
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
            if (decompilerOnly)
            {
                InspectionEnvelope<AssemblyTypeDecompilationEntry>
                    decompilationInspection =
                    await scope.UseImplementationParticipant(
                        participant,
                        (group, member) =>
                            TypeSourceInspection.DecompileAsync(
                                group,
                                member,
                                request,
                                BrowserSourceQueryContext.Create(),
                                cancellationToken: cancellationToken));
                return new(
                    new BrowserTypeCodeView.Source(
                        Adapt(decompilationInspection.Content, participant),
                        decompilationInspection.Share,
                        decompilationInspection.Diagnostics),
                    Evidence: null);
            }

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
                                Context:
                                    BrowserSourceQueryContext.Create(),
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
                        BrowserTypeSourcePdbAcquisitionEvidence.From(
                            evidence.Evidence)));
        }
    }

    static async Task<BrowserTypeCodeView> QueryPlatformTypeSourceCore(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string styleOptionsJson,
        string view,
        string? contextId,
        CancellationToken cancellationToken)
    {
        if (view is "api-declarations" or "all-declarations")
        {
            return await QueryPlatformTypeApiDeclarationsCore(
                targetFramework,
                platformVersion,
                assemblyName,
                pack,
                typeIdentity,
                view == "all-declarations"
                    ? TypeApiDeclarationScope.All
                    : TypeApiDeclarationScope.ApiVisible,
                contextId,
                cancellationToken);
        }
        bool decompilerOnly = view == "decompiler-source";
        if (view != "source" && !decompilerOnly)
            throw new ArgumentException(
                $"Unknown type code view '{view}'.",
                nameof(view));

        await using BrowserMemberResolution.ScopedPlatformTypeResolution resolved =
            await BrowserMemberResolution.PlatformImplementationTypeAsync(
                targetFramework,
                platformVersion,
                assemblyName,
                pack,
                typeIdentity,
                contextId,
                cancellationToken);
        var request = AssemblyTypeSourceRequest.From(
            resolved.Type,
            BrowserStyleOptions.Resolve(styleOptionsJson));
        if (decompilerOnly)
        {
            InspectionEnvelope<AssemblyTypeDecompilationEntry> inspection =
                await resolved.Scope.UseParticipant(
                    resolved.Participant,
                    (group, member) =>
                        TypeSourceInspection.DecompileAsync(
                            group,
                            member,
                            request,
                            BrowserSourceQueryContext.Create(),
                            cancellationToken: cancellationToken));
            return new BrowserTypeCodeView.Source(
                Adapt(inspection.Content, resolved.Participant),
                inspection.Share,
                inspection.Diagnostics);
        }

        InspectionEnvelope<AssemblyTypeSourceEntry> sourceInspection =
            await resolved.Scope.UseParticipant(
                resolved.Participant,
                (group, member) =>
                    TypeSourceInspection.ExecuteWithPdbLatencyHedgeAsync(
                        group,
                        member,
                        request,
                        BrowserSourceQueryContext.Create(),
                        BrowserTypeSourcePdbLatencyHedge,
                        cancellationToken));
        return new BrowserTypeCodeView.Source(
            Adapt(sourceInspection.Content, resolved.Participant),
            sourceInspection.Share,
            sourceInspection.Diagnostics);
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

    static async Task<BrowserTypeCodeView> QueryPlatformTypeApiDeclarationsCore(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        TypeApiDeclarationScope declarationScope,
        string? contextId,
        CancellationToken cancellationToken)
    {
        if (MetadataTypeDefinitionName.ParseSerialized(typeIdentity)
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            throw new TypeSourceUnavailableException(
                $"'{typeIdentity}' is not an exact metadata type identity.");
        }

        await using BrowserMemberResolution.ScopedPlatformTypeResolution resolved =
            await BrowserMemberResolution.PlatformImplementationTypeAsync(
                targetFramework,
                platformVersion,
                assemblyName,
                pack,
                typeIdentity,
                contextId,
                cancellationToken);
        InspectionEnvelope<TypeApiDeclarationResult> inspection =
            resolved.Scope.UseParticipant(
                resolved.Participant,
                (group, member) => TypeApiDeclarationInspection.Execute(
                    group,
                    member,
                    valid.Name,
                    declarationScope,
                    BrowserApiSurfacePolicy.Limits,
                    cancellationToken));
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
            documentFingerprint: "",
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
        string documentFingerprint,
        string styleOptionsJson,
        bool includeParts)
    {
        if (!string.IsNullOrWhiteSpace(documentFingerprint))
        {
            return await QueryPackageMemberDocumentSourceCore(
                    packageId,
                    version,
                    targetFramework,
                    assemblyName,
                    typeIdentity,
                    memberName,
                    documentFingerprint,
                    styleOptionsJson,
                    includeParts)
                .ConfigureAwait(false);
        }

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
        AssemblyMemberSourceRequest request = MemberSourceRequest(
            resolution,
            typeIdentity,
            memberName,
            styleOptionsJson,
            includeParts);
        InspectionEnvelope<AssemblyMemberSourceEntry> inspection =
            await scope.UseImplementationParticipant(
                participant,
                (group, member) => MemberSourceInspection.ExecuteAsync(
                    group,
                    member,
                    request,
                    BrowserSourceQueryContext.Create(),
                    operation.CancellationToken));

        return AdaptMember(inspection.Content, participant, includeParts);
    }

    static async Task<BrowserMemberSource>
        QueryPackageMemberDocumentSourceCore(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeIdentity,
            string memberName,
            string fingerprintPrefix,
            string styleOptionsJson,
            bool includeParts)
    {
        using BrowserSourceOperationLease operation =
            await BrowserSourceOperationCoordinator.BeginAsync();
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId,
                    version,
                    targetFramework,
                    operation.CancellationToken)
                .ConfigureAwait(false);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        if (!coordinate.Selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"{packageId} {version} has no selected compile Library "
                    + $"({coordinate.Selection.Status}).");
        }
        BrowserWorkspaceParticipant surface =
            scope.SurfaceParticipant(
                coordinate,
                coordinate.CompileAsset(assemblyName));
        BrowserWorkspaceParticipant implementation =
            scope.ImplementationParticipant(surface);
        var plan = new MemberDocumentInspectionPlan(
            new MemberGroupSubject(
                BrowserExactMemberPolicy.ParseTypeIdentity(
                    typeIdentity),
                memberName),
            new(fingerprintPrefix: fingerprintPrefix),
            BrowserExactMemberPolicy.Bounds,
            source:
                new(
                    BrowserStyleOptions.Resolve(
                        styleOptionsJson),
                    includeParts,
                    allowDecompiledFallback: true));
        InspectionEnvelope<MemberDocumentInspectionOutcome>
            inspection =
                await BrowserMemberDocumentExecution.ExecuteAsync(
                        scope.UseImplementationParticipant(
                            implementation,
                            (group, member) =>
                                AssemblyContextLibraryAdapter
                                    .MaterializeAsync(
                                        group,
                                        member,
                                        AssemblyContextLibraryRole
                                            .Implementation,
                                        BrowserExactMemberPolicy
                                            .MaterializationLimits,
                                        operation.CancellationToken)),
                        plan,
                        sourceProvider:
                            async (request, token) =>
                                (await scope
                                    .UseImplementationParticipant(
                                        implementation,
                                        (group, member) =>
                                            MemberSourceInspection
                                                .ExecuteAsync(
                                                    group,
                                                    member,
                                                    request,
                                                    BrowserSourceQueryContext
                                                        .Create(),
                                                    token))
                                    .ConfigureAwait(false))
                                .Content,
                        cancellationToken:
                            operation.CancellationToken)
                    .ConfigureAwait(false);
        return AdaptAttachedMemberSource(
            inspection.Content,
            implementation,
            includeParts);
    }

    static async Task<BrowserMemberSource>
        QueryPlatformMemberDocumentSourceCore(
            string targetFramework,
            string platformVersion,
            string assemblyName,
            string pack,
            string typeIdentity,
            string memberName,
            string fingerprintPrefix,
            string styleOptionsJson,
            bool includeParts,
            string? contextId)
    {
        using BrowserSourceOperationLease operation =
            await BrowserSourceOperationCoordinator.BeginAsync();
        await using BrowserPlatformScopeResolution resolution =
            await (contextId is null
                ? BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    operation.CancellationToken)
                : BrowserPlatformWorkspace
                    .OpenRetainedContextAssemblyAsync(
                        contextId,
                        targetFramework,
                        platformVersion,
                        assemblyName,
                        pack,
                        operation.CancellationToken))
                .ConfigureAwait(false);
        var plan = new MemberDocumentInspectionPlan(
            new MemberGroupSubject(
                BrowserExactMemberPolicy.ParseTypeIdentity(
                    typeIdentity),
                memberName),
            new(fingerprintPrefix: fingerprintPrefix),
            BrowserExactMemberPolicy.Bounds,
            source:
                new(
                    BrowserStyleOptions.Resolve(
                        styleOptionsJson),
                    includeParts,
                    allowDecompiledFallback: true));
        InspectionEnvelope<MemberDocumentInspectionOutcome>
            inspection =
                await BrowserMemberDocumentExecution.ExecuteAsync(
                        resolution.Scope.UseParticipant(
                            resolution.Participant,
                            (group, member) =>
                                AssemblyContextLibraryAdapter
                                    .MaterializeAsync(
                                        group,
                                        member,
                                        AssemblyContextLibraryRole
                                            .Implementation,
                                        BrowserExactMemberPolicy
                                            .MaterializationLimits,
                                        operation.CancellationToken)),
                        plan,
                        sourceProvider:
                            async (request, token) =>
                                (await resolution.Scope.UseParticipant(
                                        resolution.Participant,
                                        (group, member) =>
                                            MemberSourceInspection
                                                .ExecuteAsync(
                                                    group,
                                                    member,
                                                    request,
                                                    BrowserSourceQueryContext
                                                        .Create(),
                                                    token))
                                    .ConfigureAwait(false))
                                .Content,
                        cancellationToken:
                            operation.CancellationToken)
                    .ConfigureAwait(false);
        return AdaptAttachedMemberSource(
            inspection.Content,
            resolution.Participant,
            includeParts);
    }

    static BrowserMemberSource AdaptAttachedMemberSource(
        MemberDocumentInspectionOutcome outcome,
        BrowserWorkspaceParticipant participant,
        bool includeParts) =>
        AdaptMember(
            AttachedMemberSource(outcome),
            participant,
            includeParts);

    static BrowserMemberSource AdaptAttachedMemberSource(
        MemberDocumentInspectionOutcome outcome,
        WorkspaceContextMember participant,
        bool includeParts) =>
        AdaptMember(
            AttachedMemberSource(outcome),
            participant,
            includeParts);

    static AssemblyMemberSourceEntry AttachedMemberSource(
        MemberDocumentInspectionOutcome outcome) =>
        outcome switch
        {
            MemberDocumentInspectionOutcome.Available
                {
                    Document.Source.Outcome: { } source,
                } => source,
            MemberDocumentInspectionOutcome.Available =>
                throw new InvalidOperationException(
                    "The requested Member document source attachment "
                        + "was not produced."),
            MemberDocumentInspectionOutcome.Rejected rejected =>
                throw new InvalidOperationException(
                    $"The Member document source was rejected "
                        + $"({rejected.Reason})."),
            MemberDocumentInspectionOutcome.Incomplete incomplete =>
                throw new InvalidOperationException(
                    $"The Member document source reached "
                        + $"{incomplete.Bound} "
                        + $"({incomplete.Measured} > "
                        + $"{incomplete.Limit})."),
            MemberDocumentInspectionOutcome.Failed failed =>
                throw new InvalidOperationException(
                    $"The Member document source failed "
                        + $"({failed.Reason})."),
            _ => throw new InvalidOperationException(
                "Unknown Member document source outcome."),
        };

    static AssemblyMemberSourceRequest MemberSourceRequest(
        CallGraphMemberResolution resolution,
        string typeIdentity,
        string memberName,
        string styleOptionsJson,
        bool includeParts)
    {
        ApiMember selectedMember = resolution.Member;
        if (selectedMember.MetadataToken != resolution.BodyToken)
        {
            selectedMember = ApiMemberAccessors.Create(
                    selectedMember,
                    resolution.Type)
                .SingleOrDefault(
                    member => member.MetadataToken == resolution.BodyToken)
                ?? throw new InvalidOperationException(
                    $"Whole-member source for '{typeIdentity}.{memberName}' is unavailable because "
                    + "the selected body has no exact physical accessor projection.");
        }

        AssemblyMemberSourceRequest request = AssemblyMemberSourceRequest.From(
            resolution.Type,
            selectedMember,
            BrowserStyleOptions.Resolve(styleOptionsJson));
        return includeParts
            ? request.WithAuthoredParts(allowDecompiledFallback: true)
            : request;
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

    internal static AssemblyContextSourceQueryContext CreateSourceContext() =>
        BrowserSourceQueryContext.Create();

    internal static BrowserSource Adapt(
        AssemblyMemberSourceEntry result,
        BrowserWorkspaceParticipant participant) =>
        AdaptMember(result, participant, includeParts: false).Source;

    internal static BrowserMemberSource AdaptMember(
        AssemblyMemberSourceEntry result,
        BrowserWorkspaceParticipant participant,
        bool includeParts) =>
        AdaptMember(
            result,
            DecompiledProvenance(participant),
            includeParts);

    internal static BrowserMemberSource AdaptMember(
        AssemblyMemberSourceEntry result,
        WorkspaceContextMember participant,
        bool includeParts) =>
        AdaptMember(
            result,
            DecompiledProvenance(participant),
            includeParts);

    static BrowserMemberSource AdaptMember(
        AssemblyMemberSourceEntry result,
        InertString decompiledProvenance,
        bool includeParts) =>
        result switch
        {
            AssemblyMemberSourceEntry.Available available =>
                AdaptMember(
                    available.Source,
                    decompiledProvenance,
                    includeParts),
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
        Adapt(result, DecompiledProvenance(participant));

    internal static BrowserSource Adapt(
        AssemblyTypeSourceEntry result,
        WorkspaceContextMember participant) =>
        Adapt(result, DecompiledProvenance(participant));

    static BrowserSource Adapt(
        AssemblyTypeSourceEntry result,
        InertString decompiledProvenance) =>
        result switch
        {
            AssemblyTypeSourceEntry.Available available =>
                Adapt(available.Source, decompiledProvenance),
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

    internal static BrowserSource Adapt(
        AssemblyTypeDecompilationEntry result,
        BrowserWorkspaceParticipant participant) =>
        Adapt(result, DecompiledProvenance(participant));

    internal static BrowserSource Adapt(
        AssemblyTypeDecompilationEntry result,
        WorkspaceContextMember participant) =>
        Adapt(result, DecompiledProvenance(participant));

    static BrowserSource Adapt(
        AssemblyTypeDecompilationEntry result,
        InertString decompiledProvenance) =>
        result switch
        {
            AssemblyTypeDecompilationEntry.Settled
            {
                Attempt:
                {
                    Status: CSharpDecompilationStatus.Available,
                    Text: { } text,
                },
            } => new BrowserSource(
                "decompiled",
                decompiledProvenance,
                null,
                null,
                text),
            AssemblyTypeDecompilationEntry.Settled settled =>
                throw new TypeSourceUnavailableException(
                    "Decompiler source unavailable: "
                    + DecompilerAttemptReason(settled.Attempt)),
            AssemblyTypeDecompilationEntry.Rejected rejected =>
                throw new TypeSourceUnavailableException(
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}"),
            AssemblyTypeDecompilationEntry.Unavailable unavailable =>
                throw new TypeSourceUnavailableException(
                    SourceUnavailable(unavailable.Failure).Message,
                    unavailable.Failure.Error),
            _ => throw new InvalidOperationException(
                "Unknown assembly type decompilation result."),
        };

    sealed class TypeSourceUnavailableException(string message, Exception? inner = null)
        : InvalidOperationException(message, inner);

    static BrowserSource Adapt(
        AssemblyMemberSource source,
        InertString decompiledProvenance) =>
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
                decompiledProvenance,
                null,
                PdbSourceLimitation(decompiled.PdbAttempt.Lines),
                decompiled.Text),
            _ => throw new InvalidOperationException(
                "Unknown available member source result."),
        };

    internal static BrowserMemberSource AdaptMember(
        AssemblyMemberSource source,
        BrowserWorkspaceParticipant participant,
        bool includeParts) =>
        AdaptMember(
            source,
            DecompiledProvenance(participant),
            includeParts);

    internal static BrowserMemberSource AdaptMember(
        AssemblyMemberSource source,
        WorkspaceContextMember participant,
        bool includeParts) =>
        AdaptMember(
            source,
            DecompiledProvenance(participant),
            includeParts);

    static BrowserMemberSource AdaptMember(
        AssemblyMemberSource source,
        InertString decompiledProvenance,
        bool includeParts)
    {
        BrowserSource browserSource = Adapt(source, decompiledProvenance);
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
        Adapt(source, DecompiledProvenance(participant));

    static BrowserSource Adapt(
        AssemblyTypeSource source,
        InertString decompiledProvenance) =>
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
                decompiledProvenance,
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

    static InertString DecompiledProvenance(
        WorkspaceContextMember participant) =>
        participant.Realized is RealizedMemberCoordinate.Platform platform
            ? new InertString(
                TextPolicy.Field,
                $"dotnet-inspect from {platform.Family} "
                + $"{platform.Version} {platform.Assembly ?? "platform"}")
            : throw new InvalidOperationException(
                "Platform member source requires a realized platform coordinate.");

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

    static string DecompilerAttemptReason(
        CSharpDecompilationAttempt attempt) =>
        attempt.DiagnosticSummary is { Length: > 0 } detail
            ? detail
            : $"decompilation ended with status {attempt.Status}";

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
