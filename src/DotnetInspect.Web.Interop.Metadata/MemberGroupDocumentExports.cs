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
        string memberName)
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
            ProjectMemberGroupDocument(
                await ExecuteMemberGroupDocumentAsync(
                    scope.UseSurfaceParticipant(
                        participant,
                        (group, member) =>
                            AssemblyContextLibraryAdapter.MaterializeAsync(
                                group,
                                member,
                                AssemblyContextLibraryRole.ApiOnly,
                                s_memberGroupMaterializationLimits,
                                CancellationToken.None)),
                    typeIdentity,
                    memberName)
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
        string memberName)
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
            ProjectMemberGroupDocument(
                await ExecuteMemberGroupDocumentAsync(
                    resolution.Scope.UseParticipant(
                        resolution.Participant,
                        (group, member) =>
                            AssemblyContextLibraryAdapter.MaterializeAsync(
                                group,
                                member,
                                AssemblyContextLibraryRole.Implementation,
                                s_memberGroupMaterializationLimits,
                                CancellationToken.None)),
                    typeIdentity,
                    memberName)
                .ConfigureAwait(false));
        return SerializeMemberGroupDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryUploadedLibraryMemberGroupDocument(
        string declaredName,
        byte[] content,
        string typeIdentity,
        string memberName)
    {
        ArgumentNullException.ThrowIfNull(content);
        BrowserMemberGroupDocumentInspection inspection =
            ProjectMemberGroupDocument(
                await ExecuteMemberGroupDocumentAsync(
                    EmbeddedLibraryInspection.MaterializeAsync(
                        declaredName,
                        ImmutableArray.CreateRange(content),
                        AssemblyContextLibraryRole.Implementation,
                        s_memberGroupMaterializationLimits),
                    typeIdentity,
                    memberName)
                .ConfigureAwait(false));
        return SerializeMemberGroupDocument(inspection);
    }

    private const long MemberGroupMaxAssemblyImageBytes =
        64L * 1024 * 1024;

    private static readonly ApiSurfaceExtractionBounds s_memberGroupBounds =
        new(
            maxTypes: BrowserApiSurfacePolicy.MaxTypes,
            maxMembers: BrowserApiSurfacePolicy.MaxMembers,
            maxInspectionFailures:
                BrowserApiSurfacePolicy.MaxInspectionFailures,
            maxTypeForwarders:
                BrowserApiSurfacePolicy.MaxTypeForwarders,
            maxMetadataRows:
                BrowserApiSurfacePolicy.MaxMetadataRows,
            maxRetainedTextCharacters:
                BrowserApiSurfacePolicy.MaxRetainedTextCharacters);

    private static readonly AssemblyContextLibraryMaterializationLimits
        s_memberGroupMaterializationLimits =
            new(
                MemberGroupMaxAssemblyImageBytes,
                MemberGroupMaxAssemblyImageBytes);

    private static async Task<
        InspectionEnvelope<MemberGroupDocumentInspectionOutcome>>
        ExecuteMemberGroupDocumentAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        MetadataTypeDefinitionName type = ParseTypeIdentity(typeIdentity);
        var plan = new MemberOverloadPopulationInspectionPlan(
            new MemberGroupSubject(type, memberName),
            new MemberOverloadPopulationRequest(
                new MemberOverloadCountRequest(),
                new MemberOverloadRowsRequest(
                    maximumRows: s_memberGroupBounds.MaxMembers),
                MemberOverloadAccessibilityFilter.Public,
                MemberOverloadReceiverFilter.All,
                includeHidden: false),
            s_memberGroupBounds);
        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<MemberGroupDocumentInspectionOutcome>> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                        materialization,
                        (reference, owner) =>
                            owner.IssueOperationLease(reference)
                                is LibraryOperationLeaseIssueOutcome.Issued issued
                                ? MemberGroupDocumentInspectionOperation.Execute(
                                    new(reference, plan),
                                    issued.Lease)
                                : null)
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

        return new InspectionEnvelope<MemberGroupDocumentInspectionOutcome>(
            inspection.Content,
            inspection.Share,
            inspection.Diagnostics.Concat(
                run.CleanupFailures.Select(static failure =>
                    new InspectionDiagnostic(
                        "member-group-document.library-retirement",
                        InspectionDiagnosticSeverity.Warning,
                        failure))));
    }

    private static MetadataTypeDefinitionName ParseTypeIdentity(
        string typeIdentity) =>
        MetadataTypeDefinitionName.ParseSerialized(typeIdentity) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid =>
                valid.Name,
            MetadataTypeDefinitionNameResult.Rejected rejected =>
                throw new ArgumentException(
                    $"The exact Type identity is invalid "
                        + $"({rejected.Rejection.Kind}).",
                    nameof(typeIdentity)),
            _ => throw new InvalidOperationException(
                "Unknown exact Type identity parse result."),
        };

    private static BrowserMemberGroupDocumentInspection
        ProjectMemberGroupDocument(
            InspectionEnvelope<MemberGroupDocumentInspectionOutcome>
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
            MemberGroupDocumentInspectionOutcome.Available available =>
                ProjectAvailable(available.Document, diagnostics),
            MemberGroupDocumentInspectionOutcome.Rejected rejected =>
                new(
                    BrowserMemberGroupDocumentOutcome.Rejected,
                    $"The member group was rejected ({rejected.Reason}).",
                    null,
                    diagnostics),
            MemberGroupDocumentInspectionOutcome.Incomplete incomplete =>
                new(
                    BrowserMemberGroupDocumentOutcome.Incomplete,
                    $"The member group reached {incomplete.Bound} "
                        + $"({incomplete.Measured} > {incomplete.Limit}).",
                    null,
                    diagnostics),
            MemberGroupDocumentInspectionOutcome.Failed failed =>
                new(
                    BrowserMemberGroupDocumentOutcome.Failed,
                    $"The member group failed ({failed.Reason}).",
                    null,
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown member-group document outcome."),
        };
    }

    private static BrowserMemberGroupDocumentInspection ProjectAvailable(
        MemberGroupDocument document,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics)
    {
        if (document.Overloads.Count
                is not MemberOverloadCountOutcome.Counted count)
        {
            return new(
                BrowserMemberGroupDocumentOutcome.Failed,
                "The member-group document did not produce its requested Count.",
                null,
                diagnostics);
        }
        if (document.Overloads.Rows
                is not MemberOverloadRowsOutcome.Read rows)
        {
            return new(
                BrowserMemberGroupDocumentOutcome.Failed,
                "The member-group document did not produce readable Rows.",
                null,
                diagnostics);
        }
        if (rows.Continuation is not null
            || rows.Items.Length != count.Value)
        {
            return new(
                BrowserMemberGroupDocumentOutcome.Incomplete,
                "The member-group document did not contain its complete overload population.",
                null,
                diagnostics);
        }

        return new(
            BrowserMemberGroupDocumentOutcome.Available,
            null,
            new(
                document.Subject.DeclaringType.ToEscapedFullName(),
                document.Subject.Name,
                count.Value,
                [
                    .. rows.Items.Select(static row =>
                        new BrowserMemberGroupDocumentRow(
                            row.MetadataToken,
                            row.BaselineOrdinal,
                            row.DisplaySignature.ToString(),
                            row.CanonicalSignature.ToString(),
                            row.Fingerprint.ToString(),
                            row.Accessibility.ToString(),
                            row.Receiver.ToString())),
                ]),
            diagnostics);
    }

    private static string SerializeMemberGroupDocument(
        BrowserMemberGroupDocumentInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserMemberGroupDocumentInspection);
}
