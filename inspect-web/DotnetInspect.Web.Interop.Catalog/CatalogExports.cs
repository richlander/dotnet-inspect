using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Interop.Catalog;

/// <summary>
/// Product-owned static vocabulary and demo definitions plus product-owned workspace-share
/// transport.
/// </summary>
/// <remarks>
/// A demo run reaches the shared package/workspace services through
/// <c>DotnetInspect.Web.Core</c>; it does not call the package facade or reuse that facade's wire
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
        BrowserHomeDemoRunResult result = plan.Requests[0] switch
        {
            BrowserHomeDemoRunRequest.Package =>
                await RunPackageHomeDemoAsync(plan),
            BrowserHomeDemoRunRequest.Platform =>
                await RunPlatformHomeDemoAsync(plan),
            _ => throw new InvalidOperationException(
                "The product home demo run plan contains an unknown request kind."),
        };

        // Keep JSON return provenance outside async cleanup for the generated typed facade.
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

        BrowserPackageRequest[] requests =
        [
            .. plan.Requests.Select(request =>
                request is BrowserHomeDemoRunRequest.Package package
                    ? package.Request
                    : throw new InvalidOperationException(
                        "A package home demo run contains a non-package request.")),
        ];
        BrowserInspectionScope scope = resolution.Scope;
        BrowserPackageProjectionInfo[] projections =
        [
            .. resolution.RequestedCoordinates.Select(requested =>
                BrowserPackageSurfaceProjection.Project(
                    scope,
                    scope.Coordinate(requested))),
        ];
        if (resolution.RequestedCoordinates.Length != requests.Length)
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
                    FocusKind: "package",
                    FocusId: focusCoordinate.PackageId,
                    focusCoordinate.Version,
                    focusCoordinate.Framework,
                    FocusAssembly: null,
                    type.Id,
                    plan.Section,
                    MemberName: null,
                    MemberKind: null,
                    MemberAnchorDigest: null,
                    MemberSection: null),
                null);
        }

        BrowserHomeDemoSelectedMember selectedMember =
            SelectMember(
                plan,
                type,
                focusProjection.ApiSurfaces
                ?? throw new InvalidOperationException(
                    "The product home demo focus has no compile-library API surface."));
        AssemblyContextSubject subject = selectedMember.Subject;
        BrowserMemberSurfaceInfo member = selectedMember.Surface;
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
                FocusKind: "package",
                FocusId: focusCoordinate.PackageId,
                focusCoordinate.Version,
                focusCoordinate.Framework,
                FocusAssembly: null,
                type.Id,
                plan.Section,
                member.Name,
                member.Kind,
                member.AnchorDigest,
                memberPlan.MemberSection),
            BrowserCatalogWireProjection.Project(
                BrowserCallGraphProjection.Project(scope, view)));
    }

    static async Task<BrowserHomeDemoRunResult> RunPackageHomeDemoAsync(
        BrowserHomeDemoRunPlan plan)
    {
        BrowserPackageRequest[] requests =
        [
            .. plan.Requests.Select(request =>
                request is BrowserHomeDemoRunRequest.Package package
                    ? package.Request
                    : throw new InvalidOperationException(
                        "A package home demo run contains a non-package request.")),
        ];
        await using BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
                    requests,
                    deadline.Token),
                BrowserPackageWorkspace.PackageOperationTimeout);
        return RunHomeDemoCore(plan, resolution);
    }

    static async Task<BrowserHomeDemoRunResult> RunPlatformHomeDemoAsync(
        BrowserHomeDemoRunPlan plan)
    {
        BrowserHomeDemoRunRequest.Platform[] requests =
        [
            .. plan.Requests.Select(request =>
                request as BrowserHomeDemoRunRequest.Platform
                ?? throw new InvalidOperationException(
                    "A Platform home demo run contains a non-Platform request.")),
        ];
        string[] frameworks =
        [
            .. requests.Select(request => request.TargetFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        string[] versions =
        [
            .. requests.Select(request => request.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        if (frameworks.Length != 1 || versions.Length != 1)
        {
            throw new InspectionDefinitionException(
                "A Browser Platform home demo must use one exact target framework "
                + "and Platform version.");
        }

        return await BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                BrowserPlatformHomeDemoPreparation preparation;
                await using (BrowserPlatformScopeResolution resolution =
                    await BrowserPlatformWorkspace.OpenAssembliesAsync(
                        frameworks[0],
                        versions[0],
                        [
                            .. requests.Select(request =>
                                new BrowserPlatformAssemblyRequest(
                                    BrowserPlatformIdentity.AssemblyFileName(
                                        request.Assembly),
                                    BrowserPlatformWorkspace.Pack(
                                        request.Family))),
                        ],
                        deadline.Token))
                {
                    preparation = PreparePlatformHomeDemo(
                        plan,
                        resolution);
                }
                return await CompletePlatformHomeDemoAsync(
                    preparation,
                    deadline.Token);
            },
            BrowserPackageWorkspace.PackageOperationTimeout);
    }

    internal static BrowserPlatformHomeDemoPreparation
        PreparePlatformHomeDemo(
            BrowserHomeDemoRunPlan plan,
            BrowserPlatformScopeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resolution);

        BrowserHomeDemoRunRequest.Platform[] requests =
        [
            .. plan.Requests.Select(request =>
                request as BrowserHomeDemoRunRequest.Platform
                ?? throw new InvalidOperationException(
                    "A Platform home demo run contains a non-Platform request.")),
        ];
        if ((uint)plan.FocusRequestIndex >= (uint)requests.Length)
        {
            throw new InvalidOperationException(
                "The product home demo focus is outside its resolved Browser Platform workspace.");
        }

        BrowserPlatformProjectionInfo[] projections =
        [
            .. requests.Select(request =>
            {
                WorkspaceContextMember participant =
                    resolution.Scope.Participant(
                        request.Family,
                        request.Assembly);
                RealizedMemberCoordinate.Platform coordinate =
                    participant.Realized
                        as RealizedMemberCoordinate.Platform
                    ?? throw new InvalidOperationException(
                        "The Browser Platform workspace returned a non-Platform participant.");
                return BrowserPlatformSurfaceProjection.Project(
                    resolution.Scope,
                    participant,
                    coordinate,
                    participant.Participant.Assembly.AssetFileName);
            }),
        ];
        if (projections
                .Select(projection => projection.Coordinate)
                .Distinct()
                .Count() != requests.Length)
        {
            throw new InvalidOperationException(
                "The product home demo Platform workspace collapsed distinct "
                + "requests onto the same realized coordinate.");
        }
        BrowserPlatformProjectionInfo focusProjection =
            projections[plan.FocusRequestIndex];
        RealizedMemberCoordinate.Platform focusCoordinate =
            focusProjection.Coordinate;
        string focusFramework =
            BrowserFrameworkText.Require(resolution.Scope.Framework);
        string focusAssembly =
            focusProjection.Participant.Participant.Assembly.Identity.Name;
        BrowserTypeSurfaceInfo[] types =
        [
            .. focusProjection.Surface.Types.Where(type =>
                string.Equals(
                    type.DefinitionId,
                    plan.TypeId,
                    StringComparison.Ordinal)),
        ];
        if (types.Length != 1)
        {
            throw new InvalidOperationException(
                $"The product home demo type '{plan.TypeId}' resolved to "
                + $"{types.Length} Browser Platform surface rows.");
        }

        BrowserTypeSurfaceInfo type = types[0];
        BrowserHomeDemoRunMember? memberPlan = plan.Member;
        if (memberPlan is null)
        {
            return new BrowserPlatformHomeDemoPreparation(
                new BrowserHomeDemoRunResult(
                    true,
                    [
                        .. projections.Select(projection =>
                            BrowserCatalogWireProjection.Project(projection.Surface)),
                    ],
                    new BrowserHomeDemoRunActivation(
                        FocusKind: "platform",
                        FocusId: focusCoordinate.Family,
                        focusCoordinate.Version,
                        focusFramework,
                        FocusAssembly: focusAssembly,
                        type.Id,
                        plan.Section,
                        MemberName: null,
                        MemberKind: null,
                        MemberAnchorDigest: null,
                        MemberSection: null),
                    null),
                Graph: null);
        }

        BrowserHomeDemoSelectedMember selectedMember =
            SelectMember(
                plan,
                type,
                focusProjection.ApiSurfaces);
        BrowserMemberSurfaceInfo member = selectedMember.Surface;
        AssemblyReferenceIdentity assemblyIdentity =
            focusProjection.Participant.Participant.Assembly.Identity;
        return new BrowserPlatformHomeDemoPreparation(
            new BrowserHomeDemoRunResult(
                true,
                [
                    .. projections.Select(projection =>
                        BrowserCatalogWireProjection.Project(projection.Surface)),
                ],
                new BrowserHomeDemoRunActivation(
                    FocusKind: "platform",
                    FocusId: focusCoordinate.Family,
                    focusCoordinate.Version,
                    focusFramework,
                    FocusAssembly: focusAssembly,
                    type.Id,
                    plan.Section,
                    member.Name,
                    member.Kind,
                    member.AnchorDigest,
                    memberPlan.MemberSection),
                CallGraph: null),
            new BrowserPlatformHomeDemoGraphRequest(
                focusFramework,
                focusCoordinate.Version,
                focusAssembly,
                BrowserPlatformWorkspace.Pack(focusCoordinate.Family),
                (assemblyIdentity.Version
                    ?? throw new InvalidOperationException(
                        "The Platform home demo assembly has no metadata version."))
                    .ToString(),
                assemblyIdentity.Culture,
                assemblyIdentity.PublicKeyToken,
                type.DefinitionId,
                member.Name,
                member.GraphSelectorKey,
                member.MetadataToken ?? 0));
    }

    static async Task<BrowserHomeDemoRunResult>
        CompletePlatformHomeDemoAsync(
            BrowserPlatformHomeDemoPreparation preparation,
            CancellationToken cancellationToken = default)
    {
        if (preparation.Graph is not { } request)
            return preparation.Result;

        BrowserCallGraphInfo graph =
            await BrowserPlatformCallGraph.QueryAsync(
                request.TargetFramework,
                request.PlatformVersion,
                request.Assembly,
                request.Pack,
                request.AssemblyVersion,
                request.AssemblyCulture,
                request.AssemblyPublicKeyToken,
                request.TypeFullName,
                request.MemberName,
                request.SelectorKey,
                request.MetadataToken,
                cancellationToken);
        return preparation.Result with
        {
            CallGraph = BrowserCatalogWireProjection.Project(graph),
        };
    }

    internal static async Task<BrowserHomeDemoRunResult>
        CompletePlatformHomeDemoAsync(
            BrowserPlatformHomeDemoPreparation preparation,
            HttpClient client,
            IPackageSourceAuthorization sourceAuthorization,
            TimeSpan operationTimeout,
            CancellationToken cancellationToken = default)
    {
        if (preparation.Graph is not { } request)
            return preparation.Result;

        BrowserCallGraphInfo graph =
            await BrowserPlatformCallGraph.QueryAsync(
                request.TargetFramework,
                request.PlatformVersion,
                request.Assembly,
                request.Pack,
                request.AssemblyVersion,
                request.AssemblyCulture,
                request.AssemblyPublicKeyToken,
                request.TypeFullName,
                request.MemberName,
                request.SelectorKey,
                request.MetadataToken,
                client,
                sourceAuthorization,
                operationTimeout,
                cancellationToken);
        return preparation.Result with
        {
            CallGraph = BrowserCatalogWireProjection.Project(graph),
        };
    }

    static BrowserHomeDemoSelectedMember SelectMember(
        BrowserHomeDemoRunPlan plan,
        BrowserTypeSurfaceInfo type,
        AssemblyContextApiSurfaceResult apiSurfaces)
    {
        BrowserHomeDemoRunMember memberPlan =
            plan.Member
            ?? throw new InvalidOperationException(
                "The product home demo run plan has no member selection.");
        (ApiType Type, AssemblyContextSubject Subject)[] apiTypes =
        [
            .. apiSurfaces.Assemblies.Assemblies
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

        return new BrowserHomeDemoSelectedMember(
            subject,
            transportedMembers[0]);
    }

    sealed record BrowserHomeDemoSelectedMember(
        AssemblyContextSubject Subject,
        BrowserMemberSurfaceInfo Surface);

    internal sealed record BrowserPlatformHomeDemoPreparation(
        BrowserHomeDemoRunResult Result,
        BrowserPlatformHomeDemoGraphRequest? Graph);

    internal sealed record BrowserPlatformHomeDemoGraphRequest(
        string TargetFramework,
        string PlatformVersion,
        string Assembly,
        string Pack,
        string AssemblyVersion,
        string? AssemblyCulture,
        string? AssemblyPublicKeyToken,
        string TypeFullName,
        string MemberName,
        string SelectorKey,
        int MetadataToken);
}
