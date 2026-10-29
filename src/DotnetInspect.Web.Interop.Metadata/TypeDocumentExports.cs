using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryTypeDocument),
    typeof(BrowserTypeDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformTypeDocument),
    typeof(BrowserTypeDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryTypeDocument),
    typeof(BrowserTypeDocumentInspection))]
public static partial class MetadataExports
{
    private const string MembersContent = "members";
    private const string DocumentedMembersContent =
        "documented-members";

    public static Task<string> QueryTypeDocument(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string spelling,
        string accessibility,
        string content) =>
        QueryTypeDocument(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            spelling,
            accessibility,
            content,
            runtimeIdentifier: null);

    [JSExport]
    public static async Task<string> QueryTypeDocument(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string spelling,
        string accessibility,
        string content,
        string? runtimeIdentifier)
    {
        bool documented = IsDocumentedContent(content);
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework,
                runtimeIdentifier).ConfigureAwait(false);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        if (!coordinate.Selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"{packageId} {version} has no selected compile Library "
                    + $"({coordinate.Selection.Status}).");
        }
        BrowserWorkspaceParticipant participant =
            scope.SurfaceParticipant(
                coordinate,
                coordinate.CompileAsset(assemblyName));
        ValueTask<AssemblyContextLibraryAdapterResult> materialization =
            scope.UseSurfaceParticipant(
                participant,
                (group, member) =>
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        member,
                        AssemblyContextLibraryRole.ApiOnly,
                        BrowserExactMemberPolicy.MaterializationLimits,
                        CancellationToken.None));
        BrowserTypeDocumentInspection inspection =
            documented
                ? await ExecuteDocumentedTypeDocumentAsync(
                        materialization,
                        typeIdentity,
                        spelling,
                        accessibility,
                        new(
                            DocumentationDemand
                                .CompiledXmlAndAuthoredSourceDocumentation),
                        async (
                            documentationIds,
                            demand,
                            cancellationToken) =>
                        {
                            if (demand
                                != DocumentationDemand
                                    .CompiledXmlAndAuthoredSourceDocumentation)
                            {
                                throw new InvalidOperationException(
                                    "Package Type documents require compiled "
                                        + "and authored documentation demand.");
                            }
                            var outcomes = new Dictionary<
                                string,
                                DocumentationQueryOutcome>(
                                    documentationIds.Count,
                                    StringComparer.Ordinal);
                            foreach (string documentationId
                                in documentationIds)
                            {
                                outcomes.Add(
                                    documentationId,
                                    await BrowserPackageWorkspace
                                        .QueryMemberDocumentationAsync(
                                            packageId,
                                            version,
                                            targetFramework,
                                            assemblyName,
                                            documentationId,
                                            runtimeIdentifier,
                                            cancellationToken)
                                        .ConfigureAwait(false));
                            }
                            return outcomes;
                        })
                    .ConfigureAwait(false)
                : await ExecuteTypeDocumentAsync(
                        materialization,
                        typeIdentity,
                        spelling,
                        accessibility)
                    .ConfigureAwait(false);
        return SerializeTypeDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryPlatformTypeDocument(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string spelling,
        string accessibility,
        string content)
    {
        bool documented = IsDocumentedContent(content);
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    CancellationToken.None)
                .ConfigureAwait(false);
        ValueTask<AssemblyContextLibraryAdapterResult> materialization =
            resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, member) =>
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        member,
                        AssemblyContextLibraryRole.Implementation,
                        BrowserExactMemberPolicy.MaterializationLimits,
                        CancellationToken.None));
        BrowserTypeDocumentInspection inspection =
            documented
                ? await ExecuteDocumentedTypeDocumentAsync(
                        materialization,
                        typeIdentity,
                        spelling,
                        accessibility,
                        new(DocumentationDemand.CompiledXml),
                        async (
                            documentationIds,
                            demand,
                            cancellationToken) =>
                        {
                            if (demand != DocumentationDemand.CompiledXml)
                            {
                                throw new InvalidOperationException(
                                    "Platform Type documents require compiled "
                                        + "XML documentation demand.");
                            }
                            var outcomes = new Dictionary<
                                string,
                                DocumentationQueryOutcome>(
                                    documentationIds.Count,
                                    StringComparer.Ordinal);
                            foreach (string documentationId
                                in documentationIds)
                            {
                                outcomes.Add(
                                    documentationId,
                                    await BrowserPlatformWorkspace
                                        .QueryUnifiedMemberDocumentationAsync(
                                            targetFramework,
                                            platformVersion,
                                            assemblyName,
                                            pack,
                                            documentationId,
                                            cancellationToken)
                                        .ConfigureAwait(false));
                            }
                            return outcomes;
                        })
                    .ConfigureAwait(false)
                : await ExecuteTypeDocumentAsync(
                        materialization,
                        typeIdentity,
                        spelling,
                        accessibility)
                    .ConfigureAwait(false);
        return SerializeTypeDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryUploadedLibraryTypeDocument(
        string declaredName,
        byte[] contentBytes,
        string typeIdentity,
        string spelling,
        string accessibility,
        string content)
    {
        ArgumentNullException.ThrowIfNull(contentBytes);
        bool documented = IsDocumentedContent(content);
        ValueTask<AssemblyContextLibraryAdapterResult> materialization =
            EmbeddedLibraryInspection.MaterializeAsync(
                declaredName,
                ImmutableArray.CreateRange(contentBytes),
                AssemblyContextLibraryRole.Implementation,
                BrowserExactMemberPolicy.MaterializationLimits);
        BrowserTypeDocumentInspection inspection =
            await ExecuteTypeDocumentAsync(
                    materialization,
                    typeIdentity,
                    spelling,
                    accessibility,
                    documented
                        ? new(
                            BrowserTypeDocumentDocumentationStatus.Unavailable,
                            "Compiled documentation is unavailable for "
                                + "uploaded Libraries.")
                        : null)
                .ConfigureAwait(false);
        return SerializeTypeDocument(inspection);
    }

    private static bool IsDocumentedContent(string content) =>
        content.Trim().ToLowerInvariant() switch
        {
            MembersContent => false,
            DocumentedMembersContent => true,
            _ => throw new ArgumentException(
                $"Content must be '{MembersContent}' or "
                    + $"'{DocumentedMembersContent}'.",
                nameof(content)),
        };

    private static TypeDocumentInspectionPlan TypeDocumentPlan(
        string typeIdentity,
        string spelling,
        string accessibility) =>
        new(
            BrowserExactMemberPolicy.ParseTypeIdentity(typeIdentity),
            BrowserExactMemberPolicy.Bounds,
            ParseOverviewSpelling(spelling),
            ParseOverviewAccessibility(accessibility));

    private static async Task<BrowserTypeDocumentInspection>
        ExecuteTypeDocumentAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string spelling,
            string accessibility,
            BrowserTypeDocumentDocumentation? documentation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        InspectionEnvelope<TypeDocumentInspectionOutcome> envelope =
            await BrowserTypeDocumentExecution.ExecuteAsync(
                    materialization,
                    TypeDocumentPlan(
                        typeIdentity,
                        spelling,
                        accessibility))
                .ConfigureAwait(false);
        return ProjectTypeDocument(
            envelope.Content,
            documentation
                ?? new(
                    BrowserTypeDocumentDocumentationStatus.NotRequested,
                    null),
            attachments: null,
            envelope.Share,
            ProjectTypeDocumentDiagnostics(envelope.Diagnostics));
    }

    private static async Task<BrowserTypeDocumentInspection>
        ExecuteDocumentedTypeDocumentAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string spelling,
            string accessibility,
            MemberDocumentationAttachmentRequest documentation,
            MemberDocumentationAttachmentProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        InspectionEnvelope<TypeDocumentAttachmentCompositionResult> envelope =
            await BrowserTypeDocumentExecution.ExecuteDocumentedAsync(
                    materialization,
                    TypeDocumentPlan(
                        typeIdentity,
                        spelling,
                        accessibility),
                    documentation,
                    provider)
                .ConfigureAwait(false);
        MemberDocumentationAttachmentOutcome? outcome =
            envelope.Content.Documentation;
        return ProjectTypeDocument(
            envelope.Content.Inspection,
            outcome switch
            {
                MemberDocumentationAttachmentOutcome.Available =>
                    new(
                        BrowserTypeDocumentDocumentationStatus.Available,
                        null),
                MemberDocumentationAttachmentOutcome.Failed failed =>
                    new(
                        BrowserTypeDocumentDocumentationStatus.Failed,
                        $"Documentation attachment failed ({failed.Reason})."),
                null => new(
                    BrowserTypeDocumentDocumentationStatus.Failed,
                    "Documentation attachment did not settle."),
                _ => throw new InvalidOperationException(
                    "Unknown Member documentation attachment outcome."),
            },
            (outcome as MemberDocumentationAttachmentOutcome.Available)
                ?.Attachments.ToDictionary(
                    static attachment => attachment.Subject),
            envelope.Share,
            ProjectTypeDocumentDiagnostics(envelope.Diagnostics));
    }

    private static BrowserTypeDocumentInspection ProjectTypeDocument(
        TypeDocumentInspectionOutcome outcome,
        BrowserTypeDocumentDocumentation documentation,
        IReadOnlyDictionary<
            MemberSubject,
            MemberDocumentationAttachment>? attachments,
        InspectionShare share,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics) =>
        outcome switch
        {
            TypeDocumentInspectionOutcome.Available available =>
                new(
                    BrowserTypeDocumentOutcome.Available,
                    null,
                    ProjectAvailableTypeDocument(
                        available.Document,
                        documentation,
                        attachments),
                    share,
                    diagnostics),
            TypeDocumentInspectionOutcome.Rejected rejected =>
                new(
                    BrowserTypeDocumentOutcome.Rejected,
                    $"The Type document was rejected ({rejected.Reason}).",
                    null,
                    share,
                    diagnostics),
            TypeDocumentInspectionOutcome.Incomplete incomplete =>
                new(
                    BrowserTypeDocumentOutcome.Incomplete,
                    $"The Type document reached {incomplete.Bound} "
                        + $"({incomplete.Measured}/{incomplete.Limit}).",
                    null,
                    share,
                    diagnostics),
            TypeDocumentInspectionOutcome.Failed failed =>
                new(
                    BrowserTypeDocumentOutcome.Failed,
                    $"The Type document failed ({failed.Reason}).",
                    null,
                    share,
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Type document outcome."),
        };

    private static BrowserTypeDocument ProjectAvailableTypeDocument(
        TypeDocument document,
        BrowserTypeDocumentDocumentation documentation,
        IReadOnlyDictionary<
            MemberSubject,
            MemberDocumentationAttachment>? attachments) =>
        new(
            document.Subject.Type.ToEscapedFullName(),
            document.MemberGroups.Spelling.ToString(),
            document.MemberGroups.Accessibility.ToString(),
            documentation,
            [
                .. document.Members.Select(member =>
                {
                    string kind =
                        OverviewKind(member.Subject.Group.Category);
                    MemberDocumentationAttachment? attachment = null;
                    if (attachments is not null)
                    {
                        attachments.TryGetValue(
                            member.Subject,
                            out attachment);
                    }
                    return new BrowserTypeDocumentMember(
                        $"{kind}:{member.Subject.Group.Name}",
                        member.Subject.Group.Name,
                        kind,
                        member.Subject.MetadataToken,
                        member.Subject.BaselineOrdinal,
                        member.DisplaySignature.ToString(),
                        member.CanonicalSignature.ToString(),
                        member.Subject.Anchor.StableSelector,
                        member.Subject.Fingerprint.ToString(),
                        member.Subject.DocumentationId.ToString(),
                        member.Accessibility.ToString(),
                        member.Receiver.ToString(),
                        attachment is null
                            ? null
                            : BrowserDocumentationWireProjection.Project(
                                attachment.Outcome));
                }),
            ]);

    private static BrowserMemberGroupDocumentDiagnostic[]
        ProjectTypeDocumentDiagnostics(
            IEnumerable<InspectionDiagnostic> diagnostics) =>
    [
        .. diagnostics.Select(static diagnostic =>
            new BrowserMemberGroupDocumentDiagnostic(
                diagnostic.Code,
                diagnostic.Severity.ToString(),
                diagnostic.Summary.ToString(),
                diagnostic.Correspondence?.ToString())),
    ];

    private static string SerializeTypeDocument(
        BrowserTypeDocumentInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserTypeDocumentInspection);
}
