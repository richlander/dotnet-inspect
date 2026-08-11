using System.Collections.Immutable;
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
        BrowserPackageCoordinate requested = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            targetFramework);
        BrowserInspectionScope scope = BrowserPackageWorkspace.OpenScope([requested]);
        BrowserPackageCoordinate coordinate = scope.Coordinate(requested);

        // The site's default path shows public types by default and reaches non-public ones
        // through the accessibility filter, so it asks for the composed scope: a public type
        // keeps its public member list even though non-public types are present.
        AssemblyContextApiSurfaceResult surfaces = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.Execute(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes));

        // The query walks the group's participants in order and returns one entry each, so entry
        // i is participant i. Assert that rather than assume it: a correlation that silently
        // slipped would attribute one assembly's types to another.
        if (surfaces.Assemblies.Assemblies.Length != scope.SurfaceParticipants.Length)
        {
            throw new InvalidOperationException(
                "The API surface query returned a different number of entries than the workspace "
                + "has participants, so per-assembly attribution cannot be trusted.");
        }

        var assemblies = new List<BrowserAssemblySurface>();
        var types = new List<BrowserTypeSurface>();
        for (int index = 0; index < surfaces.Assemblies.Assemblies.Length; index++)
        {
            if (surfaces.Assemblies.Assemblies[index]
                is not AssemblyContextEntry<AssemblyApiSurface>.Available available)
            {
                continue;
            }

            BrowserWorkspaceParticipant participant = scope.SurfaceParticipants[index];
            if (!ReferenceEquals(
                    available.Subject.Registration,
                    participant.Assembly.Registration))
            {
                throw new InvalidOperationException(
                    "The API surface query's entry order does not match the workspace's "
                    + "participant order, so per-assembly attribution cannot be trusted.");
            }

            if (!participant.Coordinate.Key.Equals(coordinate.Key, StringComparison.Ordinal))
                continue;

            BrowserTypeSurface[] assemblyTypes =
            [
                .. available.Value.Surface.Types
                    .Select(type => BrowserSurfaceProjection.Type(
                        type,
                        participant.Asset.AssemblyName)),
            ];
            BrowserTypeSurface[] publicTypes =
            [
                .. assemblyTypes.Where(type => IsDefaultBucket(surfaces, type)),
            ];
            assemblies.Add(new BrowserAssemblySurface(
                participant.Asset.Id,
                participant.Asset.AssemblyName,
                participant.Asset.Path,
                publicTypes.Length,
                publicTypes.Sum(type => type.Members)));
            types.AddRange(assemblyTypes);
        }

        if (assemblies.Count == 0)
        {
            throw new InvalidOperationException(
                $"No assembly of {coordinate.PackageId} {coordinate.Version} for "
                + $"{coordinate.Framework} produced an API surface. "
                + (BrowserSurfaceProjection.Failures(surfaces.Assemblies.Assemblies)
                    ?? "The workspace reported no failure."));
        }

        // Two assemblies in one package may ship the same type. Qualify only the collisions, so
        // an unambiguous type keeps the identity deep links and search already use.
        var duplicates = types
            .GroupBy(type => type.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        BrowserTypeSurface[] identified =
        [
            .. types
                .Select(type => duplicates.Contains(type.Id)
                    ? type with { Id = $"{type.Assembly}:{type.Id}" }
                    : type)
                .OrderBy(type => type.Namespace, StringComparer.Ordinal)
                .ThenBy(type => type.Name, StringComparer.Ordinal),
        ];

        BrowserAssemblySurface defaultAssembly = assemblies.FirstOrDefault(
                assembly => assembly.Id.Equals(
                    coordinate.DefaultAsset.Id,
                    StringComparison.Ordinal))
            ?? assemblies[0];

        return JsonSerializer.Serialize(
            new BrowserPackageSurface(
                coordinate.PackageId,
                coordinate.Version,
                [.. coordinate.Selection.AvailableTargetFrameworks],
                coordinate.Framework,
                defaultAssembly.Id,
                [.. assemblies],
                identified,
                [.. surfaces.Accessibility.Select(BrowserSurfaceProjection.Descriptor)],
                identified
                    .Where(type => IsDefaultBucket(surfaces, type))
                    .Sum(type => type.Members),
                [.. coordinate.Package.Documents()],
                BrowserSurfaceProjection.Failures(surfaces.Assemblies.Assemblies)),
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
        string typeId,
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
            int implementationToken
        ) = await ImplementationMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeId,
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
                        typeId,
                        memberName,
                        MethodToken: implementationToken,
                        SourceDocument: true,
                        PrinterOptions: BrowserStyleOptions.Resolve(styleOptionsJson)))),
            $"Annotated source for '{typeId}.{memberName}'");

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
        BrowserPackageCoordinate requested = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            targetFramework);
        BrowserInspectionScope scope = BrowserPackageWorkspace.OpenScope([requested]);
        BrowserPackageCoordinate coordinate = scope.Coordinate(requested);

        // The workspace is retained and reused, so this runs the whole-group query rather than
        // the streaming per-participant form: that form's release is terminal for the released
        // participant, which would leave the reused group unable to answer a later query.
        AssemblyContextIntegrationsResult result =
            scope.UseImplementationOrSurface(AssemblyContextIntegrationsQuery.Execute);

        var failures = new List<string>();
        var signals = new List<EcosystemIntegrationSignalInfo>();
        foreach (AssemblyIntegrationsEntry entry in result.Assemblies)
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
                    failures.Add($"{failed.Subject.Identity.Name}: {failed.Error.Message}");
                    break;
            }
        }

        return JsonSerializer.Serialize(
            new BrowserPackageIntegrations(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                [
                    .. signals
                        .GroupBy(signal => signal.Integration, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(group => new BrowserIntegrationCategory(
                            group.Key,
                            [
                                .. group
                                    .Select(signal => new BrowserIntegrationSignal(
                                        signal.Kind,
                                        signal.Name,
                                        signal.Shape))
                                    .DistinctBy(signal => (signal.Kind, signal.Name, signal.Shape))
                                    .OrderBy(signal => signal.Name, StringComparer.OrdinalIgnoreCase),
                            ])),
                ],
                signals.Count,
                result.IsComplete,
                failures.Count == 0 ? null : string.Join("; ", failures)),
            BrowserJsonContext.Default.BrowserPackageIntegrations);
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
        string typeId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string workspaceJson)
    {
        _ = memberSignature;
        if (metadataToken == 0)
        {
            throw new InvalidOperationException(
                "A call graph needs the selected overload's method-body token.");
        }

        BrowserPackageCoordinate root = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            targetFramework);
        var coordinates = new List<BrowserPackageCoordinate> { root };
        foreach (BrowserWorkspacePackage entry in JsonSerializer.Deserialize(
            workspaceJson,
            BrowserJsonContext.Default.BrowserWorkspacePackageArray) ?? [])
        {
            if (entry.Package.Equals(root.PackageId, StringComparison.OrdinalIgnoreCase)
                && entry.Version.Equals(root.Version, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            coordinates.Add(await BrowserPackageWorkspace.ResolveAsync(
                entry.Package,
                entry.Version,
                string.IsNullOrWhiteSpace(entry.Framework) ? null : entry.Framework));
        }

        BrowserInspectionScope scope = BrowserPackageWorkspace.OpenScope(coordinates);
        BrowserPackageCoordinate rootCoordinate = scope.Coordinate(root);
        (
            BrowserWorkspaceParticipant participant,
            int implementationToken
        ) = ResolveImplementationMember(
            scope,
            rootCoordinate,
            assemblyName,
            typeId,
            memberName,
            selectorKey,
            metadataToken);

        MemberCallGraphView view = scope.UseImplementation(group =>
        {
            using var session = new MemberCallGraphSession(
                group,
                participant.Assembly,
                implementationToken);
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
                [
                    .. projection.Nodes
                        .Where(node => node.Kind == CallGraphNodeKind.External)
                        .Select(Target),
                ],
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
        BrowserMemberDocumentation documentation = coordinate.Package.TryRead(
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

    // The library-owned StyleOptionCatalog is the single source of truth for the decompiler style
    // taxonomy. These records carry its data across the Wasm boundary; the host retains no labels,
    // summaries, or ordering of its own. Neither listing inspects an artifact.
    [JSExport]
    public static string ListStyleTiers() => JsonSerializer.Serialize(
        Pipeline.StyleOptionCatalog.Tiers
            .Select(tier => new BrowserStyleTier(
                tier.Id.ToString(),
                tier.Title,
                tier.Summary,
                tier.Order,
                tier.ByteDivergent))
            .ToArray(),
        BrowserJsonContext.Default.BrowserStyleTierArray);

    [JSExport]
    public static string ListStyleOptions()
    {
        var options = new List<BrowserStyleOption>();
        foreach (Pipeline.StyleOptionDescriptor descriptor in Pipeline.StyleOptionCatalog.Options)
        {
            // A knob is an axis of values; the default value is not selectable taste. Boolean
            // knobs expose one non-default value and keep the descriptor id so stored selections
            // stay stable; multi-value axes expose one option per value and share a conflict group
            // so the client single-selects within the axis.
            Pipeline.StyleOptionValue[] choices =
            [
                .. descriptor.Values.Where(value =>
                    !string.Equals(value.Token, descriptor.DefaultValue, StringComparison.Ordinal)),
            ];
            bool multiValue = choices.Length > 1;
            foreach (Pipeline.StyleOptionValue value in choices)
            {
                options.Add(new BrowserStyleOption(
                    multiValue ? $"{descriptor.Id}:{value.Token}" : descriptor.Id,
                    multiValue ? $"{descriptor.Title} · {value.Title ?? value.Token}" : descriptor.Title,
                    descriptor.Summary,
                    descriptor.Tier.ToString(),
                    descriptor.ByteDivergent,
                    value.OracleEndorsed,
                    multiValue ? descriptor.Id : null));
            }
        }

        return JsonSerializer.Serialize(
            options.ToArray(),
            BrowserJsonContext.Default.BrowserStyleOptionArray);
    }

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
        BrowserPackageCoordinate requested = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            targetFramework);
        BrowserInspectionScope scope = BrowserPackageWorkspace.OpenScope([requested]);
        BrowserPackageCoordinate coordinate = scope.Coordinate(requested);

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
        int MethodToken)> ImplementationMemberAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeId,
            string memberName,
            string selectorKey,
            int metadataToken)
    {
        BrowserPackageCoordinate requested = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            targetFramework);
        BrowserInspectionScope scope = BrowserPackageWorkspace.OpenScope([requested]);
        BrowserPackageCoordinate coordinate = scope.Coordinate(requested);
        (
            BrowserWorkspaceParticipant participant,
            int methodToken
        ) = ResolveImplementationMember(
            scope,
            coordinate,
            assemblyName,
            typeId,
            memberName,
            selectorKey,
            metadataToken);
        return (scope, participant, methodToken);
    }

    static (BrowserWorkspaceParticipant Participant, int MethodToken)
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
        BrowserWorkspaceParticipant participant = scope.ImplementationParticipant(
            coordinate,
            coordinate.ImplementationAsset(assemblyName));
        AssemblyApiSurface implementation = BrowserSurfaceProjection.Require(
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    member,
                    ApiSurfaceScope.IncludeAll)),
            $"Implementation surface for '{typeId}'");
        Analysis.CallGraphMemberResolution resolution =
            Analysis.CallGraphMemberResolver.Resolve(
                implementation.Surface,
                typeId,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken)
            ?? throw new InvalidOperationException(
                $"The implementation of '{typeId}.{memberName}' does not contain the selected "
                + "API body.");
        return (participant, resolution.BodyToken);
    }

    /// <summary>
    /// Whether a type falls in the accessibility bucket the product marked as the default one.
    /// The host does not decide which bucket that is.
    /// </summary>
    static bool IsDefaultBucket(
        AssemblyContextApiSurfaceResult surfaces,
        BrowserTypeSurface type)
        => surfaces.Accessibility.Any(
            bucket => bucket.IsDefault
                && bucket.Id.Equals(type.AccessibilityId, StringComparison.Ordinal));

    // Presentation over the neutral projection. docs/design/call-graph-projection.md makes
    // rendering host-owned on purpose: the projection carries identity, direction, cycles, and
    // boundaries, and every front end spells them for itself.
    static string Mermaid(CallGraphProjection projection)
    {
        var builder = new StringBuilder("graph LR\n");
        foreach (CallGraphNode node in projection.Nodes)
        {
            builder.Append("  n").Append(node.Id).Append("[\"")
                .Append(node.Label.Replace("\"", "&quot;", StringComparison.Ordinal))
                .Append("\"]:::")
                .Append(node.Kind.ToString().ToLowerInvariant())
                .Append('\n');
        }

        foreach (CallGraphEdge edge in projection.Edges)
        {
            builder.Append("  n").Append(edge.From)
                .Append(edge.LoopLabel is null ? " --> " : " -- loop --> ")
                .Append('n').Append(edge.To).Append('\n');
        }

        return builder.ToString();
    }

    static BrowserCallGraphTarget Target(CallGraphNode node) => new(
        $"n{node.Id}",
        node.Member.DeclaringType.Assembly ?? "",
        node.Member.DeclaringType.ToQualifiedDisplayString(),
        node.Member.Name,
        [.. node.Member.OpenSignatureParameters.Select(type => type.ToQualifiedDisplayString())],
        node.Member.OpenSignatureReturn.ToQualifiedDisplayString(),
        node.Member.GenericArity,
        null,
        Analysis.CallGraphMemberResolver.CreateSelector(node.Member).Key,
        node.Kind.ToString().ToLowerInvariant());

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
