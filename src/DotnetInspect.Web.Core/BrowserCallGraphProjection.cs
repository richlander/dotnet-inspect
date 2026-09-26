using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Web;

/// <summary>
/// One call-graph target's identity, in a DTO-neutral shape.
/// </summary>
internal sealed record BrowserCallGraphTargetInfo(
    string Id,
    string Assembly,
    string? AssemblyVersion,
    string? AssemblyCulture,
    string? AssemblyPublicKeyToken,
    string TypeFullName,
    string? TypeMetadataId,
    string? TypeDefinitionId,
    string MemberName,
    string[] ParameterTypes,
    string ReturnType,
    int GenericArity,
    int? MetadataToken,
    string SelectorKey,
    string Kind,
    string? PlatformPack,
    string? SurfaceAssemblyId,
    string? PackageId,
    string? PackageVersion,
    string? PackageFramework);

internal sealed record BrowserCallGraphNodeInfo(
    string Label,
    string Status,
    bool InLoop,
    string? Source,
    BrowserCallGraphNodeInfo[] Children,
    string Assembly,
    string TypeFullName,
    string MemberName);

internal sealed record BrowserCallGraphScopeInfo(
    int Packages,
    int Assemblies,
    int CallerAssemblies,
    string CalleeScope);

internal sealed record BrowserCallGraphBoundaryInfo(
    string Id,
    string SourcePackageId,
    string SourcePackageVersion,
    string SourcePackageFramework,
    string SourceAssembly,
    string TargetPackageId,
    string TargetPackageVersion,
    string TargetPackageFramework,
    string TargetAssembly);

internal sealed record BrowserCallGraphDiagnosticsInfo(
    int IncompleteNodes,
    int IncompleteEdges,
    int BindingIdentityConflicts,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary,
    bool HasIncompleteCorrespondence,
    int UnclassifiedBoundaryEdges,
    int UnclassifiedBoundaryNamedEdges,
    string[] UnclassifiedBoundaryAssemblies,
    int PhysicalOccurrenceUnavailableEdges);

internal sealed record BrowserCallGraphInfo(
    string Mermaid,
    BrowserCallGraphNodeInfo Callers,
    BrowserCallGraphNodeInfo Callees,
    BrowserCallGraphScopeInfo Scope,
    BrowserCallGraphTargetInfo[] Targets,
    BrowserCallGraphBoundaryInfo[] Boundaries,
    BrowserCallGraphDiagnosticsInfo Diagnostics,
    bool NoBody);

/// <summary>
/// Presentation over the product's neutral call-graph projection.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/design/call-graph-projection.md</c> makes rendering host-owned on purpose: the
/// projection carries identity, direction, cycles, and boundaries, and every front end spells
/// them for itself. The call-graph facade publishes whole graphs, the catalog facade returns one
/// from a demo run, and the source facade carries individual invocation destinations, so the
/// spelling lives here once and each facade maps it to its own transport records.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserCallGraphProjection
{
    internal static BrowserCallGraphInfo Project(
        BrowserInspectionScope scope,
        MemberCallGraphView view)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(view);
        CallGraphProjection projection = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);
        int callerAssemblies = scope.ImplementationParticipants
            .Select(candidate => candidate.Assembly.Identity.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new BrowserCallGraphInfo(
            Mermaid(projection),
            Tree(view.CallerRoot),
            Tree(view.CalleeRoot),
            new BrowserCallGraphScopeInfo(
                scope.Coordinates.Length,
                scope.ImplementationParticipants.Length,
                callerAssemblies,
                view.Tier.ToString()),
            Targets(
                projection.Nodes,
                scope.ImplementationParticipants.Select(
                    participant => participant.Assembly.Identity),
                surfaceParticipants: scope.SurfaceParticipants),
            [],
            Diagnostics(
                view.Diagnostics,
                projection.HasUnexploredTraversalBoundary,
                projection.HasAnalysisFailureBoundary),
            NoBody: view.CalleeRoot is null && view.CallerRoot is null);
    }

    internal static BrowserCallGraphInfo Project(
        PackageDependencyMemberCallGraphDocument document,
        string? rootPackageId = null,
        string? rootPackageVersion = null,
        string? rootPackageFramework = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        InspectionGraphDocument graph = document.Graph;
        if (graph.Nodes.Length == 0)
        {
            return new BrowserCallGraphInfo(
                "graph LR\n",
                Tree(node: null),
                Tree(node: null),
                new BrowserCallGraphScopeInfo(
                    Packages: 1,
                    Assemblies: 0,
                    CallerAssemblies: 0,
                    CalleeScope: ScopeLabel(document.Baseline.Kind)),
                [],
                [],
                Diagnostics(graph),
                NoBody: true);
        }

        int focusNodeId =
            graph.Seeds.FirstOrDefault(seed =>
                seed.Role == InspectionGraphSeedRole.Primary
                && seed.Target.Kind
                    == InspectionGraphTargetKind.Node)?.Target.Id
            ?? 0;
        InspectionGraphNode focus = graph.Nodes[focusNodeId];
        string focusAssembly = NodeAssembly(focus);
        int packageCount = 1
            + document.Routes
                .Select(route => route.Destination)
                .OfType<
                    PackageDependencyMemberCallGraphInspectionDestination
                        .Package>()
                .Select(package =>
                    (
                        package.Descriptor.PackageId.ToLowerInvariant(),
                        package.Descriptor.PackageVersion,
                        package.Descriptor.TargetFramework))
                .Distinct()
                .Count();
        string[] assemblies =
        [
            .. graph.Nodes.Select(NodeAssembly)
                .Where(static assembly =>
                    !string.IsNullOrWhiteSpace(assembly))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        Dictionary<int, PackageDependencyMemberCallGraphPackageSubject>
            packageSubjects = document.PackageSubjects.ToDictionary(
                static subject => subject.NodeId);
        if (!string.IsNullOrWhiteSpace(rootPackageId)
            && !string.IsNullOrWhiteSpace(rootPackageVersion)
            && !string.IsNullOrWhiteSpace(rootPackageFramework))
        {
            packageSubjects.TryAdd(
                focusNodeId,
                new(
                    focusNodeId,
                    rootPackageId,
                    rootPackageVersion,
                    rootPackageFramework));
        }
        IReadOnlyDictionary<int, string> targetKinds =
            ExternalFocusTargetKinds(graph, focusNodeId);
        BrowserCallGraphTargetInfo[] targets =
        [
            .. graph.Nodes.Select(node =>
                Target(
                    node,
                    packageSubjects.GetValueOrDefault(node.Id),
                    targetKinds[node.Id])),
        ];
        return new BrowserCallGraphInfo(
            Mermaid(graph),
            EmptyTree(),
            Tree(graph, focusNodeId),
            new BrowserCallGraphScopeInfo(
                packageCount,
                assemblies.Length,
                string.IsNullOrWhiteSpace(focusAssembly) ? 0 : 1,
                ScopeLabel(document.Baseline.Kind)),
            targets,
            Boundaries(graph, targets),
            Diagnostics(graph),
            NoBody: false);
    }

    static BrowserCallGraphBoundaryInfo[] Boundaries(
        InspectionGraphDocument graph,
        IReadOnlyList<BrowserCallGraphTargetInfo> targets)
    {
        IReadOnlyDictionary<string, BrowserCallGraphTargetInfo> targetsById =
            targets.ToDictionary(target => target.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var boundaries = new List<BrowserCallGraphBoundaryInfo>();
        foreach (InspectionGraphEdge edge in graph.Edges.OrderBy(edge => edge.Id))
        {
            BrowserCallGraphTargetInfo source =
                targetsById[$"n{edge.FromNodeId}"];
            BrowserCallGraphTargetInfo target =
                targetsById[$"n{edge.ToNodeId}"];
            if (target.Kind != "boundary"
                || string.IsNullOrWhiteSpace(source.PackageId)
                || string.IsNullOrWhiteSpace(source.PackageVersion)
                || string.IsNullOrWhiteSpace(source.PackageFramework)
                || string.IsNullOrWhiteSpace(source.Assembly)
                || string.IsNullOrWhiteSpace(target.PackageId)
                || string.IsNullOrWhiteSpace(target.PackageVersion)
                || string.IsNullOrWhiteSpace(target.PackageFramework)
                || string.IsNullOrWhiteSpace(target.Assembly))
            {
                continue;
            }

            string key = string.Join(
                '\u001F',
                source.PackageId,
                source.PackageVersion,
                source.PackageFramework,
                source.Assembly,
                target.PackageId,
                target.PackageVersion,
                target.PackageFramework,
                target.Assembly);
            if (!seen.Add(key))
                continue;

            boundaries.Add(
                new BrowserCallGraphBoundaryInfo(
                    $"e{edge.Id}",
                    source.PackageId,
                    source.PackageVersion,
                    source.PackageFramework,
                    source.Assembly,
                    target.PackageId,
                    target.PackageVersion,
                    target.PackageFramework,
                    target.Assembly));
        }
        return [.. boundaries];
    }

    static string ScopeLabel(
        PackageSupplyChainBaseline baseline) =>
        baseline switch
        {
            PackageSupplyChainBaseline.Nothing =>
                "External packages",
            PackageSupplyChainBaseline.Self =>
                "Outside first-party scope",
            PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems =>
                "Supply Chain",
            _ => throw new ArgumentOutOfRangeException(nameof(baseline)),
        };

    internal static string Mermaid(CallGraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
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
        ArgumentNullException.ThrowIfNull(value);
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

    static string Mermaid(InspectionGraphDocument graph)
    {
        var builder = new StringBuilder("graph LR\n");
        foreach (InspectionGraphNode node in graph.Nodes)
        {
            builder.Append("  n").Append(node.Id).Append("[\"")
                .Append(MermaidLabel(NodeLabel(node)))
                .Append("\"]:::")
                .Append(node.Role.ToString().ToLowerInvariant())
                .Append('\n');
        }
        foreach (InspectionGraphEdge edge in graph.Edges)
        {
            builder.Append("  n").Append(edge.FromNodeId)
                .Append(" --> n").Append(edge.ToNodeId)
                .Append('\n');
        }
        return builder.ToString();
    }

    static BrowserCallGraphNodeInfo EmptyTree() =>
        new("", "None", false, null, [], "", "", "");

    static BrowserCallGraphNodeInfo Tree(
        InspectionGraphDocument graph,
        int nodeId) =>
        Tree(
            graph,
            nodeId,
            incomingSource: null,
            new HashSet<int>());

    static BrowserCallGraphNodeInfo Tree(
        InspectionGraphDocument graph,
        int nodeId,
        string? incomingSource,
        HashSet<int> path)
    {
        InspectionGraphNode node = graph.Nodes[nodeId];
        Analysis.MemberRef member = NodeMember(node);
        bool inLoop = !path.Add(nodeId);
        BrowserCallGraphNodeInfo[] children = inLoop
            ? []
            :
            [
                .. graph.Edges
                    .Where(edge => edge.FromNodeId == nodeId)
                    .OrderBy(edge => edge.Id)
                    .Select(edge => Tree(
                        graph,
                        edge.ToNodeId,
                        edge.Relationship.Id,
                        new HashSet<int>(path))),
            ];
        return new BrowserCallGraphNodeInfo(
            NodeLabel(node),
            node.Role.ToString(),
            inLoop,
            incomingSource,
            children,
            NodeAssembly(node),
            member.DeclaringType.ToQualifiedDisplayString(),
            member.Name);
    }

    static BrowserCallGraphTargetInfo Target(
        InspectionGraphNode node,
        PackageDependencyMemberCallGraphPackageSubject? packageSubject,
        string kind)
    {
        Analysis.MemberRef member = NodeMember(node);
        Analysis.TypeRef? definition =
            DeclaringTypeDefinition(member.DeclaringType);
        AssemblyReferenceIdentity? identity =
            definition?.Resolution?.Origin switch
            {
                Analysis.TypeReferenceOrigin.AssemblyReference
                    reference => reference.Assembly,
                Analysis.TypeReferenceOrigin.CurrentAssembly
                    current => current.Assembly,
                _ => null,
            };
        string assembly =
            identity?.Name
            ?? member.DeclaringType.Assembly
            ?? "";
        return new BrowserCallGraphTargetInfo(
            $"n{node.Id}",
            assembly,
            identity?.Version?.ToString(),
            identity?.Culture,
            identity?.PublicKeyToken,
            member.DeclaringType.ToQualifiedDisplayString(),
            definition is null ? null : LegacyMetadataTypeId(definition),
            DefinitionTypeId(definition),
            member.Name,
            [.. member.ParameterTypes.Select(
                type => type.ToQualifiedDisplayString())],
            member.ReturnType.ToQualifiedDisplayString(),
            member.GenericArity,
            MetadataToken: null,
            Analysis.CallGraphMemberResolver.CreateSelector(member).Key,
            kind,
            PlatformPack: null,
            SurfaceAssemblyId: null,
            packageSubject?.PackageId,
            packageSubject?.PackageVersion,
            packageSubject?.TargetFramework);
    }

    static IReadOnlyDictionary<int, string> ExternalFocusTargetKinds(
        InspectionGraphDocument graph,
        int focusNodeId)
    {
        ILookup<int, InspectionGraphCharacteristic> characteristics =
            graph.Characteristics
                .Where(static characteristic =>
                    characteristic.Target.Kind
                        == InspectionGraphTargetKind.Edge)
                .ToLookup(static characteristic =>
                    characteristic.Target.Id);
        var kinds = new Dictionary<int, string>
        {
            [focusNodeId] = "focus",
        };
        foreach (InspectionGraphEdge edge in graph.Edges)
        {
            if (edge.ToNodeId == focusNodeId)
                continue;
            InspectionGraphCharacteristic[] roles =
            [
                .. characteristics[edge.Id].Where(
                    static characteristic =>
                        characteristic.Descriptor.Id
                            == ExternalFocusedCallGraphInspectionCatalog
                                .EdgeRole.Id),
            ];
            if (roles.Length != 1
                || roles[0].Value
                    is not InspectionGraphValue.Token
                        {
                            Value:
                                "connector"
                                or "boundary"
                                or "unclassified-boundary",
                        } token)
            {
                throw new InvalidOperationException(
                    $"External-focused edge {edge.Id} must carry exactly one supported role.");
            }

            if (kinds.TryGetValue(edge.ToNodeId, out string? existing)
                && !string.Equals(
                    existing,
                    token.Value,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"External-focused node {edge.ToNodeId} has conflicting incoming roles.");
            }
            kinds[edge.ToNodeId] = token.Value;
        }
        foreach (InspectionGraphNode node in graph.Nodes)
        {
            // Outbound evidence can retain a disconnected hub source whose
            // only projected edge is an unclassified boundary.
            kinds.TryAdd(node.Id, "connector");
        }
        return kinds;
    }

    static Analysis.MemberRef NodeMember(
        InspectionGraphNode node) =>
        node.Subject
            is InspectionGraphSubject.MemberSubject
            {
                Identity:
                    InspectionGraphMemberIdentity.CallGraph callGraph,
            }
                ? callGraph.Member
                : throw new InvalidOperationException(
                    "The dependency member call graph contained a non-member node.");

    static string NodeLabel(InspectionGraphNode node)
    {
        Analysis.MemberRef member = NodeMember(node);
        return $"{member.DeclaringType.ToQualifiedDisplayString()}.{member.Name}";
    }

    static string NodeAssembly(InspectionGraphNode node) =>
        NodeMember(node).DeclaringType.Assembly ?? "";

    static BrowserCallGraphDiagnosticsInfo Diagnostics(
        InspectionGraphDocument graph)
    {
        CallGraphCorrespondenceIncompleteEvidence[] correspondence =
        [
            .. graph.Limits
                .Where(limit =>
                    limit.Descriptor.Id
                        == CallGraphInspectionGraphCatalog
                            .CorrespondenceIncomplete.Id)
                .Select(static limit => limit.Evidence)
                .OfType<CallGraphCorrespondenceIncompleteEvidence>(),
        ];
        InspectionGraphLimit[] unclassifiedBoundaries =
        [
            .. graph.Limits.Where(limit =>
                limit.Descriptor.Id
                    == ExternalFocusedCallGraphInspectionCatalog
                        .BoundaryClassificationIncomplete.Id),
        ];
        string[] unclassifiedBoundaryAssemblyTargets =
        [
            .. unclassifiedBoundaries
                .Select(static limit => limit.Target)
                .Where(static target => target is not null)
                .Select(static target => target!.Value)
                .Where(static target =>
                    target.Kind == InspectionGraphTargetKind.Edge)
                .Select(target =>
                    NodeAssembly(
                        graph.Nodes[
                            graph.Edges[target.Id].ToNodeId]))
                .Where(static assembly =>
                    !string.IsNullOrWhiteSpace(assembly)),
        ];
        string[] unclassifiedAssemblies =
        [
            .. unclassifiedBoundaryAssemblyTargets
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        return new BrowserCallGraphDiagnosticsInfo(
            correspondence.Sum(static evidence =>
                evidence.IncompleteNodeCount),
            correspondence.Sum(static evidence =>
                evidence.IncompleteEdgeCount),
            correspondence.Sum(static evidence =>
                evidence.BindingIdentityConflictCount),
            HasUnexploredTraversalBoundary:
                graph.Limits.Any(limit =>
                    limit.Descriptor.Id
                        == CallGraphInspectionGraphCatalog
                            .TraversalIncomplete.Id),
            HasAnalysisFailureBoundary:
                graph.Failures.Any(failure =>
                    failure.Descriptor.Id
                        == CallGraphInspectionGraphCatalog
                            .AnalysisIncomplete.Id),
            HasIncompleteCorrespondence:
                correspondence.Length > 0,
            UnclassifiedBoundaryEdges:
                unclassifiedBoundaries.Length,
            UnclassifiedBoundaryNamedEdges:
                unclassifiedBoundaryAssemblyTargets.Length,
            UnclassifiedBoundaryAssemblies:
                unclassifiedAssemblies,
            PhysicalOccurrenceUnavailableEdges:
                graph.Limits.Count(limit =>
                    limit.Descriptor.Id
                        == CallGraphInspectionGraphCatalog
                            .PhysicalOccurrencesUnavailable.Id));
    }

    internal static BrowserCallGraphTargetInfo Target(
        CallGraphNode node,
        IReadOnlyList<AssemblyReferenceIdentity> loadedIdentities,
        Func<string, string?>? platformPackForAssembly,
        IReadOnlyList<BrowserWorkspaceParticipant>? surfaceParticipants = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(loadedIdentities);
        Analysis.TypeRef? definition = DeclaringTypeDefinition(node.Member.DeclaringType);
        // The metadata origin may be a facade; the resolved definition identifies the browsable
        // assembly and must win when the catalog established it.
        AssemblyReferenceIdentity? identity =
            node.DefinitionAssemblyIdentity
            ?? node.OccurrenceAssemblyIdentity
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
        string? surfaceAssemblyId = null;
        if (identity is not null && surfaceParticipants is not null)
        {
            BrowserWorkspaceParticipant[] matches =
            [
                .. surfaceParticipants.Where(participant =>
                    participant.Assembly.Identity.IsEquivalentTo(identity)),
            ];
            if (matches.Length == 1)
                surfaceAssemblyId = matches[0].Asset.Id;
        }
        return new BrowserCallGraphTargetInfo(
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
            platformPackForAssembly?.Invoke(assembly),
            surfaceAssemblyId,
            PackageId: null,
            PackageVersion: null,
            PackageFramework: null);
    }

    internal static BrowserCallGraphTargetInfo Target(
        Analysis.MethodIdentity member,
        AssemblyReferenceIdentity identity,
        string id,
        IReadOnlyList<BrowserWorkspaceParticipant>? surfaceParticipants = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Analysis.TypeRef? definition = DeclaringTypeDefinition(member.DeclaringType);
        string? surfaceAssemblyId = null;
        if (surfaceParticipants is not null)
        {
            BrowserWorkspaceParticipant[] matches =
            [
                .. surfaceParticipants.Where(participant =>
                    participant.Assembly.Identity.IsEquivalentTo(identity)),
            ];
            if (matches.Length == 1)
                surfaceAssemblyId = matches[0].Asset.Id;
        }
        return new BrowserCallGraphTargetInfo(
            id,
            identity.Name,
            identity.Version?.ToString(),
            identity.Culture,
            identity.PublicKeyToken,
            member.DeclaringType.ToQualifiedDisplayString(),
            definition is null ? null : LegacyMetadataTypeId(definition),
            DefinitionTypeId(definition),
            member.Name,
            [.. member.ParameterTypes.Select(type => type.ToQualifiedDisplayString())],
            member.ReturnType.ToQualifiedDisplayString(),
            member.GenericArity,
            member.MetadataToken,
            Analysis.CallGraphMemberResolver.CreateSelector(member).Key,
            "method",
            PlatformPack: null,
            surfaceAssemblyId,
            PackageId: null,
            PackageVersion: null,
            PackageFramework: null);
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

    internal static BrowserCallGraphTargetInfo[] Targets(
        IEnumerable<CallGraphNode> nodes,
        IEnumerable<AssemblyReferenceIdentity>? loadedIdentities = null,
        Func<string, string?>? platformPackForAssembly = null,
        IReadOnlyList<BrowserWorkspaceParticipant>? surfaceParticipants = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        AssemblyReferenceIdentity[] identities = [.. loadedIdentities ?? []];
        return
        [
            .. nodes.Select(node =>
                Target(
                    node,
                    identities,
                    platformPackForAssembly,
                    surfaceParticipants)),
        ];
    }

    internal static BrowserCallGraphDiagnosticsInfo Diagnostics(
        Analysis.CatalogCallGraphDiagnostics diagnostics,
        bool hasUnexploredTraversalBoundary = false,
        bool hasAnalysisFailureBoundary = false) =>
        new(
            diagnostics.IncompleteNodeCount,
            diagnostics.IncompleteEdgeCount,
            diagnostics.BindingIdentityConflictCount,
            hasUnexploredTraversalBoundary,
            hasAnalysisFailureBoundary,
            HasIncompleteCorrespondence:
                diagnostics.IncompleteNodeCount > 0
                || diagnostics.IncompleteEdgeCount > 0,
            UnclassifiedBoundaryEdges: 0,
            UnclassifiedBoundaryNamedEdges: 0,
            UnclassifiedBoundaryAssemblies: [],
            PhysicalOccurrenceUnavailableEdges: 0);

    internal static BrowserCallGraphNodeInfo Tree(Analysis.CallTreeNode? node) => node is null
        ? new BrowserCallGraphNodeInfo("", "None", false, null, [], "", "", "")
        : new BrowserCallGraphNodeInfo(
            $"{node.Member.DeclaringType.ToQualifiedDisplayString()}.{node.Member.Name}",
            node.Status.ToString(),
            node.Perf?.InLoop ?? false,
            node.Kind?.ToString(),
            [.. node.Children.Select(Tree)],
            node.Member.DeclaringType.Assembly ?? "",
            node.Member.DeclaringType.ToQualifiedDisplayString(),
            node.Member.Name);
}
