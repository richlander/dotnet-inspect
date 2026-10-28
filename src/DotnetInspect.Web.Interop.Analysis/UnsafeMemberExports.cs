using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using DotnetInspect.Web;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// The unsafe member census of one Library, one row per declared member with a
/// finding, as docs/design/inspect-web-surface-composition.md#unsafe
/// defines for the Unsafe tab.
/// </summary>
public sealed record BrowserUnsafeMembers(
    string Outcome,
    string Assembly,
    BrowserUnsafeMember[] Members,
    string[] Limitations,
    string? InspectionError);

/// <summary>
/// One declared member's finding: the row the CLI section shows, its
/// navigation identity when the API surface resolves it, and its evidence.
/// </summary>
public sealed record BrowserUnsafeMember(
    string Member,
    int MetadataToken,
    string Exposure,
    bool ExplicitContract,
    string Evidence,
    bool Partial,
    string? TypeId,
    string? StableSelector,
    string? Accessibility,
    BrowserUnsafeMemberEvidence[] Items);

/// <summary>One contributing evidence item and the physical body that produced it.</summary>
public sealed record BrowserUnsafeMemberEvidence(
    string Role,
    string Body,
    int? IlOffset,
    string Detail);

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(nameof(QueryPackageUnsafeMembers), typeof(BrowserUnsafeMembers))]
[JsExportJsonOutput(nameof(QueryPlatformUnsafeMembers), typeof(BrowserUnsafeMembers))]
public static partial class AnalysisExports
{
    [JSExport]
    public static async Task<string> QueryPackageUnsafeMembers(
        string packageId, string version, string targetFramework, string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(packageId, version, targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserUnsafeMembers result;
        if (!coordinate.Selection.IsSelected)
        {
            result = UnsafeMembersUnavailable(
                "unavailable",
                $"The package has no selected compile library ({coordinate.Selection.Status}).");
        }
        else
        {
            BrowserWorkspaceParticipant participant = scope.LibraryParticipant(coordinate, assemblyName);
            result = scope.ImplementationParticipants.Contains(participant)
                ? ProjectUnsafeMembers(scope.UseImplementationParticipant(
                    participant, AssemblyContextUnsafeMemberCensusQuery.ExecuteParticipant))
                : UnsafeMembersUnavailable(
                    "unavailable", "The selected library has no managed implementation assembly.");
            // Rows navigate into the Library's API surface, which names the assembly.
            result = result with
            {
                Assembly = scope.TryGetSurfaceParticipant(participant)?.Asset.AssemblyName
                    ?? participant.Asset.AssemblyName,
            };
        }
        return JsonSerializer.Serialize(result, BrowserAnalysisJsonContext.Default.BrowserUnsafeMembers);
    }

    [JSExport]
    public static async Task<string> QueryPlatformUnsafeMembers(
        string targetFramework, string platformVersion, string assemblyFileName, string pack)
    {
        BrowserUnsafeMembers result;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework, platformVersion, assemblyFileName, pack))
        {
            result = ProjectUnsafeMembers(resolution.Scope.UseParticipant(
                resolution.Participant,
                static (group, participant) =>
                    AssemblyContextUnsafeMemberCensusQuery.ExecuteParticipant(group, participant)))
                with { Assembly = assemblyFileName };
        }
        return JsonSerializer.Serialize(result, BrowserAnalysisJsonContext.Default.BrowserUnsafeMembers);
    }

    internal static BrowserUnsafeMembers ProjectUnsafeMembers(
        AssemblyContextEntry<AssemblyUnsafeMemberCensusResult> entry) =>
        entry switch
        {
            AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Available available =>
                ProjectUnsafeMembers(available.Subject.Identity, available.Value),
            AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Rejected rejected =>
                UnsafeMembersUnavailable("rejected", rejected.Failure.Detail),
            AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Failed failed =>
                UnsafeMembersUnavailable("failed", failed.Error.Message),
            _ => throw new InvalidOperationException("Unknown unsafe member census participant outcome."),
        };

    static BrowserUnsafeMembers ProjectUnsafeMembers(
        AssemblyReferenceIdentity identity,
        AssemblyUnsafeMemberCensusResult content)
    {
        UnsafeMemberFindingInspection inspection;
        switch (content.Census)
        {
            case UnsafeMemberCensusQueryResult.Inspected inspected:
                inspection = AnalysisFindings.InspectUnsafeMembers(
                    inspected.Census,
                    new Inspector.Findings.FindingSubject(identity.ToString(), identity.Name));
                break;
            case UnsafeMemberCensusQueryResult.NoMetadata:
                return UnsafeMembersUnavailable("unavailable", "The image has no managed metadata.");
            case UnsafeMemberCensusQueryResult.Failed failed:
                return UnsafeMembersUnavailable("failed", failed.Error.Message);
            default:
                throw new InvalidOperationException("Unknown unsafe member census result.");
        }

        // Navigation identity is a convenience; a row the API surface cannot
        // resolve keeps its finding and renders without a link.
        return inspection switch
        {
            UnsafeMemberFindingInspection.Complete complete =>
                new("available", "", Rows(complete.Inspection, content), [], null),
            UnsafeMemberFindingInspection.Incomplete incomplete =>
                new("incomplete", "", Rows(incomplete.Inspection, content),
                    [.. incomplete.Limitations.Select(static limitation =>
                        limitation.Describe(UnsafeMemberMethodLabel))],
                    null),
            UnsafeMemberFindingInspection.Failed failed =>
                UnsafeMembersUnavailable("failed", failed.Error.Reason),
            _ => throw new InvalidOperationException("Unknown unsafe member inspection outcome."),
        };
    }

    // Rows in the CLI section's order: the rendered member, then its token.
    static BrowserUnsafeMember[] Rows(
        Inspector.Findings.FindingInspection<UnsafeMemberFinding>.Complete inspection,
        AssemblyUnsafeMemberCensusResult content) =>
        [
            .. inspection.Findings
                .Select(static finding => finding.Payload)
                .Select(finding => (Label: UnsafeMemberMethodLabel(finding.Member), Finding: finding))
                .OrderBy(static row => row.Label, StringComparer.Ordinal)
                .ThenBy(static row => row.Finding.Member.MetadataToken)
                .Select(row =>
                {
                    UnsafeMemberFinding finding = row.Finding;
                    UnsafeMemberNavigation? navigation =
                        content.Members.GetValueOrDefault(finding.Member.MetadataToken);
                    return new BrowserUnsafeMember(
                        row.Label,
                        finding.Member.MetadataToken,
                        UnsafeMemberFindingSummary.ExposureText(finding.Exposure),
                        finding.HasExplicitUnsafeContract,
                        string.Join("; ", UnsafeMemberFindingSummary.RolesOf(finding.Evidence)
                            .Select(static role => UnsafeMemberFindingSummary.RoleText(role, static callee => callee))),
                        finding.HasPartialEvidence,
                        navigation?.Type,
                        navigation?.StableSelector,
                        navigation?.Accessibility,
                        [.. finding.Evidence.Select(static evidence => new BrowserUnsafeMemberEvidence(
                            UnsafeMemberFindingSummary.RoleText(evidence.Kind),
                            UnsafeMemberMethodLabel(evidence.Body),
                            evidence.ILOffset,
                            evidence.Detail))]);
                }),
        ];

    static string UnsafeMemberMethodLabel(MethodIdentity method) =>
        $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(static parameter => parameter.ToQualifiedDisplayString()))})";

    static BrowserUnsafeMembers UnsafeMembersUnavailable(string outcome, string error) =>
        new(outcome, "", [], [], error);
}
