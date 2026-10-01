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
    nameof(MetadataExports.QueryTypeMemberPopulation),
    typeof(BrowserTypeMemberPopulationInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryPlatformTypeMemberPopulation),
    typeof(BrowserTypeMemberPopulationInspection))]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryUploadedLibraryTypeMemberPopulation),
    typeof(BrowserTypeMemberPopulationInspection))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryTypeMemberPopulation(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string spelling,
        string accessibility)
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
        BrowserTypeMemberPopulationInspection inspection =
            await ExecuteTypeMemberPopulationAsync(
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
                    spelling,
                    accessibility)
                .ConfigureAwait(false);
        return SerializeTypeMemberPopulation(inspection);
    }

    [JSExport]
    public static async Task<string> QueryPlatformTypeMemberPopulation(
        string targetFramework,
        string platformVersion,
        string assemblyName,
        string pack,
        string typeIdentity,
        string spelling,
        string accessibility)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                    targetFramework,
                    platformVersion,
                    assemblyName,
                    pack,
                    CancellationToken.None)
                .ConfigureAwait(false);
        BrowserTypeMemberPopulationInspection inspection =
            await ExecuteTypeMemberPopulationAsync(
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
                    spelling,
                    accessibility)
                .ConfigureAwait(false);
        return SerializeTypeMemberPopulation(inspection);
    }

    [JSExport]
    public static async Task<string>
        QueryUploadedLibraryTypeMemberPopulation(
            string declaredName,
            byte[] content,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentNullException.ThrowIfNull(content);
        BrowserTypeMemberPopulationInspection inspection =
            await ExecuteTypeMemberPopulationAsync(
                    EmbeddedLibraryInspection.MaterializeAsync(
                        declaredName,
                        ImmutableArray.CreateRange(content),
                        AssemblyContextLibraryRole.Implementation,
                        s_memberGroupMaterializationLimits),
                    typeIdentity,
                    spelling,
                    accessibility)
                .ConfigureAwait(false);
        return SerializeTypeMemberPopulation(inspection);
    }

    private static async Task<BrowserTypeMemberPopulationInspection>
        ExecuteTypeMemberPopulationAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            string typeIdentity,
            string spelling,
            string accessibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        MetadataTypeMemberPopulationRequest request = new(
            ParseTypeIdentity(typeIdentity),
            ParseSpelling(spelling),
            includeHidden: false,
            ParseAccessibility(accessibility));
        AssemblyContextLibraryInspectionRun<
            LibraryTypeMemberPopulationInspectionOutcome> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                        materialization,
                        (reference, owner) =>
                            AssemblyContextLibraryInspection.ExecuteOperation(
                                reference,
                                owner,
                                lease =>
                                    LibraryTypeMemberPopulationInspection.Execute(
                                        new(
                                            reference,
                                            request,
                                            s_memberGroupBounds),
                                        lease)))
                    .ConfigureAwait(false);
        if (run.Failure is { } failure)
        {
            return new(
                BrowserTypeMemberPopulationOutcome.Failed,
                failure,
                null,
                [.. run.CleanupFailures]);
        }
        if (run.Result is not { } outcome)
        {
            return new(
                BrowserTypeMemberPopulationOutcome.Failed,
                "The exact Library owner could not issue the Type Member population lease.",
                null,
                [.. run.CleanupFailures]);
        }

        return ProjectTypeMemberPopulation(
            outcome,
            [.. run.CleanupFailures]);
    }

    private static BrowserTypeMemberPopulationInspection
        ProjectTypeMemberPopulation(
            LibraryTypeMemberPopulationInspectionOutcome outcome,
            string[] diagnostics) =>
        outcome switch
        {
            LibraryTypeMemberPopulationInspectionOutcome.Completed completed =>
                completed.Population switch
                {
                    MetadataTypeMemberPopulationOutcome.Available available =>
                        ProjectAvailable(available.Population, diagnostics),
                    MetadataTypeMemberPopulationOutcome.TypeNotFound =>
                        Failed("The exact Type was not found.", diagnostics),
                    MetadataTypeMemberPopulationOutcome.TypeAmbiguous =>
                        Failed("The exact Type identity was ambiguous.", diagnostics),
                    MetadataTypeMemberPopulationOutcome.Incomplete incomplete =>
                        new(
                            BrowserTypeMemberPopulationOutcome.Incomplete,
                            $"The Type Member population reached {incomplete.Bound}.",
                            null,
                            diagnostics),
                    MetadataTypeMemberPopulationOutcome.Failed failed =>
                        Failed(failed.Detail, diagnostics),
                    _ => throw new InvalidOperationException(
                        "Unknown Type Member population outcome."),
                },
            LibraryTypeMemberPopulationInspectionOutcome.Rejected rejected =>
                new(
                    BrowserTypeMemberPopulationOutcome.Rejected,
                    $"The Type Member population was rejected ({rejected.Reason}).",
                    null,
                    diagnostics),
            LibraryTypeMemberPopulationInspectionOutcome.Failed failed =>
                Failed(
                    $"The Type Member population failed ({failed.Reason}).",
                    diagnostics),
            _ => throw new InvalidOperationException(
                "Unknown Library Type Member population outcome."),
        };

    private static BrowserTypeMemberPopulationInspection ProjectAvailable(
        MetadataTypeMemberPopulation population,
        string[] diagnostics)
    {
        MetadataTypeMemberComposition composition = population.Composition;
        return new(
            BrowserTypeMemberPopulationOutcome.Available,
            null,
            new(
                population.Type.ToEscapedFullName(),
                population.Spelling.ToString(),
                population.Accessibility.ToString(),
                new(
                    composition.Public,
                    composition.Protected,
                    composition.Internal,
                    composition.Private,
                    composition.Static,
                    composition.This,
                    composition.Extension),
                [
                    .. population.Groups.Select(group =>
                    {
                        bool hasExactSelectors =
                            population.Accessibility
                                == MetadataMethodAccessibilityFilter.Public
                            && group.Kind == "method"
                            && group.Members.All(member =>
                                member.MethodSemantics
                                    is null
                                    or ApiMethodSemanticsKind.None);
                        return new BrowserTypeMemberPopulationGroup(
                            group.Key,
                            group.Name,
                            group.Kind,
                            group.CompleteCount,
                            [
                                .. group.Members.Select((member, index) =>
                                    BrowserMetadataWireProjection.Project(
                                        BrowserSurfaceProjection.Member(
                                            population.Subject,
                                            member)) with
                                    {
                                        BaselineOrdinal = hasExactSelectors
                                            ? index + 1
                                            : null,
                                    }),
                            ]);
                    }),
                ]),
            diagnostics);
    }

    private static BrowserTypeMemberPopulationInspection Failed(
        string detail,
        string[] diagnostics) =>
        new(
            BrowserTypeMemberPopulationOutcome.Failed,
            detail,
            null,
            diagnostics);

    private static MetadataMemberSpelling ParseSpelling(string spelling) =>
        spelling.Trim().ToLowerInvariant() switch
        {
            "csharp" or "c#" => MetadataMemberSpelling.CSharp,
            "metadata" => MetadataMemberSpelling.Metadata,
            _ => throw new ArgumentException(
                "Spelling must be 'csharp' or 'metadata'.",
                nameof(spelling)),
        };

    private static MetadataMethodAccessibilityFilter ParseAccessibility(
        string accessibility) =>
        accessibility.Trim().ToLowerInvariant() switch
        {
            "all" => MetadataMethodAccessibilityFilter.All,
            "public" => MetadataMethodAccessibilityFilter.Public,
            "protected" => MetadataMethodAccessibilityFilter.Protected,
            "internal" => MetadataMethodAccessibilityFilter.Internal,
            "private" => MetadataMethodAccessibilityFilter.Private,
            _ => throw new ArgumentException(
                "Accessibility must be all, public, protected, internal, or private.",
                nameof(accessibility)),
        };

    private static string SerializeTypeMemberPopulation(
        BrowserTypeMemberPopulationInspection inspection) =>
        JsonSerializer.Serialize(
            inspection,
            BrowserMetadataJsonContext.Default
                .BrowserTypeMemberPopulationInspection);
}
