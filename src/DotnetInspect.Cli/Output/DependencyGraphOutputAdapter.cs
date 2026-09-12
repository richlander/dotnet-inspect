using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.CSharp;
using ILInspector.Metadata;
using InertText;
using Markout;
using Markout.Formatting;
using NuGetFetch;

namespace DotnetInspect.Cli.Output;

internal sealed record DependencyGraphEdgeRow(
    int EdgeId,
    int SourceNodeId,
    int TargetNodeId,
    int[] RootOccurrences,
    string SourceKind,
    string SourceIdentity,
    string Source,
    string Relationship,
    string TargetKind,
    string TargetIdentity,
    string Target,
    int MinimumDepth,
    string Resolution,
    string? EvidenceKind,
    string? EvidenceIdentity)
{
    internal InertString SourceIdentityText { get; init; }

    internal InertString SourceText { get; init; }

    internal InertString TargetIdentityText { get; init; }

    internal InertString TargetText { get; init; }

    internal InertString? EvidenceIdentityText { get; init; }
}

internal sealed record DependencyGraphJsonLibraryIdentity(
    string Kind,
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken,
    string? ModuleVersionId);

internal sealed record DependencyGraphJsonPackageIdentity(
    string Id,
    string Version);

internal sealed record DependencyGraphJsonBoundaryIdentity(
    int SourceProjection,
    DependencyEvidenceDeclarationIdentityJson Declaration,
    string PackageId,
    string VersionConstraint);

internal sealed record DependencyGraphJsonNodeIdentity(
    string Kind,
    string? Type,
    DependencyGraphJsonLibraryIdentity? Library,
    DependencyGraphJsonPackageIdentity? Package,
    DependencyEvidenceRestoredSelectionIdentityJson? RestoredRoot,
    DependencyEvidenceProjectNodeIdentityJson? RestoredProject,
    DependencyEvidencePackageNodeIdentityJson? RestoredPackage,
    DependencyGraphJsonBoundaryIdentity? Boundary);

internal sealed record DependencyGraphJsonNode(
    int Id,
    DependencyGraphJsonNodeIdentity Identity,
    string Label,
    int[] RootOccurrences);

internal sealed record DependencyGraphJsonEvidenceIdentity(
    string Kind,
    DependencyGraphJsonLibraryIdentity? AssemblyReference,
    string? PackageVersionConstraint,
    int? SourceProjection,
    DependencyEvidenceDeclarationIdentityJson? PackageDeclaration,
    DependencyEvidenceGraphParentIdentityJson? RestoredParent,
    DependencyEvidenceProjectNodeIdentityJson? RestoredProject,
    DependencyEvidenceEdgeIdentityJson? RestoredPackage);

internal sealed record DependencyGraphJsonEdge(
    int Id,
    int[] RootOccurrences,
    int SourceNodeId,
    int TargetNodeId,
    DependencyGraphJsonNodeIdentity SourceIdentity,
    string Relationship,
    DependencyGraphJsonNodeIdentity TargetIdentity,
    int MinimumDepth,
    string Resolution,
    DependencyGraphJsonEvidenceIdentity? EvidenceIdentity,
    int? SourcePackageProjection,
    int? TargetPackageProjection,
    PackageDependencyTraversalEdgeEmissionAuthority? PackageEmissionAuthority,
    DependsPackageAuthorityFailureJson[]? PackageDiagnostics);

internal sealed record DependencyGraphJsonDepthBoundary(
    int NodeId,
    DependencyGraphJsonNodeIdentity NodeIdentity,
    int? PackageProjection,
    int MaximumDepth,
    int[] RootOccurrences,
    DependencyGraphDepthBoundaryProducerKind Producer);

internal sealed record DependencyGraphJsonPackageProjection(
    int Id,
    int NodeId,
    PackageDependencyTraversalProjectionKind Kind,
    PackageDependencyTraversalProjectionExpansion Expansion,
    int? RootOccurrence,
    DependencyGraphJsonPackageEvidence? Evidence,
    DependencyGraphJsonPackageCandidate? Candidate,
    DependsPackageAuthorityFailureJson[] Diagnostics);

internal sealed record DependencyGraphJsonPackageEvidence(
    DependencyEvidenceRootIdentityJson Identity,
    PackageDependencyEvidenceAcquisitionForm SourceKind,
    string? SourceLabel,
    PackageManifestIdentityProvenance IdentityProvenance,
    DependencyEvidenceSourceIdentityJson? Source,
    PackageDependencyEvidenceSelectionStatus? Selection,
    DependencyEvidenceGroupIdentityJson? SelectedGroup,
    DependencyEvidenceGroupOccurrenceJson? SelectedSourceOccurrence,
    string? RequestedFramework,
    string? SelectedFramework,
    DependencyEvidenceDeclarationState? Declaration,
    PackageDependencyEvidencePhaseCompletion? DeclarationCompletion);

internal sealed record DependencyGraphJsonPackageCandidate(
    int Correspondence,
    DependencyGraphJsonPackageIdentity Coordinate,
    PackageAcquisitionCandidateKind Kind,
    DependencyGraphJsonVersionDiscoveryContract? DiscoveryContract,
    DependencyGraphJsonPackageAuthority[] Authorities);

internal sealed record DependencyGraphJsonVersionDiscoveryContract(
    int ContractVersion,
    bool IncludePrerelease,
    bool IncludeUnlisted,
    int? Limit);

internal sealed record DependencyGraphJsonPackageAuthority(
    int Association,
    ConfiguredPackageAuthorityKind Kind,
    string? PersistentKey,
    DependencyEvidenceSourceIdentityJson? ObservationSource,
    PackageDiscoveryContract? DiscoveryContract,
    PackageListingState? ListingState);

internal sealed record DependencyGraphJsonDocument(
    DependencyGraphJsonNode[] Nodes,
    DependencyGraphJsonEdge[] Edges,
    DependencyGraphJsonDepthBoundary[] DepthBoundaries,
    DependencyGraphJsonPackageProjection[] PackageProjections);

internal sealed record DependencyGraphJsonLine(
    int[] RootOccurrences,
    string SourceKind,
    string SourceIdentity,
    string Source,
    string Relationship,
    string TargetKind,
    string TargetIdentity,
    string Target,
    int MinimumDepth,
    string Resolution,
    string? EvidenceKind,
    string? EvidenceIdentity);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DependencyGraphJsonDocument))]
[JsonSerializable(typeof(DependencyGraphJsonLine))]
internal partial class DependencyGraphJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DependencyGraphJsonDocument))]
[JsonSerializable(typeof(DependencyGraphJsonLine))]
internal partial class DependencyGraphCompactJsonContext :
    JsonSerializerContext;

internal static class DependencyGraphOutputAdapter
{
    internal static List<DependencyGraphEdgeRow> EdgeRows(
        DependencyGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return
        [
            .. document.Edges.Select(edge =>
            {
                DependencyGraphNode source =
                    document.Nodes[edge.SourceNodeId];
                DependencyGraphNode target =
                    document.Nodes[edge.TargetNodeId];
                InertString sourceIdentity = IdentityText(source.Identity);
                InertString targetIdentity = IdentityText(target.Identity);
                InertString? evidenceIdentity =
                    EvidenceText(edge.EvidenceIdentity);
                return new DependencyGraphEdgeRow(
                    edge.Id,
                    edge.SourceNodeId,
                    edge.TargetNodeId,
                    [.. edge.RootOccurrences],
                    Kind(source.Identity),
                    sourceIdentity.ToString(),
                    source.Label.ToString(),
                    edge.Relationship,
                    Kind(target.Identity),
                    targetIdentity.ToString(),
                    target.Label.ToString(),
                    edge.MinimumDepth,
                    edge.Resolution.ToString().ToLowerInvariant(),
                    EvidenceKind(edge.EvidenceIdentity),
                    evidenceIdentity?.ToString())
                {
                    SourceIdentityText = sourceIdentity,
                    SourceText = source.Label,
                    TargetIdentityText = targetIdentity,
                    TargetText = target.Label,
                    EvidenceIdentityText = evidenceIdentity,
                };
            }),
        ];
    }

    internal static void Write(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        OutputFormat format,
        bool tree,
        bool embeddedMermaid,
        bool noHeader,
        bool compactJson)
    {
        if (tree)
        {
            WriteGraph(
                document,
                rows,
                new PlainTextFormatter(),
                markWindowedFragments: true,
                occurrenceAwareRoots: true);
            return;
        }

        switch (format)
        {
            case OutputFormat.Json:
                WriteJson(document, rows, compactJson);
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
                WriteTable(rows, format, noHeader);
                break;
            case OutputFormat.Jsonl:
                WriteJsonLines(rows);
                break;
            case OutputFormat.Mermaid:
                WriteGraph(
                    document,
                    rows,
                    new MermaidFormatter(),
                    markWindowedFragments: false,
                    occurrenceAwareRoots: false);
                break;
            case OutputFormat.Markdown when embeddedMermaid:
                WriteGraph(
                    document,
                    rows,
                    new MarkdownFormatter(MarkdownGraphMode.Mermaid),
                    markWindowedFragments: false,
                    occurrenceAwareRoots: false);
                break;
            default:
                WriteGraph(
                    document,
                    rows,
                    new PlainTextFormatter(),
                    markWindowedFragments: true,
                    occurrenceAwareRoots: true);
                break;
        }
    }

    private static void WriteGraph(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        IMarkoutFormatter formatter,
        bool markWindowedFragments,
        bool occurrenceAwareRoots)
    {
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteGraph(
            ToGraph(
                document,
                rows,
                markWindowedFragments,
                occurrenceAwareRoots));
        writer.Flush();
    }

    private static void WriteTable(
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        OutputFormat format,
        bool noHeader)
    {
        var writer = new MarkoutWriter(
            Console.Out,
            new TableFormatter(!noHeader),
            OutputFormatter.CreateTableWriterOptions(
                tsv: format == OutputFormat.Tsv,
                jsonl: false));
        writer.WriteTable(
            [
                "Roots",
                "Source Kind",
                "Source Identity",
                "Source",
                "Relationship",
                "Target Kind",
                "Target Identity",
                "Target",
                "Minimum Depth",
                "Resolution",
                "Evidence Kind",
                "Evidence Identity",
            ],
            [
                "roots",
                "source_kind",
                "source_identity",
                "source",
                "relationship",
                "target_kind",
                "target_identity",
                "target",
                "minimum_depth",
                "resolution",
                "evidence_kind",
                "evidence_identity",
            ],
            [
                .. rows.Select(row => new[]
                {
                    string.Join(",", row.RootOccurrences),
                    row.SourceKind,
                    row.SourceIdentity,
                    row.Source,
                    row.Relationship,
                    row.TargetKind,
                    row.TargetIdentity,
                    row.Target,
                    row.MinimumDepth.ToString(CultureInfo.InvariantCulture),
                    row.Resolution,
                    row.EvidenceKind ?? "",
                    row.EvidenceIdentity ?? "",
                }),
            ]);
        writer.Flush();
    }

    private static void WriteJsonLines(
        IReadOnlyList<DependencyGraphEdgeRow> rows)
    {
        foreach (DependencyGraphEdgeRow row in rows)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new DependencyGraphJsonLine(
                        row.RootOccurrences,
                        row.SourceKind,
                        row.SourceIdentity,
                        row.Source,
                        row.Relationship,
                        row.TargetKind,
                        row.TargetIdentity,
                        row.Target,
                        row.MinimumDepth,
                        row.Resolution,
                        row.EvidenceKind,
                        row.EvidenceIdentity),
                    DependencyGraphCompactJsonContext.Default
                        .DependencyGraphJsonLine));
        }
    }

    private static void WriteJson(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        bool compact)
    {
        DependencyGraphJsonDocument json = CreateJsonDocument(
            document,
            rows);
        Console.WriteLine(
            JsonSerializer.Serialize(
                json,
                compact
                    ? DependencyGraphCompactJsonContext.Default
                        .DependencyGraphJsonDocument
                    : DependencyGraphJsonContext.Default
                        .DependencyGraphJsonDocument));
    }

    internal static DependencyGraphJsonDocument CreateJsonDocument(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        DependencyEvidenceSourceTokens? tokens = null,
        bool includePackageSelectionEvidence = true,
        bool includePackageDeclarationEvidence = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rows);
        tokens ??= CreatePackageTokens(document);
        HashSet<int> selectedEdgeIds =
        [
            .. rows.Select(static row => row.EdgeId),
        ];
        HashSet<int> selectedNodeIds =
        [
            .. document.Roots.Select(static root => root.NodeId),
            .. rows.SelectMany(static row =>
                new[] { row.SourceNodeId, row.TargetNodeId }),
        ];
        var rootOccurrencesByNode = document.Roots
            .GroupBy(static root => root.NodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static root => root.OccurrenceIndex)
                    .ToArray());
        var json = new DependencyGraphJsonDocument(
            [
                .. document.Nodes
                    .Where(node => selectedNodeIds.Contains(node.Id))
                    .Select(node => new DependencyGraphJsonNode(
                        node.Id,
                        JsonIdentity(node.Identity),
                        node.Label.ToString(),
                        rootOccurrencesByNode.TryGetValue(
                            node.Id,
                            out int[]? occurrences)
                            ? occurrences
                            : [])),
            ],
            [
                .. document.Edges
                    .Where(edge => selectedEdgeIds.Contains(edge.Id))
                    .Select(edge => new DependencyGraphJsonEdge(
                        edge.Id,
                        [.. edge.RootOccurrences],
                        edge.SourceNodeId,
                        edge.TargetNodeId,
                        JsonIdentity(
                            document.Nodes[edge.SourceNodeId].Identity),
                        edge.Relationship,
                        JsonIdentity(
                            document.Nodes[edge.TargetNodeId].Identity),
                        edge.MinimumDepth,
                        edge.Resolution.ToString().ToLowerInvariant(),
                        JsonEvidenceIdentity(
                            edge.EvidenceIdentity),
                        edge.SourcePackageProjectionId,
                        edge.TargetPackageProjectionId,
                        edge.PackageEmissionAuthority,
                        edge.PackageDiagnostics.IsDefaultOrEmpty
                            ? null
                            : [
                                .. edge.PackageDiagnostics.Select(
                                    diagnostic =>
                                        DependsPackageAuthorityFailureJson
                                            .Create(
                                                diagnostic,
                                                tokens)),
                            ])),
            ],
            [
                .. document.DepthBoundaries
                    .Where(boundary =>
                        selectedNodeIds.Contains(boundary.NodeId))
                    .Select(boundary =>
                        new DependencyGraphJsonDepthBoundary(
                            boundary.NodeId,
                            JsonIdentity(
                                document.Nodes[boundary.NodeId].Identity),
                            boundary.PackageProjectionId,
                            boundary.MaximumDepth,
                            [.. boundary.RootOccurrences],
                            boundary.Producer)),
            ],
            [
                .. document.PackageProjections
                    .Where(projection =>
                        selectedNodeIds.Contains(projection.NodeId))
                    .Select(projection =>
                        JsonPackageProjection(
                            projection,
                            tokens,
                            includePackageSelectionEvidence,
                            includePackageDeclarationEvidence)),
            ]);
        return json;
    }

    private static DependencyGraphJsonPackageProjection JsonPackageProjection(
        DependencyGraphPackageProjection projection,
        DependencyEvidenceSourceTokens tokens,
        bool includeSelectionEvidence,
        bool includeDeclarationEvidence) =>
        new(
            projection.Id,
            projection.NodeId,
            projection.Kind,
            projection.Expansion,
            projection.RootOccurrence,
            JsonPackageEvidence(
                projection.Evidence,
                tokens,
                includeSelectionEvidence,
                includeDeclarationEvidence),
            projection.Candidate is { } candidate
                ? new DependencyGraphJsonPackageCandidate(
                    tokens.ProjectCorrespondence(
                        candidate.Correspondence),
                    new DependencyGraphJsonPackageIdentity(
                        candidate.Coordinate.PackageId,
                        candidate.Coordinate.Version),
                    candidate.Kind,
                    candidate.DiscoveryContract is { } contract
                        ? new DependencyGraphJsonVersionDiscoveryContract(
                            contract.ContractVersion,
                            contract.IncludePrerelease,
                            contract.IncludeUnlisted,
                            contract.Limit)
                        : null,
                    [
                        .. candidate.Authorities.Select(authority =>
                            new DependencyGraphJsonPackageAuthority(
                                tokens.ProjectAssociation(
                                    authority.Authority.Association),
                                authority.Authority.Kind,
                                authority.Authority.PersistentCacheKey,
                                tokens.Project(
                                    authority.Observation?.Source),
                                authority.Observation?.DiscoveryContract,
                                authority.Observation?.ListingState)),
                    ])
                : null,
            [
                .. projection.Diagnostics.Select(diagnostic =>
                    DependsPackageAuthorityFailureJson.Create(
                        diagnostic,
                        tokens)),
            ]);

    private static DependencyGraphJsonPackageEvidence? JsonPackageEvidence(
        PackageDependencyEvidenceRoot? root,
        DependencyEvidenceSourceTokens tokens,
        bool includeSelectionEvidence,
        bool includeDeclarationEvidence)
    {
        if (root is null)
            return null;
        var provenance =
            (PackageDependencyEvidenceRootProvenance.Package)root.Provenance;
        return new DependencyGraphJsonPackageEvidence(
            DependencyEvidenceRootIdentityJson.Create(root.Identity),
            provenance.AcquisitionForm,
            provenance.SourceLabel?.ToString(),
            provenance.IdentityProvenance,
            tokens.Project(provenance.Source),
            includeSelectionEvidence ? root.Selection.Status : null,
            includeSelectionEvidence
                ? DependencyEvidenceGroupIdentityJson.CreateOptional(
                    root.Selection.SelectedGroup)
                : null,
            includeSelectionEvidence
                ? DependencyEvidenceGroupOccurrenceJson.CreateOptional(
                    root.Selection.SelectedSourceOccurrence)
                : null,
            includeSelectionEvidence
                ? root.Selection.RequestedFramework?.ToString()
                : null,
            includeSelectionEvidence
                ? root.Selection.SelectedFramework?.ToString()
                : null,
            includeDeclarationEvidence
                ? root.Declaration switch
                {
                    PackageDependencyEvidenceDeclarationResult.Available =>
                        DependencyEvidenceDeclarationState.Available,
                    PackageDependencyEvidenceDeclarationResult.NotApplicable =>
                        DependencyEvidenceDeclarationState.NotApplicable,
                    PackageDependencyEvidenceDeclarationResult.Unavailable =>
                        DependencyEvidenceDeclarationState.Unavailable,
                    PackageDependencyEvidenceDeclarationResult.Failed =>
                        DependencyEvidenceDeclarationState.Failed,
                    _ => throw new InvalidOperationException(
                        "Unknown package declaration state."),
                }
                : null,
            includeDeclarationEvidence
                ? (root.Declaration
                    as PackageDependencyEvidenceDeclarationResult.Available)
                    ?.Completion
                : null);
    }

    private static IEnumerable<PackageSourceResultIdentity?>
        EnumeratePackageSources(DependencyGraphDocument document)
    {
        foreach (DependencyGraphPackageProjection projection in
                 document.PackageProjections)
        {
            yield return DependsAssetDocument.PackageSource(
                projection.Evidence);
            if (projection.Candidate is { } candidate)
            {
                foreach (PackageAcquisitionAuthorityEvidence authority in
                         candidate.Authorities)
                {
                    yield return authority.Observation?.Source;
                }
            }

            foreach (PackageAuthorityFailure diagnostic in
                     projection.Diagnostics)
            {
                yield return diagnostic.ResultSource
                    ?? diagnostic.SourceFailure?.Source;
            }
            foreach (PackageAuthorityFailure diagnostic in
                     document.Edges
                         .Where(edge =>
                             edge.SourcePackageProjectionId == projection.Id
                             || edge.TargetPackageProjectionId
                                == projection.Id)
                         .SelectMany(static edge =>
                             edge.PackageDiagnostics.IsDefault
                                ? []
                                : edge.PackageDiagnostics))
            {
                yield return diagnostic.ResultSource
                    ?? diagnostic.SourceFailure?.Source;
            }
        }
    }

    private static DependencyEvidenceSourceTokens CreatePackageTokens(
        DependencyGraphDocument document)
    {
        DependencyEvidenceSourceTokens tokens =
            DependencyEvidenceSourceTokens.Create(
                EnumeratePackageSources(document));
        foreach (DependencyGraphPackageProjection projection in
                 document.PackageProjections)
        {
            if (projection.Candidate is not { } candidate)
                continue;
            tokens.ProjectCorrespondence(candidate.Correspondence);
            foreach (PackageAcquisitionAuthorityEvidence authority in
                     candidate.Authorities)
            {
                tokens.ProjectAssociation(authority.Authority.Association);
            }
        }
        return tokens;
    }

    internal static Markout.Graph ToGraph(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        bool markWindowedFragments,
        bool occurrenceAwareRoots = false)
    {
        if (occurrenceAwareRoots && document.Roots.Length > 1)
        {
            return ToOccurrenceAwareTreeGraph(
                document,
                rows,
                markWindowedFragments);
        }

        HashSet<int> selectedNodeIds =
        [
            .. document.Roots.Select(static root => root.NodeId),
            .. rows.SelectMany(static row =>
                new[] { row.SourceNodeId, row.TargetNodeId }),
        ];
        HashSet<int> rootNodeIds =
        [
            .. document.Roots.Select(static root => root.NodeId),
        ];
        HashSet<int> fragmentNodeIds = markWindowedFragments
            ? WindowedFragmentNodeIds(document, rows, rootNodeIds)
            : [];
        bool labelRelationships =
            document.Edges.Select(static edge => edge.Relationship)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any();
        List<Markout.GraphNode> graphNodes =
        [
            .. document.Nodes
                .Where(node => selectedNodeIds.Contains(node.Id))
                .Select(node => new Markout.GraphNode(
                    Key(node.Id),
                    RenderNodeLabel(
                        document,
                        node.Id,
                        rootOccurrence: null,
                        fragmentNodeIds.Contains(node.Id)
                            ? "(fragment) "
                            : null))
                {
                    Emphasized = rootNodeIds.Contains(node.Id),
                }),
        ];
        foreach (IGrouping<int, DependencyGraphRootOccurrence> repeated in
                 document.Roots
                     .GroupBy(static root => root.NodeId)
                     .Where(static group => group.Skip(1).Any()))
        {
            DependencyGraphNode node = document.Nodes[repeated.Key];
            foreach (DependencyGraphRootOccurrence occurrence in
                     repeated.Skip(1))
            {
                graphNodes.Add(
                    new Markout.GraphNode(
                        $"root-{occurrence.OccurrenceIndex}",
                        RenderNodeLabel(
                            document,
                            node.Id,
                            occurrence.OccurrenceIndex,
                            "(revisit) "))
                    {
                        Emphasized = true,
                    });
            }
        }

        return new Markout.Graph(
            graphNodes,
            [
                .. rows.Select(row => new Markout.GraphEdge(
                    Key(row.SourceNodeId),
                    Key(row.TargetNodeId))
                {
                    Label = labelRelationships
                        ? row.Relationship
                        : null,
                }),
            ]);
    }

    private static Markout.Graph ToOccurrenceAwareTreeGraph(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        bool markWindowedFragments)
    {
        DependencyGraphRootOccurrence[] orderedRoots =
        [
            .. document.Roots.OrderBy(
                static root => root.OccurrenceIndex),
        ];
        DependencyGraphEdgeRow[] selectedRows = [.. rows];
        Dictionary<int, DependencyGraphEdgeRow[]> rowsBySource =
            selectedRows
                .GroupBy(static row => row.SourceNodeId)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToArray());
        var graphNodes = new List<Markout.GraphNode>();
        var graphEdges = new List<Markout.GraphEdge>();
        var emittedEdges = new HashSet<int>();
        var renderedNodes = new HashSet<int>();
        int renderedNodeOrdinal = 0;

        foreach (DependencyGraphRootOccurrence root in orderedRoots)
        {
            string rootKey = AddNode(
                root.OccurrenceIndex,
                root.NodeId,
                emphasized: true,
                marker: renderedNodes.Contains(root.NodeId)
                    ? "(revisit) "
                    : null);
            Traverse(
                root.OccurrenceIndex,
                root.NodeId,
                rootKey,
                []);
        }

        foreach (DependencyGraphEdgeRow row in selectedRows)
        {
            if (emittedEdges.Contains(row.EdgeId))
                continue;

            DependencyGraphRootOccurrence owner =
                orderedRoots.FirstOrDefault(root =>
                    row.RootOccurrences.Contains(root.OccurrenceIndex))
                ?? throw new InvalidOperationException(
                    $"Dependency graph edge {row.EdgeId} has no matching root occurrence.");
            string sourceKey = AddNode(
                owner.OccurrenceIndex,
                row.SourceNodeId,
                emphasized: row.SourceNodeId == owner.NodeId,
                marker: markWindowedFragments
                    ? "(fragment) "
                    : null);
            Traverse(
                owner.OccurrenceIndex,
                row.SourceNodeId,
                sourceKey,
                []);
        }

        return new Markout.Graph(graphNodes, graphEdges);

        void Traverse(
            int rootOccurrence,
            int nodeId,
            string sourceKey,
            HashSet<int> expandedNodes)
        {
            if (!expandedNodes.Add(nodeId)
                || !rowsBySource.TryGetValue(
                    nodeId,
                    out DependencyGraphEdgeRow[]? outgoing))
            {
                return;
            }

            foreach (DependencyGraphEdgeRow row in outgoing)
            {
                if (!row.RootOccurrences.Contains(rootOccurrence))
                    continue;

                if (emittedEdges.Add(row.EdgeId))
                {
                    string targetKey = AddNode(
                        rootOccurrence,
                        row.TargetNodeId,
                        emphasized: false,
                        marker: renderedNodes.Contains(row.TargetNodeId)
                            ? "(revisit) "
                            : null);
                    graphEdges.Add(
                        new Markout.GraphEdge(sourceKey, targetKey)
                        {
                            Label = row.Relationship,
                        });
                    Traverse(
                        rootOccurrence,
                        row.TargetNodeId,
                        targetKey,
                        expandedNodes);
                    continue;
                }

                if (!LeadsToUnrenderedEdge(
                        rootOccurrence,
                        row.TargetNodeId,
                        expandedNodes,
                        []))
                {
                    continue;
                }

                string connectorKey = AddNode(
                    rootOccurrence,
                    row.TargetNodeId,
                    emphasized: false,
                    marker: "(revisit) ");
                graphEdges.Add(
                    new Markout.GraphEdge(sourceKey, connectorKey));
                Traverse(
                    rootOccurrence,
                    row.TargetNodeId,
                    connectorKey,
                    expandedNodes);
            }
        }

        bool LeadsToUnrenderedEdge(
            int rootOccurrence,
            int nodeId,
            IReadOnlySet<int> expandedNodes,
            HashSet<int> visitedNodes)
        {
            if (expandedNodes.Contains(nodeId)
                || !visitedNodes.Add(nodeId)
                || !rowsBySource.TryGetValue(
                    nodeId,
                    out DependencyGraphEdgeRow[]? outgoing))
            {
                return false;
            }

            foreach (DependencyGraphEdgeRow row in outgoing)
            {
                if (!row.RootOccurrences.Contains(rootOccurrence))
                    continue;
                if (!emittedEdges.Contains(row.EdgeId)
                    || LeadsToUnrenderedEdge(
                        rootOccurrence,
                        row.TargetNodeId,
                        expandedNodes,
                        visitedNodes))
                {
                    return true;
                }
            }
            return false;
        }

        string AddNode(
            int rootOccurrence,
            int nodeId,
            bool emphasized,
            string? marker)
        {
            string key =
                $"r{rootOccurrence}:n{nodeId}:v{renderedNodeOrdinal++}";
            renderedNodes.Add(nodeId);
            graphNodes.Add(
                new Markout.GraphNode(
                    key,
                    RenderNodeLabel(
                        document,
                        nodeId,
                        rootOccurrence,
                        marker))
                {
                    Emphasized = emphasized,
                });
            return key;
        }
    }

    private static string RenderNodeLabel(
        DependencyGraphDocument document,
        int nodeId,
        int? rootOccurrence,
        string? marker)
    {
        int[] depths =
        [
            .. document.DepthBoundaries
                .Where(boundary =>
                    boundary.NodeId == nodeId
                    && (rootOccurrence is null
                        || boundary.RootOccurrences.Contains(
                            rootOccurrence.Value)))
                .Select(static boundary => boundary.MaximumDepth)
                .Distinct()
                .Order(),
        ];
        string boundaryMarker = depths.Length switch
        {
            0 => "",
            1 => $"(bounded at depth {depths[0]}) ",
            _ => $"(bounded at depths {string.Join(", ", depths)}) ",
        };
        return $"{marker}{boundaryMarker}{document.Nodes[nodeId].Label}";
    }

    private static HashSet<int> WindowedFragmentNodeIds(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        HashSet<int> rootNodeIds)
    {
        HashSet<int> selectedEdgeIds =
        [
            .. rows.Select(static row => row.EdgeId),
        ];
        var selectedOutgoing = rows
            .GroupBy(static row => row.SourceNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static row => row.TargetNodeId)
                    .ToArray());
        var reachable = new HashSet<int>(rootNodeIds);
        var pendingReachable = new Stack<int>(rootNodeIds);
        while (pendingReachable.TryPop(out int current))
        {
            if (!selectedOutgoing.TryGetValue(
                    current,
                    out int[]? targets))
            {
                continue;
            }
            foreach (int target in targets)
            {
                if (reachable.Add(target))
                    pendingReachable.Push(target);
            }
        }

        HashSet<int> unreachableNodeIds =
        [
            .. rows.SelectMany(static row =>
                    new[] { row.SourceNodeId, row.TargetNodeId })
                .Where(nodeId => !reachable.Contains(nodeId)),
        ];
        HashSet<int> unreachableTargetIds =
        [
            .. rows
                .Where(row =>
                    unreachableNodeIds.Contains(row.SourceNodeId)
                    && unreachableNodeIds.Contains(row.TargetNodeId))
                .Select(static row => row.TargetNodeId),
        ];
        HashSet<int> naturalRoots =
        [
            .. rows
                .Select(static row => row.SourceNodeId)
                .Where(nodeId =>
                    unreachableNodeIds.Contains(nodeId)
                    && !unreachableTargetIds.Contains(nodeId)),
        ];
        if (naturalRoots.Count > 0)
        {
            unreachableNodeIds.ExceptWith(
                NodesReachableFrom(naturalRoots, selectedOutgoing));
        }

        var fragments = new HashSet<int>(naturalRoots);
        var adjacency = new Dictionary<int, HashSet<int>>();
        foreach (DependencyGraphEdgeRow row in rows)
        {
            if (!unreachableNodeIds.Contains(row.SourceNodeId)
                || !unreachableNodeIds.Contains(row.TargetNodeId))
            {
                continue;
            }
            AddNeighbor(row.SourceNodeId, row.TargetNodeId);
            AddNeighbor(row.TargetNodeId, row.SourceNodeId);
        }

        var remaining = new HashSet<int>(adjacency.Keys);
        while (remaining.Count > 0)
        {
            int start = remaining.First();
            var component = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(start);
            while (pending.TryPop(out int current))
            {
                if (!remaining.Remove(current))
                    continue;
                component.Add(current);
                foreach (int neighbor in adjacency[current])
                    pending.Push(neighbor);
            }

            DependencyGraphEdge? omittedIncoming = document.Edges
                .FirstOrDefault(edge =>
                    component.Contains(edge.TargetNodeId)
                    && !selectedEdgeIds.Contains(edge.Id));
            fragments.Add(
                omittedIncoming?.TargetNodeId
                    ?? rows.First(row =>
                        component.Contains(row.SourceNodeId)).SourceNodeId);
        }

        return fragments;

        static HashSet<int> NodesReachableFrom(
            IEnumerable<int> starts,
            IReadOnlyDictionary<int, int[]> outgoing)
        {
            var result = new HashSet<int>(starts);
            var pending = new Stack<int>(starts);
            while (pending.TryPop(out int current))
            {
                if (!outgoing.TryGetValue(current, out int[]? targets))
                    continue;
                foreach (int target in targets)
                {
                    if (result.Add(target))
                        pending.Push(target);
                }
            }
            return result;
        }

        void AddNeighbor(int source, int target)
        {
            if (!adjacency.TryGetValue(source, out HashSet<int>? neighbors))
            {
                neighbors = [];
                adjacency.Add(source, neighbors);
            }
            neighbors.Add(target);
        }
    }

    private static string Key(int id) =>
        id.ToString(CultureInfo.InvariantCulture);

    internal static string Kind(DependencyGraphNodeIdentity identity) =>
        identity.Kind switch
        {
            DependencyGraphNodeKind.Type => "type",
            DependencyGraphNodeKind.Library => "library",
            DependencyGraphNodeKind.Package => "package",
            DependencyGraphNodeKind.RestoredRoot => "restored-root",
            DependencyGraphNodeKind.RestoredProject => "restored-project",
            DependencyGraphNodeKind.RestoredPackage => "restored-package",
            DependencyGraphNodeKind.PackageBoundary => "package-boundary",
            DependencyGraphNodeKind.PackageFailure => "package-failure",
            DependencyGraphNodeKind.PackageBudget => "package-budget",
            _ => throw new InvalidOperationException(
                "Unknown dependency graph node kind."),
        };

    internal static InertString IdentityText(
        DependencyGraphNodeIdentity identity) =>
        identity switch
        {
            DependencyGraphNodeIdentity.Type type =>
                Field(type.Name),
            DependencyGraphNodeIdentity.Library library =>
                LibraryIdentityText(library.Identity),
            DependencyGraphNodeIdentity.Package package =>
                InertString.Format(
                    TextPolicy.Field,
                    $"{Field(package.Id)}@{Field(package.Version)}"),
            DependencyGraphNodeIdentity.RestoredRoot restored =>
                RestoredSelectionIdentityText(
                    restored.Identity.Selection,
                    "root"),
            DependencyGraphNodeIdentity.RestoredProject restored =>
                InertString.Format(
                    TextPolicy.Field,
                    $"{RestoredSelectionIdentityText(restored.Identity.Selection, "project")}:{Field(restored.Identity.SourceIdentity)}"),
            DependencyGraphNodeIdentity.RestoredPackage restored =>
                InertString.Format(
                    TextPolicy.Field,
                    $"{RestoredSelectionIdentityText(restored.Identity.Selection, "package")}:{Field(restored.Identity.Coordinate.PackageId)}@{Field(restored.Identity.Coordinate.Version)}"),
            DependencyGraphNodeIdentity.PackageBoundary boundary =>
                PackageBoundaryIdentityText(
                    boundary.SourceProjectionIndex,
                    boundary.PackageId,
                    boundary.VersionConstraint),
            DependencyGraphNodeIdentity.PackageFailure failure =>
                PackageBoundaryIdentityText(
                    failure.SourceProjectionIndex,
                    failure.PackageId,
                    failure.VersionConstraint),
            DependencyGraphNodeIdentity.PackageBudget budget =>
                PackageBoundaryIdentityText(
                    budget.SourceProjectionIndex,
                    budget.PackageId,
                    budget.VersionConstraint),
            _ => throw new InvalidOperationException(
                "Unknown dependency graph node identity."),
        };

    internal static DependencyGraphJsonNodeIdentity JsonIdentity(
        DependencyGraphNodeIdentity identity) =>
        identity switch
        {
            DependencyGraphNodeIdentity.Type type => new(
                Kind(identity),
                Field(type.Name).ToString(),
                Library: null,
                Package: null,
                RestoredRoot: null,
                RestoredProject: null,
                RestoredPackage: null,
                Boundary: null),
            DependencyGraphNodeIdentity.Library library => new(
                Kind(identity),
                Type: null,
                JsonLibraryIdentity(library.Identity),
                Package: null,
                RestoredRoot: null,
                RestoredProject: null,
                RestoredPackage: null,
                Boundary: null),
            DependencyGraphNodeIdentity.Package package => new(
                Kind(identity),
                Type: null,
                Library: null,
                new DependencyGraphJsonPackageIdentity(
                    Field(package.Id).ToString(),
                    Field(package.Version).ToString()),
                RestoredRoot: null,
                RestoredProject: null,
                RestoredPackage: null,
                Boundary: null),
            DependencyGraphNodeIdentity.RestoredRoot restored => new(
                Kind(identity),
                Type: null,
                Library: null,
                Package: null,
                DependencyEvidenceRestoredSelectionIdentityJson.Create(
                    restored.Identity.Selection),
                RestoredProject: null,
                RestoredPackage: null,
                Boundary: null),
            DependencyGraphNodeIdentity.RestoredProject restored => new(
                Kind(identity),
                Type: null,
                Library: null,
                Package: null,
                RestoredRoot: null,
                new DependencyEvidenceProjectNodeIdentityJson
                {
                    Selection =
                        DependencyEvidenceRestoredSelectionIdentityJson.Create(
                            restored.Identity.Selection),
                    SourceIdentity = restored.Identity.SourceIdentity,
                },
                RestoredPackage: null,
                Boundary: null),
            DependencyGraphNodeIdentity.RestoredPackage restored => new(
                Kind(identity),
                Type: null,
                Library: null,
                Package: null,
                RestoredRoot: null,
                RestoredProject: null,
                DependencyEvidencePackageNodeIdentityJson.Create(
                    restored.Identity),
                Boundary: null),
            DependencyGraphNodeIdentity.PackageBoundary boundary => new(
                Kind(identity),
                Type: null,
                Library: null,
                Package: null,
                RestoredRoot: null,
                RestoredProject: null,
                RestoredPackage: null,
                JsonBoundary(
                    boundary.SourceProjectionIndex,
                    boundary.DeclarationIdentity,
                    boundary.PackageId,
                    boundary.VersionConstraint)),
            DependencyGraphNodeIdentity.PackageFailure failure => new(
                Kind(identity),
                Type: null,
                Library: null,
                Package: null,
                RestoredRoot: null,
                RestoredProject: null,
                RestoredPackage: null,
                JsonBoundary(
                    failure.SourceProjectionIndex,
                    failure.DeclarationIdentity,
                    failure.PackageId,
                    failure.VersionConstraint)),
            DependencyGraphNodeIdentity.PackageBudget budget => new(
                Kind(identity),
                Type: null,
                Library: null,
                Package: null,
                RestoredRoot: null,
                RestoredProject: null,
                RestoredPackage: null,
                JsonBoundary(
                    budget.SourceProjectionIndex,
                    budget.DeclarationIdentity,
                    budget.PackageId,
                    budget.VersionConstraint)),
            _ => throw new InvalidOperationException(
                "Unknown dependency graph node identity."),
        };

    private static InertString LibraryIdentityText(
        ManagedMetadataIdentity identity) =>
        identity switch
        {
            ManagedMetadataIdentity.Assembly assembly =>
                Field(AssemblyIdentityFormatter.Format(
                    assembly.Identity)),
            ManagedMetadataIdentity.Module module =>
                InertString.Format(
                    TextPolicy.Field,
                    $"{Field(module.Name)}@{module.ModuleVersionId:D}"),
            _ => throw new InvalidOperationException(
                "Unknown managed metadata identity."),
        };

    internal static DependencyGraphJsonLibraryIdentity
        JsonLibraryIdentity(ManagedMetadataIdentity identity) =>
        identity switch
        {
            ManagedMetadataIdentity.Assembly assembly => new(
                "assembly",
                Field(assembly.Identity.Name).ToString(),
                assembly.Identity.Version?.ToString(),
                assembly.Identity.Culture is { } culture
                    ? Field(culture).ToString()
                    : null,
                assembly.Identity.PublicKeyToken is { } token
                    ? Field(token).ToString()
                    : null,
                ModuleVersionId: null),
            ManagedMetadataIdentity.Module module => new(
                "module",
                Field(module.Name).ToString(),
                Version: null,
                Culture: null,
                PublicKeyToken: null,
                ModuleVersionId:
                    module.ModuleVersionId.ToString("D")),
            _ => throw new InvalidOperationException(
                "Unknown managed metadata identity."),
        };

    private static InertString RestoredSelectionIdentityText(
        DotnetInspector.Queries.RestoredProjectSelectionIdentity selection,
        string kind) =>
        InertString.Format(
            TextPolicy.Field,
            $"{kind}:{Field(selection.TargetIdentity)}:{Field(selection.FactsDigest)}");

    private static InertString PackageBoundaryIdentityText(
        int sourceProjectionIndex,
        string packageId,
        string versionConstraint) =>
        InertString.Format(
            TextPolicy.Field,
            $"projection:{sourceProjectionIndex}:{Field(packageId)}:{Field(versionConstraint)}");

    private static DependencyGraphJsonBoundaryIdentity JsonBoundary(
        int sourceProjectionIndex,
        DotnetInspector.Queries.PackageDependencyEvidenceDeclarationIdentity
            declaration,
        string packageId,
        string versionConstraint) =>
        new(
            sourceProjectionIndex,
            DependencyEvidenceDeclarationIdentityJson.Create(declaration),
            packageId,
            versionConstraint);

    private static string ParentIdentityText(
        DotnetInspector.Queries.RestoredProjectGraphParentIdentity parent) =>
        parent switch
        {
            DotnetInspector.Queries.RestoredProjectGraphParentIdentity.Root
                root =>
                $"root:{root.Identity.Selection.TargetIdentity}:{root.Identity.Selection.FactsDigest}",
            DotnetInspector.Queries.RestoredProjectGraphParentIdentity.Project
                project =>
                $"project:{project.Identity.Selection.TargetIdentity}:{project.Identity.Selection.FactsDigest}:{project.Identity.SourceIdentity}",
            DotnetInspector.Queries.RestoredProjectGraphParentIdentity.Package
                package =>
                $"package:{package.Identity.Selection.TargetIdentity}:{package.Identity.Selection.FactsDigest}:{package.Identity.Coordinate.PackageId}@{package.Identity.Coordinate.Version}",
            _ => throw new InvalidOperationException(
                "Unknown restored-project parent identity."),
        };

    private static string? EvidenceKind(
        DependencyGraphEvidenceIdentity? identity) =>
        identity switch
        {
            null => null,
            DependencyGraphEvidenceIdentity.AssemblyReference =>
                "assembly-reference",
            DependencyGraphEvidenceIdentity.PackageVersionConstraint =>
                "package-version-constraint",
            DependencyGraphEvidenceIdentity.PackageDeclaration =>
                "package-declaration",
            DependencyGraphEvidenceIdentity.RestoredProjectRelationship =>
                "restored-project-relationship",
            DependencyGraphEvidenceIdentity.RestoredPackageRelationship =>
                "restored-package-relationship",
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    private static InertString? EvidenceText(
        DependencyGraphEvidenceIdentity? identity) =>
        identity switch
        {
            null => null,
            DependencyGraphEvidenceIdentity.AssemblyReference assembly =>
                Field(AssemblyIdentityFormatter.Format(
                    assembly.Identity)),
            DependencyGraphEvidenceIdentity.PackageVersionConstraint
                constraint => constraint.Value,
            DependencyGraphEvidenceIdentity.PackageDeclaration declaration =>
                declaration.VersionConstraint,
            DependencyGraphEvidenceIdentity.RestoredProjectRelationship
                project =>
                Field(
                    $"{ParentIdentityText(project.Identity.Parent)}->project:{project.Identity.Dependency.SourceIdentity}"),
            DependencyGraphEvidenceIdentity.RestoredPackageRelationship
                package =>
                Field(
                    $"{ParentIdentityText(package.Identity.Parent)}->package:{package.Identity.Dependency.Coordinate.PackageId}@{package.Identity.Dependency.Coordinate.Version}"),
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    private static DependencyGraphJsonEvidenceIdentity?
        JsonEvidenceIdentity(
            DependencyGraphEvidenceIdentity? identity) =>
        identity switch
        {
            null => null,
            DependencyGraphEvidenceIdentity.AssemblyReference assembly =>
                new(
                    "assembly-reference",
                    JsonLibraryIdentity(
                        new ManagedMetadataIdentity.Assembly(
                            assembly.Identity)),
                    PackageVersionConstraint: null,
                    SourceProjection: null,
                    PackageDeclaration: null,
                    RestoredParent: null,
                    RestoredProject: null,
                    RestoredPackage: null),
            DependencyGraphEvidenceIdentity.PackageVersionConstraint
                constraint =>
                new(
                    "package-version-constraint",
                    AssemblyReference: null,
                    constraint.Value.ToString(),
                    SourceProjection: null,
                    PackageDeclaration: null,
                    RestoredParent: null,
                    RestoredProject: null,
                    RestoredPackage: null),
            DependencyGraphEvidenceIdentity.PackageDeclaration declaration =>
                new(
                    "package-declaration",
                    AssemblyReference: null,
                    PackageVersionConstraint:
                        declaration.VersionConstraint.ToString(),
                    declaration.SourceProjectionIndex,
                    DependencyEvidenceDeclarationIdentityJson.Create(
                        declaration.Identity),
                    RestoredParent: null,
                    RestoredProject: null,
                    RestoredPackage: null),
            DependencyGraphEvidenceIdentity.RestoredProjectRelationship
                project =>
                new(
                    "restored-project-relationship",
                    AssemblyReference: null,
                    PackageVersionConstraint: null,
                    SourceProjection: null,
                    PackageDeclaration: null,
                    DependencyEvidenceGraphParentIdentityJson.Create(
                        project.Identity.Parent),
                    new DependencyEvidenceProjectNodeIdentityJson
                    {
                        Selection =
                            DependencyEvidenceRestoredSelectionIdentityJson
                                .Create(
                                    project.Identity.Dependency.Selection),
                        SourceIdentity =
                            project.Identity.Dependency.SourceIdentity,
                    },
                    RestoredPackage: null),
            DependencyGraphEvidenceIdentity.RestoredPackageRelationship
                package =>
                new(
                    "restored-package-relationship",
                    AssemblyReference: null,
                    PackageVersionConstraint: null,
                    SourceProjection: null,
                    PackageDeclaration: null,
                    DependencyEvidenceGraphParentIdentityJson.Create(
                        package.Identity.Parent),
                    RestoredProject: null,
                    DependencyEvidenceEdgeIdentityJson.Create(
                        package.Identity)),
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
