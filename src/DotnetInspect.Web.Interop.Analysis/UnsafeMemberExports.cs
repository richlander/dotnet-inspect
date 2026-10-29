using System.Collections.Immutable;
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
/// The unsafe member census of one Library, grouped by declaring Type, as
/// docs/design/inspect-web-surface-composition.md#unsafe defines for its
/// Library, Type, and Member scopes. <see cref="Counts"/> covers the Library.
/// </summary>
public sealed record BrowserUnsafeMembers(
    string Outcome,
    string Assembly,
    BrowserUnsafeMemberCounts Counts,
    BrowserUnsafeType[] Types,
    string[] Limitations,
    string? InspectionError);

/// <summary>
/// The unsafe member dimensions a scope carries: members with findings, public
/// ones, propagating ones, and safe boundaries.
/// <see cref="SafeBoundaryUndecided"/> counts members whose module rules leave
/// the safe-boundary question inapplicable or unknown.
/// </summary>
public sealed record BrowserUnsafeMemberCounts(
    int Members,
    int Public,
    int Propagating,
    int SafeBoundaries,
    int SafeBoundaryUndecided);

/// <summary>
/// One declaring Type's findings and counts. <see cref="TypeId"/> and
/// <see cref="DeclaringType"/> are its navigation identity when the API
/// surface resolves one of its members; the Type otherwise renders without a
/// link.
/// </summary>
public sealed record BrowserUnsafeType(
    string Type,
    string? TypeId,
    BrowserPerformanceTypeSurface? DeclaringType,
    BrowserUnsafeMemberCounts Counts,
    BrowserUnsafeMember[] Members);

/// <summary>
/// One declared member's finding: the row the CLI section shows, its
/// navigation identity when the API surface resolves it, and its evidence.
/// <see cref="DeclaringType"/> is the declaring Type as the Library's ordinary
/// Type list would project it, so a host opens the member as a transient Type
/// when that list does not carry it, as Performance rows do.
/// </summary>
public sealed record BrowserUnsafeMember(
    string Member,
    int MetadataToken,
    string Exposure,
    bool ExplicitContract,
    string SafeBoundary,
    string Evidence,
    bool Partial,
    string? TypeId,
    string? StableSelector,
    string? Accessibility,
    BrowserPerformanceTypeSurface? DeclaringType,
    BrowserUnsafeMemberEvidence[] Items);

/// <summary>
/// How a host's ordinary Type list names the Library, so a row's navigation
/// identity and transient Type match the Types that list carries.
/// </summary>
internal sealed record BrowserUnsafeMemberSurfaceNaming(
    string Assembly,
    string AssemblyId,
    string? PlatformPack);

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
            // Rows navigate into the Library's API surface, which names the assembly.
            BrowserWorkspaceParticipant surface =
                scope.TryGetSurfaceParticipant(participant) ?? participant;
            result = scope.ImplementationParticipants.Contains(participant)
                ? ProjectUnsafeMembers(
                    scope.UseImplementationParticipant(
                        participant, AssemblyContextUnsafeMemberCensusQuery.ExecuteParticipant),
                    _ => new BrowserUnsafeMemberSurfaceNaming(
                        surface.Asset.AssemblyName, surface.Asset.Id, PlatformPack: null))
                : UnsafeMembersUnavailable(
                    "unavailable", "The selected library has no managed implementation assembly.");
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
            // Platform Types carry the bare identity name as both assembly and id.
            string? platformPack = string.IsNullOrWhiteSpace(pack) ? null : pack;
            result = ProjectUnsafeMembers(
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    static (group, participant) =>
                        AssemblyContextUnsafeMemberCensusQuery.ExecuteParticipant(group, participant)),
                identity => new BrowserUnsafeMemberSurfaceNaming(
                    identity.Name, identity.Name, platformPack));
        }
        return JsonSerializer.Serialize(result, BrowserAnalysisJsonContext.Default.BrowserUnsafeMembers);
    }

    internal static BrowserUnsafeMembers ProjectUnsafeMembers(
        AssemblyContextEntry<AssemblyUnsafeMemberCensusResult> entry,
        Func<AssemblyReferenceIdentity, BrowserUnsafeMemberSurfaceNaming> naming) =>
        entry switch
        {
            AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Available available =>
                ProjectUnsafeMembers(
                    available.Subject.Identity,
                    available.Value,
                    naming(available.Subject.Identity)),
            AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Rejected rejected =>
                UnsafeMembersUnavailable("rejected", rejected.Failure.Detail),
            AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Failed failed =>
                UnsafeMembersUnavailable("failed", failed.Error.Message),
            _ => throw new InvalidOperationException("Unknown unsafe member census participant outcome."),
        };

    static BrowserUnsafeMembers ProjectUnsafeMembers(
        AssemblyReferenceIdentity identity,
        AssemblyUnsafeMemberCensusResult content,
        BrowserUnsafeMemberSurfaceNaming naming)
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
                Project("available", naming.Assembly, complete.Inspection, content, identity, naming, []),
            UnsafeMemberFindingInspection.Incomplete incomplete =>
                Project("incomplete", naming.Assembly, incomplete.Inspection, content, identity, naming,
                    [.. incomplete.Limitations.Select(static limitation =>
                        limitation.Describe(UnsafeMemberMethodLabel))]),
            UnsafeMemberFindingInspection.Failed failed =>
                UnsafeMembersUnavailable("failed", failed.Error.Reason),
            _ => throw new InvalidOperationException("Unknown unsafe member inspection outcome."),
        };
    }

    // Types by name, each with its findings in the CLI section's order (the
    // rendered member, then its token) and the shared counts.
    static BrowserUnsafeMembers Project(
        string outcome,
        string assembly,
        Inspector.Findings.FindingInspection<UnsafeMemberFinding>.Complete inspection,
        AssemblyUnsafeMemberCensusResult content,
        AssemblyReferenceIdentity identity,
        BrowserUnsafeMemberSurfaceNaming naming,
        string[] limitations)
    {
        UnsafeMemberFinding[] ordered =
        [
            .. inspection.Findings
                .Select(static finding => finding.Payload)
                .OrderBy(static finding => UnsafeMemberMethodLabel(finding.Member), StringComparer.Ordinal)
                .ThenBy(static finding => finding.Member.MetadataToken),
        ];
        BrowserUnsafeType[] types =
        [
            .. UnsafeMemberTypeGroup.Group(ordered)
                .Select(static group => (Name: group.DeclaringType.ToQualifiedDisplayString(), Group: group))
                .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
                .Select(static entry => entry.Group)
                .Select(group =>
            {
                BrowserUnsafeMember[] members = Rows(group.Members, content, identity, naming);
                BrowserUnsafeMember? navigable = members.FirstOrDefault(static member => member.TypeId is not null);
                return new BrowserUnsafeType(
                    group.DeclaringType.ToQualifiedDisplayString(),
                    navigable?.TypeId,
                    navigable?.DeclaringType,
                    Counts(group.Counts),
                    members);
            }),
        ];
        return new(outcome, assembly, Counts(UnsafeMemberCounts.Of(ordered)), types, limitations, null);
    }

    static BrowserUnsafeMemberCounts Counts(UnsafeMemberCounts counts) =>
        new(counts.Members, counts.Public, counts.Propagating, counts.SafeBoundaries, counts.SafeBoundaryUndecided);

    static BrowserUnsafeMember[] Rows(
        ImmutableArray<UnsafeMemberFinding> findings,
        AssemblyUnsafeMemberCensusResult content,
        AssemblyReferenceIdentity identity,
        BrowserUnsafeMemberSurfaceNaming naming) =>
        [
            .. findings
                .Select(finding => (Label: UnsafeMemberMethodLabel(finding.Member), Finding: finding))
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
                        UnsafeMemberFindingSummary.SafeBoundaryText(finding.SafeBoundary),
                        string.Join("; ", UnsafeMemberFindingSummary.RolesOf(finding.Evidence)
                            .Select(static role => UnsafeMemberFindingSummary.RoleText(role, static callee => callee))),
                        finding.HasPartialEvidence,
                        navigation?.Type,
                        navigation?.StableSelector,
                        navigation?.Accessibility,
                        navigation is null
                            ? null
                            : BrowserAnalysisWireProjection.Project(BrowserSurfaceProjection.Type(
                                navigation.DeclaringType,
                                naming.Assembly,
                                naming.AssemblyId,
                                identity.Name,
                                qualifyId: true,
                                platformPack: naming.PlatformPack,
                                selectedMembers: [])),
                        [.. finding.Evidence.Select(static evidence => new BrowserUnsafeMemberEvidence(
                            UnsafeMemberFindingSummary.RoleText(evidence.Kind),
                            UnsafeMemberMethodLabel(evidence.Body),
                            evidence.ILOffset,
                            evidence.Detail))]);
                }),
        ];

    // The CLI section's member spelling: the shared analysis member format
    // with no method type arguments.
    static string UnsafeMemberMethodLabel(MethodIdentity method) =>
        FormatAnalysisMember(method.DeclaringType, method.Name, method.ParameterTypes, []);

    static BrowserUnsafeMembers UnsafeMembersUnavailable(string outcome, string error) =>
        new(outcome, "", new(0, 0, 0, 0, 0), [], [], error);
}
