using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;

namespace DotnetInspect.Web.Interop.Analysis;

// Member-list heat for one Type: the compact Type heat record, without the
// raw metrics that the family detail result carries.
internal static partial class BrowserImplementationProfileWireProjection
{
    const int TypeHeatSchemaVersion = 1;

    internal static BrowserTypeImplementationHeat ProjectTypeHeat(
        InspectionEnvelope<
            AssemblyContextEntry<AssemblyTypeImplementationHeatInspection>>
                inspection,
        BrowserCompileLibraryAvailability compileLibrary)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(compileLibrary);

        BrowserAnalysisInspectionShare share =
            BrowserAnalysisInspectionProjection.Project(inspection.Share);
        BrowserAnalysisInspectionDiagnostic[] diagnostics =
        [
            .. inspection.Diagnostics.Select(
                BrowserAnalysisInspectionProjection.Project),
        ];
        return inspection.Content switch
        {
            AssemblyContextEntry<
                AssemblyTypeImplementationHeatInspection>.Available
                    available =>
                new(
                    TypeHeatSchemaVersion,
                    "available",
                    Project(available.Subject),
                    ProjectTypeHeat(available.Value),
                    Failure: null,
                    share,
                    diagnostics,
                    compileLibrary),
            AssemblyContextEntry<
                AssemblyTypeImplementationHeatInspection>.Rejected rejected =>
                new(
                    TypeHeatSchemaVersion,
                    "rejected",
                    Project(rejected.Subject),
                    Content: null,
                    new(
                        rejected.Failure.Kind.ToString(),
                        rejected.Failure.Detail,
                        rejected.Failure.MetadataRootReason?.ToString()),
                    share,
                    diagnostics,
                    compileLibrary),
            AssemblyContextEntry<
                AssemblyTypeImplementationHeatInspection>.Failed failed =>
                new(
                    TypeHeatSchemaVersion,
                    "failed",
                    Project(failed.Subject),
                    Content: null,
                    new(
                        failed.Error.GetType().Name,
                        failed.Error.Message,
                        MetadataRootReason: null),
                    share,
                    diagnostics,
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown type implementation-heat outcome."),
        };
    }

    internal static BrowserTypeImplementationHeat TypeHeatUnavailable(
        string kind,
        string detail,
        BrowserCompileLibraryAvailability compileLibrary) =>
        new(
            TypeHeatSchemaVersion,
            "unavailable",
            Subject: null,
            Content: null,
            new(kind, detail, MetadataRootReason: null),
            Share: null,
            Diagnostics: [],
            compileLibrary);

    static BrowserTypeImplementationHeatContent ProjectTypeHeat(
        AssemblyTypeImplementationHeatInspection inspection) =>
        new(
            inspection.TypeDefinitionId,
            [.. inspection.Families.Select(ProjectHeatFamily)],
            [.. inspection.Diagnostics.Select(Project)],
            [.. inspection.ApiSurfaceInspectionFailures.Select(Project)]);

    static BrowserImplementationHeatFamily ProjectHeatFamily(
        ImplementationHeatFamily family) =>
        new(
            family.Member,
            [
                .. family.Roster.Select(static member =>
                    new BrowserImplementationHeatRosterMember(
                        member.TypeDefinitionId,
                        member.StableSelector,
                        member.MetadataToken)),
            ],
            [
                .. family.Methods.Select(static method =>
                    new BrowserImplementationHeatMethod(
                        method.MetadataToken,
                        method.IsRosterMember,
                        method.HasBody,
                        method.Size,
                        method.IsTrivial,
                        method.IsComplete)),
            ],
            [
                .. family.Relationships.Select(static relationship =>
                    new BrowserImplementationHeatRelationship(
                        relationship.CallerToken,
                        relationship.CalleeToken)),
            ],
            [.. family.UnavailableBodies.Select(ProjectUnavailableBody)],
            [.. family.Diagnostics.Select(Project)]);

    static BrowserImplementationProfileUnavailableBody ProjectUnavailableBody(
        ImplementationProfileUnavailableBody body) =>
        new(
            body.EvidenceMethod is { } method ? MethodKey(method) : null,
            body.MethodToken,
            body.Reason.ToString(),
            body.Diagnostic is { } diagnostic ? Project(diagnostic) : null);
}
