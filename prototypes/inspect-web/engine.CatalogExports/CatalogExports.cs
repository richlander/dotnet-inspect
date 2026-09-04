using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

// The generated wwwroot/inspect-web-catalog.js module binds exports.CatalogExports.*, so this
// type stays in the global namespace. Its helpers and wire records live in
// InspectWeb.Engine.CatalogFacade.
using InspectWeb.Engine;
using InspectWeb.Engine.CatalogFacade;

/// <summary>
/// Product-owned static vocabulary and demo definitions plus product-owned workspace-share
/// transport.
/// </summary>
/// <remarks>
/// A demo run reaches the shared package/workspace services through
/// <c>InspectWeb.Engine.Core</c>; it does not call the package facade or reuse that facade's wire
/// records.
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    // Vocabulary is product-owned static data. The browser receives the same section/field/value
    // document as the CLI and retains no separate labels, ordering, defaults, or query semantics.
    [JSExport]
    public static string ListVocabulary() =>
        JsonSerializer.Serialize(
            BrowserVocabulary.ToBrowserDocument(
                DotnetInspector.Vocabulary.VocabularyJson.ToWireDocument(
                    DotnetInspector.Vocabulary.VocabularyCatalog.Document)),
            BrowserCatalogJsonContext.Default.BrowserVocabularyDocument);

    // Home demos are product-owned closed presets. Catalog listing is metadata-only; resolve
    // allocates one demo's definition graph. The browser builds share links / runners from the
    // projected coordinates rather than a hand-maintained TypeScript twin.
    [JSExport]
    public static string ListHomeDemos() =>
        JsonSerializer.Serialize(
            BrowserProductHomeDemos.ToCatalog(EcosystemPackCatalog.DiscoverDemos()),
            BrowserCatalogJsonContext.Default.BrowserHomeDemoCatalog);

    /// <summary>
    /// Resolves one product home demo. <c>found</c> is false when the id is unknown.
    /// </summary>
    [JSExport]
    public static string ResolveHomeDemo(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return JsonSerializer.Serialize(
                new BrowserHomeDemoResolveResult(false, null),
                BrowserCatalogJsonContext.Default.BrowserHomeDemoResolveResult);
        }

        EcosystemDemoSelectionResult selectionResult =
            EcosystemPackCatalog.SelectDemo(scenarioId);
        if (selectionResult is EcosystemDemoSelectionResult.Unknown)
        {
            return JsonSerializer.Serialize(
                new BrowserHomeDemoResolveResult(false, null),
                BrowserCatalogJsonContext.Default.BrowserHomeDemoResolveResult);
        }

        EcosystemDemoSelection selection =
            ((EcosystemDemoSelectionResult.Known)selectionResult).Selection;
        return JsonSerializer.Serialize(
            new BrowserHomeDemoResolveResult(
                true,
                BrowserProductHomeDemos.ToResolved(selection)),
            BrowserCatalogJsonContext.Default.BrowserHomeDemoResolveResult);
    }

    /// <summary>
    /// Runs one supported home demo from its product definition.
    /// The browser supplies only the scenario id: workspace coordinates,
    /// navigation focus, section selection, optional member selection, and
    /// query execution remain on the engine side.
    /// </summary>
    [JSExport]
    public static async Task<string> RunHomeDemo(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return JsonSerializer.Serialize(
                new BrowserHomeDemoRunResult(false, [], null, null),
                BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult);
        }

        EcosystemDemoSelectionResult selectionResult =
            EcosystemPackCatalog.SelectDemo(scenarioId);
        if (selectionResult is EcosystemDemoSelectionResult.Unknown)
        {
            return JsonSerializer.Serialize(
                new BrowserHomeDemoRunResult(false, [], null, null),
                BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult);
        }

        ResolvedScenario resolved =
            ((EcosystemDemoSelectionResult.Known)selectionResult).Selection.Scenario;
        BrowserHomeDemoRunPlan plan =
            BrowserProductHomeDemos.ToRunPlan(resolved);
        BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
                    plan.Requests,
                    deadline.Token),
                BrowserPackageWorkspace.PackageOperationTimeout);
        BrowserHomeDemoRunResult result =
            RunHomeDemoCore(plan, resolution);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult);
    }

    internal static BrowserHomeDemoRunResult RunHomeDemoCore(
        BrowserHomeDemoRunPlan plan,
        BrowserScopeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resolution);

        BrowserInspectionScope scope = resolution.Scope;
        BrowserPackageProjectionInfo[] projections =
        [
            .. resolution.RequestedCoordinates.Select(requested =>
                BrowserPackageSurfaceProjection.Project(
                    scope,
                    scope.Coordinate(requested))),
        ];
        if (resolution.RequestedCoordinates.Length != plan.Requests.Length)
        {
            throw new InvalidOperationException(
                "The product home demo workspace did not preserve its distinct request ordering.");
        }
        if ((uint)plan.FocusRequestIndex >= (uint)resolution.RequestedCoordinates.Length)
        {
            throw new InvalidOperationException(
                "The product home demo focus is outside its resolved browser workspace.");
        }

        BrowserPackageCoordinate focusCoordinate =
            scope.Coordinate(
                resolution.RequestedCoordinates[plan.FocusRequestIndex]);
        BrowserPackageProjectionInfo focusProjection =
            projections[plan.FocusRequestIndex];
        BrowserPackageSurfaceInfo focusPackage = focusProjection.Surface;
        BrowserTypeSurfaceInfo[] types =
        [
            .. focusPackage.Types.Where(type =>
                string.Equals(
                    type.Id,
                    plan.TypeId,
                    StringComparison.Ordinal)),
        ];
        if (types.Length != 1)
        {
            throw new InvalidOperationException(
                $"The product home demo type '{plan.TypeId}' resolved to "
                + $"{types.Length} browser surface rows.");
        }

        BrowserTypeSurfaceInfo type = types[0];
        BrowserHomeDemoRunMember? memberPlan = plan.Member;
        if (memberPlan is null)
        {
            return new BrowserHomeDemoRunResult(
                true,
                [
                    .. projections.Select(projection =>
                        BrowserCatalogWireProjection.Project(projection.Surface)),
                ],
                new BrowserHomeDemoRunActivation(
                    focusCoordinate.PackageId,
                    focusCoordinate.Version,
                    focusCoordinate.Framework,
                    type.Id,
                    plan.Section,
                    MemberName: null,
                    MemberKind: null,
                    MemberAnchorDigest: null,
                    MemberSection: null),
                null);
        }

        (ApiType Type, AssemblyContextSubject Subject)[] apiTypes =
        [
            .. (focusProjection.ApiSurfaces
                ?? throw new InvalidOperationException(
                    "The product home demo focus has no compile-library API surface."))
                .Assemblies.Assemblies
                .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
                .SelectMany(entry => entry.Value.Surface.Types
                    .Where(candidate => string.Equals(
                        AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(
                            candidate),
                        type.DefinitionId,
                        StringComparison.Ordinal))
                    .Select(candidate => (candidate, entry.Subject))),
        ];
        if (apiTypes.Length != 1)
        {
            throw new InvalidOperationException(
                $"The product home demo type '{plan.TypeId}' resolved to "
                + $"{apiTypes.Length} product API rows.");
        }

        (ApiType apiType, AssemblyContextSubject subject) = apiTypes[0];
        var selector = new MemberTargetSelector(
            $"{memberPlan.Name}~{memberPlan.AnchorDigest}",
            memberPlan.Name,
            DigestPrefix: memberPlan.AnchorDigest,
            Kind: memberPlan.MemberKind);
        MemberTargetResolution target =
            MemberTargetResolver.Resolve(apiType, selector);
        if (target.Diagnostic is { } diagnostic)
        {
            throw new InvalidOperationException(
                $"The product home demo member could not be selected: {diagnostic.Message}");
        }

        BrowserMemberSurfaceInfo projectedMember =
            BrowserSurfaceProjection.Member(
                apiType,
                target.Target!.ApiMember.Member);
        BrowserMemberSurfaceInfo[] transportedMembers =
        [
            .. type.Api.Where(member =>
                string.Equals(
                    member.AnchorDigest,
                    projectedMember.AnchorDigest,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (transportedMembers.Length != 1)
        {
            throw new InvalidOperationException(
                $"The selected product home demo member projected to "
                + $"{transportedMembers.Length} browser surface rows.");
        }

        BrowserMemberSurfaceInfo member = transportedMembers[0];
        if (!string.Equals(
                type.AssemblyName,
                subject.Identity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The product home demo type projection lost its owning assembly identity.");
        }
        BrowserMemberResolution.Resolved resolvedMember =
            BrowserMemberResolution.ResolveImplementationMember(
                scope,
                focusCoordinate,
                type.Assembly,
                type.DefinitionId,
                member.Name,
                member.GraphSelectorKey,
                member.MetadataToken ?? 0);
        BrowserWorkspaceParticipant participant = resolvedMember.ImplementationParticipant;
        Analysis.CallGraphMemberResolution memberResolution = resolvedMember.Member;
        MemberCallGraphView view = scope.UseImplementation(group =>
        {
            using var session = new MemberCallGraphSession(
                group,
                participant.Assembly,
                memberResolution.BodyToken);
            return session.HasCrossLibraryScope
                ? session.CrossLibrary()
                : session.Callers();
        });

        return new BrowserHomeDemoRunResult(
            true,
            [
                .. projections.Select(projection =>
                    BrowserCatalogWireProjection.Project(projection.Surface)),
            ],
            new BrowserHomeDemoRunActivation(
                focusCoordinate.PackageId,
                focusCoordinate.Version,
                focusCoordinate.Framework,
                type.Id,
                plan.Section,
                member.Name,
                member.Kind,
                member.AnchorDigest,
                memberPlan.MemberSection),
            BrowserCatalogWireProjection.Project(
                BrowserCallGraphProjection.Project(scope, view)));
    }
}
