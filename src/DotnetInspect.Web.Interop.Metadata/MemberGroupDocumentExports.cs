using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryMemberGroupDocument),
    typeof(BrowserMemberGroupDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformMemberGroupDocument),
    typeof(BrowserMemberGroupDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryMemberGroupDocument),
    typeof(BrowserMemberGroupDocumentInspection))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryMemberGroupDocument(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string accessibility = "public",
        string receiver = "all",
        bool includeHidden = false)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework).ConfigureAwait(false);
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
        BrowserMemberGroupDocumentInspection inspection =
            ProjectMemberDocumentResolution(
                await ExecuteMemberDocumentResolutionAsync(
                    scope.UseSurfaceParticipant(
                        participant,
                        (group, member) =>
                            AssemblyContextLibraryAdapter.MaterializeAsync(
                                group,
                                member,
                                AssemblyContextLibraryRole.ApiOnly,
                                BrowserExactMemberPolicy
                                    .MaterializationLimits,
                                CancellationToken.None)),
                    typeIdentity,
                    memberName,
                    accessibility,
                    receiver,
                    includeHidden)
                .ConfigureAwait(false));
        return SerializeMemberGroupDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryPlatformMemberGroupDocument(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string memberName,
        string accessibility = "public",
        string receiver = "all",
        bool includeHidden = false)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    CancellationToken.None)
                .ConfigureAwait(false);
        BrowserMemberGroupDocumentInspection inspection =
            ProjectMemberDocumentResolution(
                await ExecuteMemberDocumentResolutionAsync(
                    resolution.Scope.UseParticipant(
                        resolution.Participant,
                        (group, member) =>
                            AssemblyContextLibraryAdapter.MaterializeAsync(
                                group,
                                member,
                                AssemblyContextLibraryRole.Implementation,
                                BrowserExactMemberPolicy
                                    .MaterializationLimits,
                                CancellationToken.None)),
                    typeIdentity,
                    memberName,
                    accessibility,
                    receiver,
                    includeHidden)
                .ConfigureAwait(false));
        return SerializeMemberGroupDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryUploadedLibraryMemberGroupDocument(
        string declaredName,
        byte[] content,
        string typeIdentity,
        string memberName,
        string accessibility = "public",
        string receiver = "all",
        bool includeHidden = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        BrowserMemberGroupDocumentInspection inspection =
            ProjectMemberDocumentResolution(
                await ExecuteMemberDocumentResolutionAsync(
                    EmbeddedLibraryInspection.MaterializeAsync(
                        declaredName,
                        ImmutableArray.CreateRange(content),
                        AssemblyContextLibraryRole.Implementation,
                        BrowserExactMemberPolicy
                            .MaterializationLimits),
                    typeIdentity,
                    memberName,
                    accessibility,
                    receiver,
                    includeHidden)
                .ConfigureAwait(false));
        return SerializeMemberGroupDocument(inspection);
    }

    private static async Task<
        InspectionEnvelope<MemberDocumentResolutionOutcome>>
        ExecuteMemberDocumentResolutionAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string memberName,
            string accessibility,
            string receiver,
            bool includeHidden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        MetadataTypeDefinitionName type =
            BrowserExactMemberPolicy.ParseTypeIdentity(
                typeIdentity);
        var plan = new MemberDocumentResolutionPlan(
            new MemberGroupSubject(type, memberName),
            BrowserExactMemberPolicy.Bounds,
            accessibility: ParseGroupAccessibility(accessibility),
            receiver: ParseGroupReceiver(receiver),
            includeHidden: includeHidden);
        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<MemberDocumentResolutionOutcome>> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                        materialization,
                        (reference, owner) =>
                            AssemblyContextLibraryInspection.ExecuteOperation(
                                reference,
                                owner,
                                lease =>
                                    MemberDocumentResolutionOperation.Execute(
                                        new(reference, plan),
                                        lease)))
                    .ConfigureAwait(false);
        if (run.Failure is { } failure)
        {
            throw new InvalidOperationException(
                $"The member-group Library could not be materialized: {failure}");
        }
        if (run.Result is not { } inspection)
        {
            throw new InvalidOperationException(
                "The exact Library owner could not issue the member-group inspection lease.");
        }
        if (run.CleanupFailures.IsEmpty)
            return inspection;

        return new InspectionEnvelope<MemberDocumentResolutionOutcome>(
            inspection.Content,
            inspection.Share,
            inspection.Diagnostics.Concat(
                run.CleanupFailures.Select(static failure =>
                    new InspectionDiagnostic(
                        "member-group-document.library-retirement",
                        InspectionDiagnosticSeverity.Warning,
                        failure))));
    }

    private static BrowserMemberGroupDocumentInspection
        ProjectMemberDocumentResolution(
            InspectionEnvelope<MemberDocumentResolutionOutcome>
                inspection)
    {
        BrowserMemberGroupDocumentDiagnostic[] diagnostics =
        [
            .. inspection.Diagnostics.Select(static diagnostic =>
                new BrowserMemberGroupDocumentDiagnostic(
                    diagnostic.Code,
                    diagnostic.Severity.ToString(),
                    diagnostic.Summary.ToString(),
                    diagnostic.Correspondence?.ToString())),
        ];
        return inspection.Content switch
        {
            MemberDocumentResolutionOutcome.Overview overview =>
                ProjectAvailable(overview.Document, diagnostics),
            MemberDocumentResolutionOutcome.Exact exact =>
                ProjectAvailable(exact.Document, diagnostics),
            MemberDocumentResolutionOutcome.Rejected rejected =>
                new(
                    BrowserMemberGroupDocumentOutcome.Rejected,
                    $"The member group was rejected ({rejected.Reason}).",
                    null,
                    diagnostics),
            MemberDocumentResolutionOutcome.Incomplete incomplete =>
                new(
                    BrowserMemberGroupDocumentOutcome.Incomplete,
                    $"The member group reached {incomplete.Bound} "
                        + $"({incomplete.Measured} > {incomplete.Limit}).",
                    null,
                    diagnostics),
            MemberDocumentResolutionOutcome.Failed failed =>
                new(
                    BrowserMemberGroupDocumentOutcome.Failed,
                    $"The member group failed ({failed.Reason}).",
                    null,
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Member document resolution outcome."),
        };
    }

    private static BrowserMemberGroupDocumentInspection ProjectAvailable(
        MemberOverviewDocument document,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics)
        => ProjectAvailable(
            document.Subject,
            document.Members,
            diagnostics);

    private static BrowserMemberGroupDocumentInspection ProjectAvailable(
        MemberDocument document,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics)
        => ProjectAvailable(
            document.Subject.Group,
            [document],
            diagnostics);

    private static BrowserMemberGroupDocumentInspection ProjectAvailable(
        MemberGroupSubject subject,
        ImmutableArray<MemberDeclaration> members,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics)
    {
        return new(
            BrowserMemberGroupDocumentOutcome.Available,
            null,
            new(
                subject.DeclaringType.ToEscapedFullName(),
                subject.Name,
                members.Length,
                [
                    .. members.Select(static member =>
                        new BrowserMemberGroupDocumentRow(
                            member.Subject.MetadataToken,
                            member.Subject.BaselineOrdinal,
                            member.DisplaySignature.ToString(),
                            member.Accessibility.ToString(),
                            member.Receiver.ToString())),
                ]),
            diagnostics);
    }

    private static string SerializeMemberGroupDocument(
        BrowserMemberGroupDocumentInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserMemberGroupDocumentInspection);

    private static MemberOverloadAccessibilityFilter
        ParseGroupAccessibility(string accessibility) =>
        accessibility.Trim().ToLowerInvariant() switch
        {
            "public" => MemberOverloadAccessibilityFilter.Public,
            "protected" => MemberOverloadAccessibilityFilter.Protected,
            "internal" => MemberOverloadAccessibilityFilter.Internal,
            "private" => MemberOverloadAccessibilityFilter.Private,
            "all" => MemberOverloadAccessibilityFilter.All,
            _ => throw new ArgumentException(
                "Unknown MemberGroup accessibility.", nameof(accessibility)),
        };

    private static MemberOverloadReceiverFilter ParseGroupReceiver(
        string receiver) =>
        receiver.Trim().ToLowerInvariant() switch
        {
            "all" => MemberOverloadReceiverFilter.All,
            "static" => MemberOverloadReceiverFilter.Static,
            "this" => MemberOverloadReceiverFilter.This,
            "extension" => MemberOverloadReceiverFilter.Extension,
            "nonextension" => MemberOverloadReceiverFilter.NonExtension,
            _ => throw new ArgumentException(
                "Unknown MemberGroup receiver.", nameof(receiver)),
        };
}
