using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using DotnetInspect.Web;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Analysis;

public sealed record BrowserResourceTriage(
    string Outcome,
    BrowserResourceTriageCandidate[] Candidates,
    BrowserResourceTriageLimitation[] Limitations,
    string? InspectionError,
    BrowserAnalysisInspectionShare? Share,
    BrowserAnalysisInspectionDiagnostic[] Diagnostics);

public sealed record BrowserResourceTriageCandidate(
    string CandidateId, string FindingId, string Provenance,
    string Assembly, string Method, int MethodToken, Guid ModuleVersionId,
    string? TypeId, string? StableSelector,
    string Resource, string Shape, int AcquireOffset,
    BrowserResourceTriageBoundary[] Boundaries,
    string Actionability, string Reason, string Impact,
    string Remediation, string Confidence);

public sealed record BrowserResourceTriageBoundary(int IlOffset, string Operation, string Kind);
public sealed record BrowserResourceTriageLimitation(string Kind, string Detail, string? Method);

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(nameof(QueryPackageResourceTriage), typeof(BrowserResourceTriage))]
[JsExportJsonOutput(nameof(QueryPlatformResourceTriage), typeof(BrowserResourceTriage))]
public static partial class AnalysisExports
{
    [JSExport]
    public static async Task<string> QueryPackageResourceTriage(
        string packageId, string version, string targetFramework, string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(packageId, version, targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserResourceTriage result;
        if (!coordinate.Selection.IsSelected)
            result = ResourceTriageUnavailable("unavailable", "No matching compile Library is available.");
        else
        {
            BrowserWorkspaceParticipant participant = scope.LibraryParticipant(coordinate, assemblyName);
            // Selecting this tab explicitly requests runtime-backed model resolution.
            await using BrowserPlatformScopeResolution runtime =
                await BrowserPlatformWorkspace.OpenRuntimeAsync(coordinate.Framework);
            var inspection = await scope.UseMetadataParticipant(participant,
                (group, target) => runtime.Scope.UseParticipant(runtime.Participant,
                    (runtimeGroup, coreLibrary) => AssemblyResourceTriageInspection.ExecuteWithRuntimeAsync(
                        group, target, runtimeGroup, coreLibrary)));
            result = ProjectResourceTriage(inspection);
            string assembly = scope.TryGetSurfaceParticipant(participant)?.Asset.AssemblyName
                ?? participant.Asset.AssemblyName;
            result = result with { Candidates = [.. result.Candidates.Select(candidate => candidate with { Assembly = assembly })] };
        }
        return JsonSerializer.Serialize(result, BrowserAnalysisJsonContext.Default.BrowserResourceTriage);
    }

    [JSExport]
    public static async Task<string> QueryPlatformResourceTriage(
        string targetFramework, string platformVersion, string assemblyFileName, string pack)
    {
        BrowserResourceTriage result;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework, platformVersion, assemblyFileName, pack))
        {
            var inspection = resolution.Scope.UseParticipant(
                resolution.Participant, AssemblyResourceTriageInspection.Execute);
            result = ProjectResourceTriage(inspection);
            result = result with { Candidates = [.. result.Candidates.Select(candidate => candidate with { Assembly = assemblyFileName })] };
        }
        return JsonSerializer.Serialize(result, BrowserAnalysisJsonContext.Default.BrowserResourceTriage);
    }

    internal static BrowserResourceTriage ProjectResourceTriage(
        InspectionEnvelope<AssemblyContextEntry<AssemblyResourceTriageResult>> inspection)
    {
        BrowserResourceTriage result = inspection.Content switch
        {
            AssemblyContextEntry<AssemblyResourceTriageResult>.Available available =>
                ProjectResourceTriageContent(available.Subject.Identity.Name, available.Value),
            AssemblyContextEntry<AssemblyResourceTriageResult>.Rejected rejected =>
                ResourceTriageUnavailable("rejected", rejected.Failure.Detail),
            AssemblyContextEntry<AssemblyResourceTriageResult>.Failed failed =>
                ResourceTriageUnavailable("failed", failed.Error.Message),
            _ => throw new InvalidOperationException("Unknown Resource Triage participant outcome."),
        };
        return result with
        {
            Share = BrowserAnalysisInspectionProjection.Project(inspection.Share),
            Diagnostics = [.. inspection.Diagnostics.Select(BrowserAnalysisInspectionProjection.Project)],
        };
    }

    static BrowserResourceTriage ProjectResourceTriageContent(string assembly, AssemblyResourceTriageResult content)
    {
        var assessments = content.Triage switch
        {
            ResourceTriageResult.Available available => available.Assessments,
            ResourceTriageResult.Incomplete incomplete => incomplete.Assessments,
            _ => [],
        };
        var limitations = content.Triage is ResourceTriageResult.Incomplete partial
            ? partial.Limitations : [];
        string outcome = content.Triage switch
        {
            ResourceTriageResult.Available => "available",
            ResourceTriageResult.Incomplete => "incomplete",
            ResourceTriageResult.NoMetadata => "unavailable",
            ResourceTriageResult.Failed => "failed",
            _ => throw new InvalidOperationException("Unknown Resource Triage result."),
        };
        string? error = content.Triage switch
        {
            ResourceTriageResult.Failed failed => failed.Error.Reason,
            ResourceTriageResult.NoMetadata => "The image has no managed metadata.",
            _ => null,
        };
        // API failures affect navigation, not the lifecycle findings themselves.
        var navigationFailures = content.ApiSurfaceInspectionFailures;
        if (navigationFailures.Length > 0)
        {
            outcome = outcome == "available" ? "incomplete" : outcome;
            error = string.Join("; ", new[] { error }.Where(detail => !string.IsNullOrWhiteSpace(detail))
                .Concat(navigationFailures.Select(failure => failure.Detail)));
        }
        return new(outcome,
            [.. assessments.Select(assessment =>
            {
                ResourceLifecycleOccurrence occurrence = assessment.Source.Payload;
                var member = content.PublicMembers.GetValueOrDefault(occurrence.Method.MetadataToken);
                return new BrowserResourceTriageCandidate(
                    assessment.CandidateId, assessment.Source.Descriptor.Id,
                    "exact", assembly, ResourceTriageMethodLabel(occurrence.Method), occurrence.Method.MetadataToken, occurrence.Method.ModuleVersionId,
                    member?.Type, member?.StableSelector,
                    occurrence.Resource, occurrence.Shape, occurrence.AcquireOffset,
                    [.. assessment.Boundaries.Select(boundary => new BrowserResourceTriageBoundary(
                        boundary.Evidence.ILOffset, $"{boundary.Evidence.Operation.DeclaringType.ToQualifiedDisplayString()}.{boundary.Evidence.Operation.Name}", boundary.Kind.ToString()))],
                    assessment.Actionability.ToString(), assessment.Reason.ToString(),
                    assessment.Impact.ToString(), assessment.Remediation.ToString(), assessment.Confidence.ToString());
            })],
            [.. limitations.Select(limitation => new BrowserResourceTriageLimitation(
                limitation.Kind.ToString(), limitation.Detail, limitation.Method is { } method ? ResourceTriageMethodLabel(method) : null))],
            error, ResourceTriageShare(), []);
    }

    static string ResourceTriageMethodLabel(MethodIdentity method) =>
        $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(parameter => parameter.ToQualifiedDisplayString()))})";

    static BrowserAnalysisInspectionShare ResourceTriageShare() =>
        BrowserAnalysisInspectionProjection.Project(new InspectionShare.NonProjectable(
            "resource-triage/share", "Resource Triage does not yet have a canonical Workspace Share projection."));

    static BrowserResourceTriage ResourceTriageUnavailable(string outcome, string error) =>
        new(outcome, [], [], error, ResourceTriageShare(), []);
}
