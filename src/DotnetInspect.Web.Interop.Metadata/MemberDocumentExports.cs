using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryMemberDocument),
    typeof(BrowserMemberDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformMemberDocument),
    typeof(BrowserMemberDocumentInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryMemberDocument),
    typeof(BrowserMemberDocumentInspection))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryMemberDocument(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        int baselineOrdinal,
        string fingerprintPrefix)
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
        BrowserMemberDocumentInspection inspection =
            ProjectMemberDocument(
                await ExecuteMemberDocumentAsync(
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
                    memberName,
                    baselineOrdinal,
                    fingerprintPrefix)
                .ConfigureAwait(false));
        return SerializeMemberDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryPlatformMemberDocument(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string memberName,
        int baselineOrdinal,
        string fingerprintPrefix)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    CancellationToken.None)
                .ConfigureAwait(false);
        BrowserMemberDocumentInspection inspection =
            ProjectMemberDocument(
                await ExecuteMemberDocumentAsync(
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
                    memberName,
                    baselineOrdinal,
                    fingerprintPrefix)
                .ConfigureAwait(false));
        return SerializeMemberDocument(inspection);
    }

    [JSExport]
    public static async Task<string> QueryUploadedLibraryMemberDocument(
        string declaredName,
        byte[] content,
        string typeIdentity,
        string memberName,
        int baselineOrdinal,
        string fingerprintPrefix)
    {
        ArgumentNullException.ThrowIfNull(content);
        BrowserMemberDocumentInspection inspection =
            ProjectMemberDocument(
                await ExecuteMemberDocumentAsync(
                    EmbeddedLibraryInspection.MaterializeAsync(
                        declaredName,
                        ImmutableArray.CreateRange(content),
                        AssemblyContextLibraryRole.Implementation,
                        s_memberGroupMaterializationLimits),
                    typeIdentity,
                    memberName,
                    baselineOrdinal,
                    fingerprintPrefix)
                .ConfigureAwait(false));
        return SerializeMemberDocument(inspection);
    }

    private static async Task<
        InspectionEnvelope<MemberDocumentInspectionOutcome>>
        ExecuteMemberDocumentAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string memberName,
            int baselineOrdinal,
            string fingerprintPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        bool hasOrdinal = baselineOrdinal > 0;
        bool hasFingerprint =
            !string.IsNullOrWhiteSpace(fingerprintPrefix);
        if (hasOrdinal == hasFingerprint)
        {
            throw new ArgumentException(
                "An exact Member request requires either one baseline "
                    + "ordinal or one fingerprint prefix.");
        }
        var plan = new MemberDocumentInspectionPlan(
            new MemberGroupSubject(
                ParseTypeIdentity(typeIdentity),
                memberName),
            hasOrdinal
                ? new MemberDocumentSelector(
                    baselineOrdinal: baselineOrdinal)
                : new MemberDocumentSelector(
                    fingerprintPrefix: fingerprintPrefix),
            s_memberGroupBounds);
        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<MemberDocumentInspectionOutcome>> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                        materialization,
                        (reference, owner) =>
                            owner.IssueOperationLease(reference)
                                is LibraryOperationLeaseIssueOutcome.Issued issued
                                ? MemberDocumentInspectionOperation.Execute(
                                    new(reference, plan),
                                    issued.Lease)
                                : null)
                    .ConfigureAwait(false);
        if (run.Failure is { } failure)
        {
            throw new InvalidOperationException(
                $"The Member Library could not be materialized: {failure}");
        }
        if (run.Result is not { } inspection)
        {
            throw new InvalidOperationException(
                "The exact Library owner could not issue the Member "
                    + "inspection lease.");
        }
        if (run.CleanupFailures.IsEmpty)
            return inspection;

        return new InspectionEnvelope<MemberDocumentInspectionOutcome>(
            inspection.Content,
            inspection.Share,
            inspection.Diagnostics.Concat(
                run.CleanupFailures.Select(static failure =>
                    new InspectionDiagnostic(
                        "member-document.library-retirement",
                        InspectionDiagnosticSeverity.Warning,
                        failure))));
    }

    private static BrowserMemberDocumentInspection ProjectMemberDocument(
        InspectionEnvelope<MemberDocumentInspectionOutcome> inspection)
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
            MemberDocumentInspectionOutcome.Available available =>
                ProjectAvailableMember(available.Document, diagnostics),
            MemberDocumentInspectionOutcome.Rejected rejected =>
                new(
                    BrowserMemberDocumentOutcome.Rejected,
                    $"The Member was rejected ({rejected.Reason}).",
                    null,
                    diagnostics),
            MemberDocumentInspectionOutcome.Incomplete incomplete =>
                new(
                    BrowserMemberDocumentOutcome.Incomplete,
                    $"The Member reached {incomplete.Bound} "
                        + $"({incomplete.Measured} > {incomplete.Limit}).",
                    null,
                    diagnostics),
            MemberDocumentInspectionOutcome.Failed failed =>
                new(
                    BrowserMemberDocumentOutcome.Failed,
                    $"The Member failed ({failed.Reason}).",
                    null,
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Member document outcome."),
        };
    }

    private static BrowserMemberDocumentInspection ProjectAvailableMember(
        MemberDocument document,
        BrowserMemberGroupDocumentDiagnostic[] diagnostics) =>
        new(
            BrowserMemberDocumentOutcome.Available,
            null,
            new(
                document.Subject.Group.DeclaringType.ToEscapedFullName(),
                document.Subject.Group.Name,
                document.Subject.MetadataToken,
                document.Subject.BaselineOrdinal,
                document.DisplaySignature.ToString(),
                document.CanonicalSignature.ToString(),
                document.Subject.Fingerprint.ToString(),
                document.Accessibility.ToString(),
                document.Receiver.ToString()),
            diagnostics);

    private static string SerializeMemberDocument(
        BrowserMemberDocumentInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserMemberDocumentInspection);
}
