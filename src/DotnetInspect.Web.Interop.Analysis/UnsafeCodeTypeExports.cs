using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using DotnetInspect.Web;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// The Library scope of the Unsafe view: the Types that have unsafe code, as
/// Finding Unsafe Any settles them, and the limitations that leave an answer
/// unsettled, as docs/design/inspect-web-surface-composition.md#unsafe
/// defines.
/// </summary>
public sealed record BrowserUnsafeCodeTypes(
    string Outcome,
    string Assembly,
    BrowserUnsafeCodeType[] Types,
    string[] Limitations,
    string? InspectionError);

/// <summary>
/// One Type and its state: <c>unsafe</c> when it declares a member with a
/// census finding, <c>unknown</c> when a body of one of its members could not
/// be inspected. <see cref="TypeId"/> and <see cref="DeclaringType"/> are its
/// navigation identity when the API surface resolves it; the Type otherwise
/// renders without a link.
/// </summary>
public sealed record BrowserUnsafeCodeType(
    string Type,
    string State,
    string? TypeId,
    BrowserPerformanceTypeSurface? DeclaringType);

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(nameof(QueryPackageUnsafeCodeTypes), typeof(BrowserUnsafeCodeTypes))]
[JsExportJsonOutput(nameof(QueryPlatformUnsafeCodeTypes), typeof(BrowserUnsafeCodeTypes))]
public static partial class AnalysisExports
{
    [JSExport]
    public static async Task<string> QueryPackageUnsafeCodeTypes(
        string packageId, string version, string targetFramework, string assemblyName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(packageId, version, targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserUnsafeCodeTypes result;
        if (!coordinate.Selection.IsSelected)
        {
            result = UnsafeCodeTypesUnavailable(
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
                ? ProjectUnsafeCodeTypes(
                    scope.UseImplementationParticipant(
                        participant,
                        AssemblyContextFindingUnsafeAnyQuery.ExecuteParticipant),
                    _ => new BrowserUnsafeMemberSurfaceNaming(
                        surface.Asset.AssemblyName, surface.Asset.Id, PlatformPack: null))
                : UnsafeCodeTypesUnavailable(
                    "unavailable", "The selected library has no managed implementation assembly.");
        }
        return JsonSerializer.Serialize(result, BrowserAnalysisJsonContext.Default.BrowserUnsafeCodeTypes);
    }

    [JSExport]
    public static async Task<string> QueryPlatformUnsafeCodeTypes(
        string targetFramework, string platformVersion, string assemblyFileName, string pack)
    {
        BrowserUnsafeCodeTypes result;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework, platformVersion, assemblyFileName, pack))
        {
            // Platform Types carry the bare identity name as both assembly and id.
            string? platformPack = string.IsNullOrWhiteSpace(pack) ? null : pack;
            result = ProjectUnsafeCodeTypes(
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    AssemblyContextFindingUnsafeAnyQuery.ExecuteParticipant),
                identity => new BrowserUnsafeMemberSurfaceNaming(
                    identity.Name, identity.Name, platformPack));
        }
        return JsonSerializer.Serialize(result, BrowserAnalysisJsonContext.Default.BrowserUnsafeCodeTypes);
    }

    internal static BrowserUnsafeCodeTypes ProjectUnsafeCodeTypes(
        AssemblyContextEntry<AssemblyUnsafeCodeTypesResult> entry,
        Func<AssemblyReferenceIdentity, BrowserUnsafeMemberSurfaceNaming> naming) =>
        entry switch
        {
            AssemblyContextEntry<AssemblyUnsafeCodeTypesResult>.Available available =>
                ProjectUnsafeCodeTypes(
                    available.Subject.Identity,
                    available.Value,
                    naming(available.Subject.Identity)),
            AssemblyContextEntry<AssemblyUnsafeCodeTypesResult>.Rejected rejected =>
                UnsafeCodeTypesUnavailable("rejected", rejected.Failure.Detail),
            AssemblyContextEntry<AssemblyUnsafeCodeTypesResult>.Failed failed =>
                UnsafeCodeTypesUnavailable("failed", failed.Error.Message),
            _ => throw new InvalidOperationException("Unknown Finding Unsafe Any participant outcome."),
        };

    // Types by name, as the CLI Library rows order them. Navigation identity
    // is a convenience; a Type the API surface cannot resolve keeps its row
    // and renders without a link.
    static BrowserUnsafeCodeTypes ProjectUnsafeCodeTypes(
        AssemblyReferenceIdentity identity,
        AssemblyUnsafeCodeTypesResult content,
        BrowserUnsafeMemberSurfaceNaming naming)
    {
        UnsafeCodeTypes answer;
        switch (content.Answer)
        {
            case FindingUnsafeAnyQueryResult.Inspected { Answer: FindingUnsafeAnyResult.Available available }:
                answer = available.Types;
                break;
            case FindingUnsafeAnyQueryResult.Inspected { Answer: FindingUnsafeAnyResult.Failed failed }:
                return UnsafeCodeTypesUnavailable("failed", failed.Error.Message);
            case FindingUnsafeAnyQueryResult.NoMetadata:
                return UnsafeCodeTypesUnavailable("unavailable", "The image has no managed metadata.");
            case FindingUnsafeAnyQueryResult.Failed failed:
                return UnsafeCodeTypesUnavailable("failed", failed.Error.Message);
            default:
                throw new InvalidOperationException("Unknown Finding Unsafe Any result.");
        }

        BrowserUnsafeCodeType[] types =
        [
            .. answer.Types
                .Select(type => (Name: type.DeclaringType.ToQualifiedDisplayString(), Type: type))
                .OrderBy(static row => row.Name, StringComparer.Ordinal)
                .Select(row =>
                {
                    UnsafeCodeTypeNavigation? navigation =
                        row.Type.MetadataToken is { } token
                            ? content.Types.GetValueOrDefault(token)
                            : null;
                    return new BrowserUnsafeCodeType(
                        row.Name,
                        row.Type.State == UnsafeCodeTypeState.Unsafe ? "unsafe" : "unknown",
                        navigation?.Type,
                        navigation is null
                            ? null
                            : BrowserAnalysisWireProjection.Project(BrowserSurfaceProjection.Type(
                                navigation.DeclaringType,
                                naming.Assembly,
                                naming.AssemblyId,
                                identity.Name,
                                qualifyId: true,
                                platformPack: naming.PlatformPack,
                                selectedMembers: [])));
                }),
        ];
        return new(
            answer.IsComplete ? "available" : "incomplete",
            naming.Assembly,
            types,
            [.. answer.Limitations.Select(static limitation => limitation.Describe(UnsafeMemberMethodLabel))],
            null);
    }

    static BrowserUnsafeCodeTypes UnsafeCodeTypesUnavailable(string outcome, string error) =>
        new(outcome, "", [], [], error);
}
