using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.Research;
using Analysis = ILInspector.Analysis;
using Pipeline = ILInspector.Decompiler.Pipeline;

// The bridge in wwwroot/engine.js binds exports.BrowserInspectionEngine.*, so this type stays
// in the global namespace. Its helpers live in InspectWeb.Engine.
using InspectWeb.Engine;

/// <summary>
/// The browser's exported inspection surface.
/// </summary>
/// <remarks>
/// <para>
/// An export that inspects an assembly resolves an exact package/version/framework identity,
/// opens a <see cref="BrowserInspectionScope"/> over it, and hands that scope's
/// <see cref="AssemblyContextGroup"/> to a public product query that owns the session. No export
/// — and no helper the engine owns — opens an <see cref="AssemblyInspectionSession"/>, a
/// <c>MetadataSource</c>, an Analysis index, or a retained image descriptor. An operation with no
/// such query is exported as explicitly unsupported; see
/// <c>BrowserUnsupportedOperations.cs</c>.
/// </para>
/// <para>
/// Two other categories exist and say so in place: exports that read package content without
/// inspecting an assembly (the document and XML-documentation reads), and exports that touch no
/// artifact at all (type-name ranking, cache statistics, and the style catalog).
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class BrowserInspectionEngine
{
    /// <summary>
    /// The package type surface for one exact package/version/framework workspace, produced by
    /// <see cref="AssemblyContextApiSurfaceQuery"/> over the workspace's own group. The query owns
    /// every session and every accessibility bucket; this method adapts its typed models and
    /// composes no evidence, no classification, and no ordering of its own.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackage(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];

        // Only this coordinate's assemblies are projected. A composite workspace may hold several
        // packages, and projecting all of them here materialized every other package's surface
        // only to discard it.
        BrowserWorkspaceParticipant[] requested =
        [
            .. scope.SurfaceParticipants.Where(candidate =>
                candidate.Coordinate.Key.Equals(coordinate.Key, StringComparison.Ordinal)),
        ];

        // The site's default path shows public types by default and reaches non-public ones
        // through the accessibility filter, so it asks for the composed scope: a public type
        // keeps its public member list even though non-public types are present. The projection
        // runs under the browser's explicit bounds; an early stop is reported, never silent.
        AssemblyContextApiSurfaceResult surfaces = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits,
                [.. requested.Select(participant => participant.Participant)]));
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                surfaces,
                [
                    .. requested.Select(participant =>
                        new BrowserSurfaceProjection.Participant(
                            participant.Participant,
                            participant.Asset.AssemblyName,
                            participant.Asset.Id,
                            participant.Asset.Path)),
                ]);
        if (projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"No assembly of {coordinate.PackageId} {coordinate.Version} for "
                + $"{coordinate.Framework} produced an API surface. "
                + (projected.InspectionError
                    ?? "The workspace reported no failure."));
        }

        string defaultAssemblyId = projected.Assemblies.FirstOrDefault(
                assembly => assembly.Id.Equals(
                    coordinate.DefaultAsset.Id,
                    StringComparison.Ordinal))
            ?.Id
            ?? projected.Assemblies.FirstOrDefault()?.Id
            ?? coordinate.DefaultAsset.Id;

        return JsonSerializer.Serialize(
            new BrowserPackageSurface(
                coordinate.PackageId,
                coordinate.Version,
                [.. coordinate.Selection.AvailableTargetFrameworks],
                coordinate.Framework,
                defaultAssemblyId,
                projected.Assemblies,
                projected.Types,
                projected.Accessibility,
                projected.TotalMembers,
                [.. coordinate.Package.Documents()],
                projected.InspectionError),
            BrowserJsonContext.Default.BrowserPackageSurface);
    }

    /// <summary>
    /// One type's metadata projection, produced by
    /// <see cref="AssemblyContextTypeProjectionQuery"/> over the participant that owns the type.
    /// The query owns the metadata source and resolves references through the group's binding
    /// policy; nothing here opens a source or reads an image.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryTypeProjection(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId)
    {
        (BrowserInspectionScope scope, BrowserWorkspaceParticipant participant) =
            await SurfaceParticipantAsync(packageId, version, targetFramework, assemblyName);

        ResearchViews.TypeProjectionResult projection = BrowserSurfaceProjection.Require(
            scope.UseSurfaceParticipant(
                participant,
                (group, member) => AssemblyContextTypeProjectionQuery.ExecuteParticipant(
                    group,
                    member,
                    new AssemblyContextTypeProjectionRequest(typeId))),
            $"Type projection for '{typeId}'");

        return JsonSerializer.Serialize(
            new BrowserTypeMetadata(
                projection.Identity.FullName,
                projection.Identity.Namespace,
                projection.Identity.Name,
                projection.Identity.Kind,
                [.. projection.Identity.Modifiers],
                projection.Identity.Accessibility,
                projection.Identity.Assembly,
                projection.BaseType,
                [.. projection.Interfaces],
                [.. projection.DerivedTypes],
                [
                    .. projection.TypeParameters.Select(parameter => new BrowserTypeParameter(
                        parameter.Name,
                        parameter.Variance,
                        [.. parameter.Constraints])),
                ],
                [.. projection.Attributes],
                projection.EnumUnderlyingType,
                projection.Composition is { } composition
                    ? new BrowserTypeComposition(
                        composition.Methods,
                        composition.Properties,
                        composition.Fields,
                        composition.Events,
                        composition.Constructors,
                        composition.Operators,
                        composition.ExplicitInterfaceImplementations,
                        composition.ExtensionMethods,
                        composition.Static,
                        composition.Unsafe,
                        composition.Async,
                        composition.Virtual,
                        composition.Abstract,
                        composition.Override,
                        composition.Extension,
                        composition.Obsolete,
                        composition.Total)
                    : null,
                [
                    .. projection.Graph?.Nodes.Select(node => new BrowserTypeGraphNode(
                        node.Id,
                        node.DisplayName,
                        node.Role.ToString().ToLowerInvariant())) ?? [],
                ],
                [
                    .. projection.Graph?.Edges.Select(edge => new BrowserTypeGraphEdge(
                        edge.FromId,
                        edge.ToId,
                        edge.Kind.ToString().ToLowerInvariant())) ?? [],
                ],
                [
                    .. projection.InspectionFailures.Select(
                        failure => $"{failure.Operation}: {failure.Detail}"),
                ]),
            BrowserJsonContext.Default.BrowserTypeMetadata);
    }

    /// <summary>
    /// One member's portable <c>AnnotatedSourceDocument</c>, produced by
    /// <see cref="AssemblyContextMemberProjectionQuery"/> over the participant that owns the
    /// member's implementation. The document is serialized by its owning product context, so the
    /// payload is the same artifact the CLI emits and the viewer validates.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberAnnotatedSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson)
    {
        _ = memberSignature;
        (
            BrowserInspectionScope scope,
            BrowserWorkspaceParticipant participant,
            Analysis.CallGraphMemberResolution resolution
        ) = await ImplementationMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken);

        AssemblyMemberProjection projection = BrowserSurfaceProjection.Require(
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                    group,
                    member,
                    new AssemblyContextMemberProjectionRequest(
                        typeQueryId,
                        memberName,
                        MethodToken: resolution.BodyToken,
                        SourceDocument: true,
                        PrinterOptions: BrowserStyleOptions.Resolve(styleOptionsJson)))),
            $"Annotated source for '{typeQueryId}.{memberName}'");

        if (projection.Projection.SourceDocument is not { } document)
        {
            IReadOnlyList<DecompilerDiagnostic> diagnostics =
                projection.Projection.SourceDocumentFailure?.Diagnostics ?? [];
            throw new InvalidOperationException(
                diagnostics.Count > 0
                    ? $"Annotated source projection failed: "
                        + string.Join("; ", diagnostics.Select(item => item.ToString()))
                    : "Annotated source projection produced no document.");
        }

        // The document's wire shape belongs to ILInspector.Decompiler; carry it verbatim so the
        // viewer's model validates the same artifact the CLI writes.
        using var serialized = JsonDocument.Parse(
            JsonSerializer.Serialize(
                document,
                AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument));
        return JsonSerializer.Serialize(
            new BrowserAnnotatedSource(
                serialized.RootElement,
                $"Annotated by dotnet-inspect from {participant.Coordinate.PackageId} "
                    + $"{participant.Coordinate.Version} {participant.Asset.Path}",
                projection.ContextLimitation is { } limitation
                    ? $"{limitation.Kind}: {limitation.Detail}"
                    : null),
            BrowserJsonContext.Default.BrowserAnnotatedSource);
    }

    /// <summary>
    /// Declared NuGet dependency groups plus the selected compile assembly's direct references.
    /// Package parsing and exact-framework selection belong to
    /// <see cref="PackageDependencyGroupsQuery"/>; the assembly-context query owns the metadata
    /// session. This method only adapts their typed results for the browser.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageDependencies(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];

        PackageDependencyGroupsResult dependencyResult =
            await PackageDependencyGroupsQuery.ExecuteAsync(
                coordinate.Package.Content,
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework);
        PackageDependencyGroups dependencies = dependencyResult switch
        {
            PackageDependencyGroupsResult.Available available => available.Value,
            PackageDependencyGroupsResult.NoManifest =>
                throw new InvalidDataException(
                    "The package contains no root manifest."),
            PackageDependencyGroupsResult.Failed failed =>
                throw new InvalidOperationException(
                    failed.Error.Message,
                    failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown package dependency-group query result."),
        };

        PackageCompileAsset asset = coordinate.CompileAsset(assemblyId);
        BrowserWorkspaceParticipant participant =
            scope.SurfaceParticipant(coordinate, asset);
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>> referenceResult =
            scope.UseSurfaceParticipant(
                participant,
                AssemblyContextReferencesQuery.ExecuteParticipant);

        BrowserAssemblyReference[] assemblyReferences = [];
        string? assemblyReferenceError = null;
        switch (referenceResult)
        {
            case AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Available available:
                assemblyReferences =
                [
                    .. available.Value
                        .Select(reference => reference.ToReference())
                        .OrderBy(
                            reference => reference.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            reference => reference.Version,
                            StringComparer.Ordinal)
                        .ThenBy(
                            reference => reference.Culture,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            reference => reference.PublicKeyToken,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(reference => new BrowserAssemblyReference(
                            reference.Name,
                            reference.Version,
                            reference.Culture,
                            reference.PublicKeyToken)),
                ];
                break;
            case AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Rejected rejected:
                assemblyReferenceError =
                    $"{rejected.Failure.Kind} ({rejected.Failure.Detail})";
                break;
            case AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Failed failed:
                assemblyReferenceError = failed.Error.Message;
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown assembly-context reference query result.");
        }

        string? dependencyGroupError =
            dependencies.SelectionStatus
                == PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework
                    ? "The manifest declares no dependency group for the active target framework."
                    : null;
        return JsonSerializer.Serialize(
            new BrowserPackageDependencies(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                asset.AssemblyName,
                [
                    .. dependencies.Groups.Select((group, index) =>
                        new BrowserPackageDependencyGroup(
                            index,
                            group.TargetFramework,
                            index == dependencies.SelectedGroupIndex,
                            [
                                .. group.Dependencies.Select(dependency =>
                                    new BrowserPackageDependency(
                                        dependency.Id,
                                        dependency.VersionRange)),
                            ])),
                ],
                assemblyReferences,
                dependencyGroupError,
                assemblyReferenceError),
            BrowserJsonContext.Default.BrowserPackageDependencies);
    }

    /// <summary>
    /// Ecosystem integration evidence for one package/version/framework workspace, produced by
    /// <see cref="AssemblyContextIntegrationsQuery"/> over the workspace's own group. The query
    /// owns every session; this method groups its signals for display and composes no evidence.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageIntegrations(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];

        // The workspace is retained and reused, so this runs the whole-group query rather than
        // the streaming per-participant form: that form's release is terminal for the released
        // participant, which would leave the reused group unable to answer a later query.
        AssemblyContextIntegrationsResult result =
            scope.UseImplementationOrSurface(AssemblyContextIntegrationsQuery.Execute);
        if (scope.ImplementationParticipants.Length > 0
            && scope.ReferenceOnlySurfaceParticipants.Length > 0)
        {
            result = new AssemblyContextIntegrationsResult(
            [
                .. result.Assemblies,
                .. scope.ReferenceOnlySurfaceParticipants.Select(participant =>
                    scope.UseSurfaceParticipant(
                        participant,
                        AssemblyContextIntegrationsQuery.ExecuteParticipant)),
            ]);
        }

        return SerializeIntegrations(
            coordinate.PackageId,
            coordinate.Version,
            coordinate.Framework,
            result.Assemblies);
    }

    /// <summary>
    /// Missing ecosystem integration opportunities for one package/version/framework workspace.
    /// The product query composes them from its typed Integrations prerequisite; the browser only
    /// groups and deduplicates the returned evidence for presentation.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageOpportunities(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];

        var registry = new InspectionQueryRegistry<AssemblyContextGroup>()
            .Add(
                AssemblyContextIntegrationsQuery.Definition,
                AssemblyContextIntegrationsQuery.Execute)
            .Add(
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Execute,
                AssemblyContextIntegrationsQuery.Definition);
        AssemblyContextIntegrationOpportunitiesResult result =
            scope.UseSurface(group =>
                registry.Run(
                        [AssemblyContextIntegrationOpportunitiesQuery.Definition],
                        group)
                    .Get(AssemblyContextIntegrationOpportunitiesQuery.Definition));

        return SerializeOpportunities(
            coordinate.PackageId,
            coordinate.Version,
            coordinate.Framework,
            result.Assemblies);
    }

    static string SerializeIntegrations(
        string package,
        string version,
        string framework,
        IEnumerable<AssemblyIntegrationsEntry> entries)
    {
        AssemblyIntegrationsEntry[] materialized = [.. entries];
        var failures = new List<string>();
        var signals = new List<EcosystemIntegrationSignalInfo>();
        foreach (AssemblyIntegrationsEntry entry in materialized)
        {
            switch (entry)
            {
                case AssemblyIntegrationsEntry.Available available:
                    signals.AddRange(available.EcosystemSignals);
                    break;
                case AssemblyIntegrationsEntry.Rejected rejected:
                    failures.Add(
                        $"{rejected.Subject.Identity.Name}: {rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})");
                    break;
                case AssemblyIntegrationsEntry.Failed failed:
                    failures.Add(
                        $"{failed.Subject.Identity.Name}: {failed.Error.Message}");
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown assembly integrations entry '{entry.GetType().Name}'.");
            }
        }

        return JsonSerializer.Serialize(
            new BrowserPackageIntegrations(
                package,
                version,
                framework,
                [
                    .. signals
                        .GroupBy(
                            signal => signal.Integration,
                            StringComparer.Ordinal)
                        .OrderBy(
                            group => group.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => new BrowserIntegrationCategory(
                            group.Key,
                            [
                                .. group
                                    .Select(signal =>
                                        new BrowserIntegrationSignal(
                                            signal.Kind,
                                            signal.Name,
                                            signal.Shape))
                                    .DistinctBy(signal =>
                                        (signal.Kind,
                                            signal.Name,
                                            signal.Shape))
                                    .OrderBy(
                                        signal => signal.Name,
                                        StringComparer.OrdinalIgnoreCase),
                            ])),
                ],
                signals.Count,
                materialized.All(
                    entry => entry
                        is AssemblyIntegrationsEntry.Available),
                failures.Count == 0
                    ? null
                    : string.Join("; ", failures)),
            BrowserJsonContext.Default.BrowserPackageIntegrations);
    }

    static string SerializeOpportunities(
        string package,
        string version,
        string framework,
        IEnumerable<AssemblyIntegrationOpportunitiesEntry> entries)
    {
        AssemblyIntegrationOpportunitiesEntry[] materialized =
            [.. entries];
        var failures = new List<string>();
        var opportunities =
            new List<(
                AssemblyReferenceIdentity Source,
                IntegrationOpportunityInfo Opportunity)>();
        foreach (AssemblyIntegrationOpportunitiesEntry entry in materialized)
        {
            switch (entry)
            {
                case AssemblyIntegrationOpportunitiesEntry.Available available:
                    opportunities.AddRange(
                        available.Opportunities.Select(
                            opportunity =>
                                (available.Subject.Identity, opportunity)));
                    break;
                case AssemblyIntegrationOpportunitiesEntry.Rejected rejected:
                    failures.Add(
                        $"{rejected.Subject.Identity.Name}: {rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})");
                    break;
                case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                    failures.Add(
                        $"{failed.Subject.Identity.Name}: {failed.Error.Message}");
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly integration-opportunities entry "
                        + $"'{entry.GetType().Name}'.");
            }
        }

        (AssemblyReferenceIdentity Source, IntegrationOpportunityInfo Opportunity)[]
            distinctOpportunities =
        [
            .. opportunities.DistinctBy(item =>
                (item.Source,
                    item.Opportunity.Integration,
                    item.Opportunity.Api,
                    item.Opportunity.IntegrationType)),
        ];
        return JsonSerializer.Serialize(
            new BrowserPackageOpportunities(
                package,
                version,
                framework,
                [
                    .. distinctOpportunities
                        .GroupBy(
                            item => item.Opportunity.Integration,
                            StringComparer.Ordinal)
                        .OrderBy(
                            group => group.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => new BrowserOpportunityCategory(
                            group.Key,
                            [
                                .. group
                                    .OrderBy(
                                        item => item.Opportunity.Api,
                                        StringComparer.OrdinalIgnoreCase)
                                    .Select(item =>
                                        new BrowserOpportunityItem(
                                            item.Opportunity.Api,
                                            item.Opportunity.IntegrationType,
                                            item.Opportunity.LookFor,
                                            item.Opportunity
                                                .GetSourceTypeDefinition()?
                                                .ToEscapedFullName(),
                                            item.Source.Name,
                                            item.Source.Version?.ToString()
                                                ?? "",
                                            item.Source.Culture,
                                            item.Source.PublicKeyToken)),
                            ])),
                ],
                distinctOpportunities.Length,
                materialized.All(
                    entry => entry
                        is AssemblyIntegrationOpportunitiesEntry.Available),
                failures.Count == 0
                    ? null
                    : string.Join("; ", failures)),
            BrowserJsonContext.Default.BrowserPackageOpportunities);
    }

    /// <summary>
    /// A progressively acquired member call graph, produced by <see cref="MemberCallGraphSession"/>
    /// over one workspace spanning every package the site currently has open. Callers in a sibling
    /// package are only visible when that package is a participant of the same binding-consistent
    /// group, so the workspace is opened over the whole set rather than one package at a time.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberCallGraph(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string workspaceJson)
    {
        _ = memberSignature;
        _ = typeQueryId;
        if (metadataToken == 0)
        {
            throw new InvalidOperationException(
                "A call graph needs the selected overload's method-body token.");
        }

        var requests = new List<BrowserPackageRequest>
        {
            new(packageId, version, targetFramework),
        };
        foreach (BrowserWorkspacePackage entry in JsonSerializer.Deserialize(
            workspaceJson,
            BrowserJsonContext.Default.BrowserWorkspacePackageArray) ?? [])
        {
            requests.Add(new BrowserPackageRequest(
                entry.Package,
                entry.Version,
                string.IsNullOrWhiteSpace(entry.Framework) ? null : entry.Framework));
        }

        BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(requests);
        BrowserInspectionScope scope = resolution.Scope;
        BrowserPackageCoordinate rootCoordinate =
            scope.Coordinate(resolution.RequestedCoordinates[0]);
        (
            BrowserWorkspaceParticipant participant,
            Analysis.CallGraphMemberResolution memberResolution
        ) = ResolveImplementationMember(
            scope,
            rootCoordinate,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken);

        MemberCallGraphView view = scope.UseImplementation(group =>
        {
            using var session = new MemberCallGraphSession(
                group,
                participant.Assembly,
                memberResolution.BodyToken);
            return session.HasCrossLibraryScope ? session.CrossLibrary() : session.Callers();
        });

        CallGraphProjection projection = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);
        int callerAssemblies = scope.ImplementationParticipants
            .Select(candidate => candidate.Assembly.Identity.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return JsonSerializer.Serialize(
            new BrowserCallGraph(
                Mermaid(projection),
                Tree(view.CallerRoot),
                Tree(view.CalleeRoot),
                new BrowserCallGraphScope(
                    scope.Coordinates.Length,
                    scope.ImplementationParticipants.Length,
                    callerAssemblies,
                    view.Tier.ToString()),
                Targets(
                    projection.Nodes,
                    scope.ImplementationParticipants.Select(
                        participant => participant.Assembly.Identity)),
                Diagnostics(
                    view.Diagnostics,
                    projection.HasUnexploredTraversalBoundary,
                    projection.HasAnalysisFailureBoundary),
                NoBody: view.CalleeRoot is null && view.CallerRoot is null),
            BrowserJsonContext.Default.BrowserCallGraph);
    }

    /// <summary>
    /// The UTF-8 text of one package-shipped Markdown document, identified by its exact package
    /// entry path. Only paths the package's own document manifest lists are served. This reads
    /// package content and inspects no assembly, so it opens no group.
    /// </summary>
    [JSExport]
    public static async Task<string> GetPackageDocument(string packageId, string version, string path)
    {
        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(packageId, version);
        return JsonSerializer.Serialize(
            package.ReadDocument(path),
            BrowserJsonContext.Default.BrowserPackageDocumentContent);
    }

    /// <summary>
    /// One member's entry from the XML documentation shipped beside the product-selected compile
    /// asset. This reads package content and inspects no assembly, so it opens no group.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberDocumentation(
        string packageId,
        string version,
        string framework,
        string assemblyName,
        string documentationId)
    {
        BrowserPackageCoordinate coordinate = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            framework);
        PackageCompileAsset asset = coordinate.CompileAsset(assemblyName);
        BrowserMemberDocumentation documentation = coordinate.Package.TryReadText(
            Path.ChangeExtension(asset.Path, ".xml"),
            out byte[] xml)
                ? BrowserXmlDocumentation.Read(xml, documentationId)
                : BrowserXmlDocumentation.Empty;
        return JsonSerializer.Serialize(
            documentation,
            BrowserJsonContext.Default.BrowserMemberDocumentation);
    }

    /// <summary>
    /// Ranks loaded type candidates against an incremental query through the product's
    /// <see cref="TypeMatcher"/>: exact and namespace-suffix matches, then prefix and substring
    /// globs, then a Levenshtein "did you mean" fallback. This inspects no artifact — the
    /// candidates are names the client already holds — so it opens no workspace.
    /// </summary>
    [JSExport]
    public static string SearchTypes(string query, string candidatesJson)
    {
        BrowserTypeCandidate[] candidates = JsonSerializer.Deserialize(
            candidatesJson,
            BrowserJsonContext.Default.BrowserTypeCandidateArray) ?? [];
        query = query?.Trim() ?? "";

        if (query.Length == 0)
        {
            return JsonSerializer.Serialize(
                candidates
                    .OrderBy(candidate => candidate.Name.Length)
                    .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .Select(candidate => new BrowserTypeSearchHit(candidate.Key, "all"))
                    .ToArray(),
                BrowserJsonContext.Default.BrowserTypeSearchHitArray);
        }

        var hits = new List<BrowserTypeSearchHit>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        void AddTier(string kind, Func<BrowserTypeCandidate, bool> predicate)
        {
            foreach (BrowserTypeCandidate candidate in candidates
                .Where(candidate => !used.Contains(candidate.Key) && predicate(candidate))
                .OrderBy(candidate => candidate.Name.Length)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (used.Add(candidate.Key))
                    hits.Add(new BrowserTypeSearchHit(candidate.Key, kind));
            }
        }

        AddTier("exact", candidate => TypeMatcher.Matches(candidate.Full, query));
        AddTier("prefix", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, query + "*"));
        AddTier("substring", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, "*" + query + "*"));
        AddTier("path", candidate => TypeMatcher.MatchesTypeFilter(candidate.Full, "*" + query + "*"));

        var remaining = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (BrowserTypeCandidate candidate in candidates.Where(candidate => !used.Contains(candidate.Key)))
        {
            if (!remaining.TryGetValue(candidate.Full, out List<string>? keys))
                remaining[candidate.Full] = keys = [];
            keys.Add(candidate.Key);
        }

        if (remaining.Count > 0)
        {
            foreach ((string name, _) in TypeMatcher.FindClosest(
                remaining.Keys,
                query,
                minSimilarity: 0.5,
                maxResults: 8))
            {
                if (!remaining.TryGetValue(name, out List<string>? keys))
                    continue;
                foreach (string key in keys)
                {
                    if (used.Add(key))
                        hits.Add(new BrowserTypeSearchHit(key, "fuzzy"));
                }
            }
        }

        return JsonSerializer.Serialize(
            hits.Take(40).ToArray(),
            BrowserJsonContext.Default.BrowserTypeSearchHitArray);
    }

    /// <summary>Session acquisition statistics. Inspects no artifact and opens no workspace.</summary>
    [JSExport]
    public static string PackageCacheStats() => JsonSerializer.Serialize(
        BrowserPackageWorkspace.Stats(),
        BrowserJsonContext.Default.BrowserPackageCacheStats);

    /// <summary>Version, source revision, and build time embedded in this browser engine.</summary>
    [JSExport]
    public static string BuildIdentity() => JsonSerializer.Serialize(
        BrowserBuildIdentityReader.Read(typeof(BrowserInspectionEngine).Assembly),
        BrowserJsonContext.Default.BrowserBuildIdentity);

    /// <summary>
    /// Published package versions from the browser acquisition owner's bounded version-index
    /// reader. The JavaScript host does not fetch or parse the untrusted index independently.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageVersions(string packageId) =>
        JsonSerializer.Serialize(
            await BrowserPackageWorkspace.GetVersionsAsync(packageId),
            BrowserJsonContext.Default.StringArray);

    [JSExport]
    public static Task<string> ResolvePackageDependencyVersion(
        string packageId,
        string? declaredRange) =>
        BrowserPackageWorkspace.ResolveDependencyVersionAsync(
            packageId,
            declaredRange);

    [JSExport]
    public static string MatchPackageDependencyCoordinate(
        string packageId,
        string? declaredRange,
        string candidatesJson)
    {
        BrowserDependencyCoordinateCandidate[] candidates = JsonSerializer.Deserialize(
            candidatesJson,
            BrowserJsonContext.Default.BrowserDependencyCoordinateCandidateArray) ?? [];
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            candidates.Select(candidate => new PackageDependencyCoordinateCandidate(
                candidate.Key,
                candidate.Provenance switch
                {
                    BrowserDependencyCoordinateProvenance.NuGetPackage =>
                        PackageDependencyCoordinateKind.NuGetPackage,
                    BrowserDependencyCoordinateProvenance.PlatformRuntime =>
                        PackageDependencyCoordinateKind.PlatformRuntime,
                    _ => throw new InvalidOperationException(
                        "The dependency-coordinate provenance is invalid."),
                },
                candidate.PackageId,
                candidate.Version,
                candidate.TargetFramework)),
            packageId,
            declaredRange);
        var browserResult = new BrowserDependencyCoordinateMatch(
            result.Status switch
            {
                PackageDependencyCoordinateMatchStatus.NoMatch =>
                    BrowserDependencyCoordinateMatchOutcome.NoMatch,
                PackageDependencyCoordinateMatchStatus.Unique =>
                    BrowserDependencyCoordinateMatchOutcome.Unique,
                PackageDependencyCoordinateMatchStatus.Ambiguous =>
                    BrowserDependencyCoordinateMatchOutcome.Ambiguous,
                _ => throw new InvalidOperationException(
                    "The dependency-coordinate match outcome is invalid."),
            },
            result.CandidateKey);
        return JsonSerializer.Serialize(
            browserResult,
            BrowserJsonContext.Default.BrowserDependencyCoordinateMatch);
    }

    // Vocabulary is product-owned static data. The browser receives the same section/field/value
    // document as the CLI and retains no separate labels, ordering, defaults, or query semantics.
    [JSExport]
    public static string ListVocabulary() =>
        DotnetInspector.Vocabulary.VocabularyJson.Serialize(
            DotnetInspector.Vocabulary.VocabularyCatalog.Document);

    /// <summary>
    /// Resolves one exact package/version/framework coordinate, reuses its workspace, and returns
    /// the reference-preferred participant for one product-selected compile asset.
    /// </summary>
    static async Task<(BrowserInspectionScope Scope, BrowserWorkspaceParticipant Participant)>
        SurfaceParticipantAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];

        return (scope, scope.SurfaceParticipant(
            coordinate,
            coordinate.CompileAsset(assemblyName)));
    }

    /// <summary>
    /// Resolves a reference-surface body selector onto its implementation participant. Metadata
    /// tokens are image-local, so the product resolver validates the token and falls back to the
    /// opaque structural selector when <c>ref/</c> and <c>lib/</c> row numbers differ.
    /// </summary>
    static async Task<(
        BrowserInspectionScope Scope,
        BrowserWorkspaceParticipant Participant,
        Analysis.CallGraphMemberResolution Resolution)> ImplementationMemberAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeId,
            string memberName,
            string selectorKey,
            int metadataToken,
            CancellationToken cancellationToken = default)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        (
            BrowserWorkspaceParticipant participant,
            Analysis.CallGraphMemberResolution resolution
        ) = ResolveImplementationMember(
            scope,
            coordinate,
            assemblyName,
            typeId,
            memberName,
            selectorKey,
            metadataToken);
        return (scope, participant, resolution);
    }

    static (
        BrowserWorkspaceParticipant Participant,
        Analysis.CallGraphMemberResolution Resolution)
        ResolveImplementationMember(
            BrowserInspectionScope scope,
            BrowserPackageCoordinate coordinate,
            string assemblyName,
            string typeId,
            string memberName,
            string selectorKey,
            int metadataToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);
        PackageCompileAsset surfaceAsset = coordinate.CompileAsset(assemblyName);
        BrowserWorkspaceParticipant surfaceParticipant =
            scope.SurfaceParticipant(coordinate, surfaceAsset);
        BrowserWorkspaceParticipant participant =
            scope.ImplementationParticipant(surfaceParticipant);
        // One participant, under the same browser bounds as the package load: a body selector is
        // resolved against the implementation surface, and an over-budget implementation must
        // fail visibly rather than resolve against a silently shortened surface.
        AssemblyContextApiSurfaceResult implementationSurfaces =
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    group,
                    ApiSurfaceScope.IncludeAll,
                    BrowserApiSurfacePolicy.Limits,
                    [member]));
        if (implementationSurfaces.Truncation is { } truncation)
        {
            throw new InvalidOperationException(
                $"The implementation surface for '{typeId}' exceeds the browser projection "
                + $"bounds, so the selected body cannot be resolved. "
                + BrowserApiSurfacePolicy.TruncationNotice(truncation));
        }

        AssemblyApiSurface implementation = BrowserSurfaceProjection.Require(
            implementationSurfaces.Assemblies.Assemblies.Single(),
            $"Implementation surface for '{typeId}'");
        Analysis.CallGraphMemberResolution resolution =
            Analysis.CallGraphMemberResolver.ResolveDefinitionIdentity(
                implementation.Surface,
                typeId,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken)
            ?? throw new InvalidOperationException(
                $"The implementation of '{typeId}.{memberName}' does not contain the selected "
                + "API body.");
        return (participant, resolution);
    }

    // Presentation over the neutral projection. docs/design/call-graph-projection.md makes
    // rendering host-owned on purpose: the projection carries identity, direction, cycles, and
    // boundaries, and every front end spells them for itself.
    internal static string Mermaid(CallGraphProjection projection)
    {
        var builder = new StringBuilder("graph LR\n");
        foreach (CallGraphNode node in projection.Nodes)
        {
            builder.Append("  n").Append(node.Id).Append("[\"")
                .Append(MermaidLabel(node.Label))
                .Append("\"]:::")
                .Append(node.Kind.ToString().ToLowerInvariant())
                .Append('\n');
        }

        foreach (CallGraphEdge edge in projection.Edges)
        {
            builder.Append("  n").Append(edge.From)
                .Append(edge.AnyCallInLoop ? " -- loop --> " : " --> ")
                .Append('n').Append(edge.To).Append('\n');
        }

        return builder.ToString();
    }

    internal static string MermaidLabel(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsHighSurrogate(character)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                char lowSurrogate = value[++index];
                var scalar = new Rune(character, lowSurrogate);
                if (Rune.GetUnicodeCategory(scalar) == UnicodeCategory.Format)
                {
                    AppendUnicodeEscape(builder, character);
                    AppendUnicodeEscape(builder, lowSurrogate);
                }
                else
                {
                    builder.Append(character).Append(lowSurrogate);
                }
                continue;
            }

            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\\':
                    builder.Append("&#92;");
                    break;
                case '\u2028':
                case '\u2029':
                    AppendUnicodeEscape(builder, character);
                    break;
                default:
                    if (char.IsControl(character)
                        || char.IsSurrogate(character)
                        || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
                    {
                        AppendUnicodeEscape(builder, character);
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    static void AppendUnicodeEscape(StringBuilder builder, char character) =>
        builder.Append("&#92;u")
            .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));

    static BrowserCallGraphTarget Target(
        CallGraphNode node,
        IReadOnlyList<AssemblyReferenceIdentity> loadedIdentities,
        Func<string, string?>? platformPackForAssembly)
    {
        Analysis.TypeRef? definition = DeclaringTypeDefinition(node.Member.DeclaringType);
        // The metadata origin may be a facade; the resolved definition identifies the browsable
        // assembly and must win when the catalog established it.
        AssemblyReferenceIdentity? identity =
            node.DefinitionAssemblyIdentity
            ?? (definition?.Resolution?.Origin
                    as Analysis.TypeReferenceOrigin.AssemblyReference)
                ?.Assembly;
        if (identity is null && definition is not null)
        {
            AssemblyReferenceIdentity[] matches =
            [
                .. loadedIdentities.Where(candidate =>
                    candidate.Name.Equals(definition.Assembly, StringComparison.OrdinalIgnoreCase)),
            ];
            if (matches.Length > 0
                && matches.All(candidate => candidate.IsEquivalentTo(matches[0])))
            {
                identity = matches[0];
            }
        }
        string assembly =
            identity?.Name
            ?? definition?.Assembly
            ?? node.Member.DeclaringType.Assembly
            ?? "";
        return new BrowserCallGraphTarget(
            $"n{node.Id}",
            assembly,
            identity?.Version?.ToString(),
            identity?.Culture,
            identity?.PublicKeyToken,
            node.Member.DeclaringType.ToQualifiedDisplayString(),
            definition is null ? null : LegacyMetadataTypeId(definition),
            DefinitionTypeId(definition),
            node.Member.Name,
            [.. node.Member.OpenSignatureParameters.Select(type => type.ToQualifiedDisplayString())],
            node.Member.OpenSignatureReturn.ToQualifiedDisplayString(),
            node.Member.GenericArity,
            null,
            Analysis.CallGraphMemberResolver.CreateSelector(node.Member).Key,
            node.Kind.ToString().ToLowerInvariant(),
            platformPackForAssembly?.Invoke(assembly));
    }

    /// <summary>
    /// The exact escaped structured identity of a call-graph target's declaring type — the same
    /// identity the browsable type surface carries and the same one the product's resolver
    /// matches. The product owns both projections; the host only carries them.
    /// </summary>
    static string? DefinitionTypeId(Analysis.TypeRef? type) =>
        type is null ? null : Analysis.CallGraphMemberResolver.DefinitionIdentity(type);

    /// <summary>
    /// The legacy flattened metadata identity, published only where the product reports that it
    /// names exactly one type. A nested <c>Outer+Inner</c> and a type whose own metadata name
    /// contains a literal <c>+</c> share that spelling, so a consumer matching on it would
    /// navigate to the wrong type.
    /// </summary>
    static string? LegacyMetadataTypeId(Analysis.TypeRef type) =>
        Analysis.CallGraphMemberResolver.UnambiguousMetadataIdentity(type);

    static Analysis.TypeRef? DeclaringTypeDefinition(Analysis.TypeRef type)
    {
        while (type.Kind == Analysis.TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }
        return type.Kind == Analysis.TypeRefKind.Definition ? type : null;
    }

    internal static BrowserCallGraphTarget[] Targets(
        IEnumerable<CallGraphNode> nodes,
        IEnumerable<AssemblyReferenceIdentity>? loadedIdentities = null,
        Func<string, string?>? platformPackForAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        AssemblyReferenceIdentity[] identities = [.. loadedIdentities ?? []];
        return
        [
            .. nodes.Select(node =>
                Target(
                    node,
                    identities,
                    platformPackForAssembly)),
        ];
    }

    internal static BrowserCallGraphDiagnostics Diagnostics(
        Analysis.CatalogCallGraphDiagnostics diagnostics,
        bool hasUnexploredTraversalBoundary = false,
        bool hasAnalysisFailureBoundary = false) =>
        new(
            diagnostics.IncompleteNodeCount,
            diagnostics.IncompleteEdgeCount,
            diagnostics.BindingIdentityConflictCount,
            hasUnexploredTraversalBoundary,
            hasAnalysisFailureBoundary);

    static BrowserCallGraphNode Tree(Analysis.CallTreeNode? node) => node is null
        ? new BrowserCallGraphNode("", "None", false, null, [], "", "", "")
        : new BrowserCallGraphNode(
            $"{node.Member.DeclaringType.ToQualifiedDisplayString()}.{node.Member.Name}",
            node.Status.ToString(),
            node.Perf?.InLoop ?? false,
            node.Kind?.ToString(),
            [.. node.Children.Select(Tree)],
            node.Member.DeclaringType.Assembly ?? "",
            node.Member.DeclaringType.ToQualifiedDisplayString(),
            node.Member.Name);
}
