using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.Research;
using Analysis = ILInspector.Analysis;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Metadata;

namespace DotnetInspect.Web.Interop.Metadata;

/// <summary>
/// API and metadata projection over a package or platform coordinate the shared workspace already
/// owns. This facade acquires no artifact of its own.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class MetadataExports
{
    /// <summary>
    /// One type's metadata projection, produced by
    /// <see cref="AssemblyContextTypeProjectionQuery"/> over the participant that owns the type
    /// and <see cref="AssemblyContextTypeDependencyQuery"/> over the active package Workspace.
    /// The queries own metadata access and resolve references through the group's binding policy;
    /// nothing here opens a source or reads an image.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryTypeProjection(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string workspaceJson)
    {
        BrowserTypeMetadata type = await TypeProjectionAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeId,
            workspaceJson);
        return JsonSerializer.Serialize(
            type,
            BrowserMetadataJsonContext.Default.BrowserTypeMetadata);
    }

    static async Task<BrowserTypeMetadata> TypeProjectionAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string workspaceJson)
    {
        (BrowserPackageRequest[] requests, int rootIndex) =
            TypeProjectionRequests(
                packageId,
                version,
                targetFramework,
                workspaceJson);
        await using BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(requests);
        BrowserInspectionScope scope = resolution.Scope;
        BrowserPackageCoordinate requestedRoot =
            resolution.RequestedCoordinates[rootIndex];
        BrowserPackageCoordinate root = scope.Coordinate(requestedRoot);
        BrowserWorkspaceParticipant participant =
            scope.SurfaceParticipant(
                root,
                root.CompileAsset(assemblyName));

        (ResearchViews.TypeProjectionResult Projection,
            AssemblyContextTypeDependencyResult Dependencies) result =
            scope.UseSurfaceParticipant(
                participant,
                (group, member) =>
                {
                    ResearchViews.TypeProjectionResult projection =
                        BrowserSurfaceProjection.Require(
                            AssemblyContextTypeProjectionQuery.ExecuteParticipant(
                                group,
                                member,
                                new AssemblyContextTypeProjectionRequest(typeId)),
                            $"Type projection for '{typeId}'");
                    return (
                        projection,
                        AssemblyContextTypeDependencyQuery.ExecuteParticipant(
                            group,
                            member,
                            projection.Identity.FullName));
                });
        ResearchViews.TypeProjectionResult projection = result.Projection;
        (BrowserTypeGraphNode[] graphNodes,
            BrowserTypeGraphEdge[] graphEdges) =
            TypeRelationshipGraph(projection, result.Dependencies);

        return new BrowserTypeMetadata(
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
                graphNodes,
                graphEdges,
                [
                    .. projection.InspectionFailures.Select(
                        failure => $"{failure.Operation}: {failure.Detail}"),
                    .. TypeDependencyFailures(scope, result.Dependencies),
                ]);
    }

    internal static (BrowserPackageRequest[] Requests, int RootIndex)
        TypeProjectionRequests(
            string packageId,
            string version,
            string targetFramework,
            string workspaceJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceJson);

        BrowserWorkspacePackage[] workspace =
            JsonSerializer.Deserialize(
                workspaceJson,
                BrowserMetadataJsonContext.Default
                    .BrowserWorkspacePackageArray)
            ?? throw new InvalidOperationException(
                "The Browser Workspace package context is required.");
        BrowserPackageRequest[] requests =
        [
            .. workspace.Select(package => new BrowserPackageRequest(
                package.Package,
                package.Version,
                package.Framework)),
        ];
        int[] rootIndexes =
        [
            .. requests
                .Select((request, index) => (request, index))
                .Where(candidate =>
                    StringComparer.OrdinalIgnoreCase.Equals(
                        candidate.request.PackageId,
                        packageId)
                    && StringComparer.OrdinalIgnoreCase.Equals(
                        candidate.request.Version,
                        version)
                    && StringComparer.OrdinalIgnoreCase.Equals(
                        candidate.request.TargetFramework,
                        targetFramework))
                .Select(candidate => candidate.index),
        ];
        if (rootIndexes.Length != 1)
        {
            throw new InvalidOperationException(
                "The Browser Workspace package context must contain the "
                    + "active package coordinate exactly once.");
        }

        return (requests, rootIndexes[0]);
    }

    static (BrowserTypeGraphNode[] Nodes, BrowserTypeGraphEdge[] Edges)
        TypeRelationshipGraph(
            ResearchViews.TypeProjectionResult projection,
            AssemblyContextTypeDependencyResult dependencies)
    {
        var nodes = new List<BrowserTypeGraphNode>();
        var nodeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var edges = new List<BrowserTypeGraphEdge>();
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);

        void AddNode(string id, string displayName, string role)
        {
            if (nodeIndexes.TryGetValue(id, out int index))
            {
                if (nodes[index].Role != "self"
                    && role == "self")
                {
                    nodes[index] = nodes[index] with { Role = role };
                }
                return;
            }

            nodeIndexes.Add(id, nodes.Count);
            nodes.Add(new BrowserTypeGraphNode(id, displayName, role));
        }

        void AddEdge(string fromId, string toId, string kind)
        {
            if (edgeKeys.Add($"{fromId}\0{toId}\0{kind}"))
                edges.Add(new BrowserTypeGraphEdge(fromId, toId, kind));
        }

        foreach (ResearchViews.TypeRelationshipNode node
                 in projection.Graph?.Nodes ?? [])
        {
            AddNode(
                node.Id,
                node.DisplayName,
                node.Role.ToString().ToLowerInvariant());
        }
        foreach (ResearchViews.TypeRelationshipEdge edge
                 in projection.Graph?.Edges ?? [])
        {
            AddEdge(
                edge.FromId,
                edge.ToId,
                edge.Kind.ToString().ToLowerInvariant());
        }

        string? matchedType = dependencies.Dependency.MatchedType;
        if (matchedType is null)
            return ([.. nodes], [.. edges]);

        var dependencyRoles = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (TypeDependencyRelationship relationship
                 in dependencies.Dependency.Relationships)
        {
            dependencyRoles.TryAdd(
                relationship.TargetTypeName,
                relationship.Kind
                    is TypeDependencyRelationshipKind.BaseType
                        ? "base"
                        : "interface");
        }

        string GraphId(string typeName) =>
            typeName.Equals(matchedType, StringComparison.Ordinal)
                ? projection.Identity.FullName
                : typeName;

        foreach (TypeDependencyRelationship relationship
                 in dependencies.Dependency.Relationships.OrderBy(
                     static relationship => relationship.Ordinal))
        {
            string sourceId = GraphId(relationship.SourceTypeName);
            string targetId = GraphId(relationship.TargetTypeName);
            AddNode(
                sourceId,
                sourceId,
                sourceId.Equals(
                    projection.Identity.FullName,
                    StringComparison.Ordinal)
                    ? "self"
                    : dependencyRoles.GetValueOrDefault(
                        relationship.SourceTypeName,
                        "base"));
            AddNode(
                targetId,
                targetId,
                dependencyRoles[relationship.TargetTypeName]);
            AddEdge(
                sourceId,
                targetId,
                relationship.Kind
                    is TypeDependencyRelationshipKind.BaseType
                        ? "inherits"
                        : "implements");
        }

        return ([.. nodes], [.. edges]);
    }

    static IEnumerable<string> TypeDependencyFailures(
        BrowserInspectionScope scope,
        AssemblyContextTypeDependencyResult dependencies)
    {
        foreach (AssemblyContextTypeDependencyEntry.Rejected rejected
                 in dependencies.Participants.OfType<
                     AssemblyContextTypeDependencyEntry.Rejected>())
        {
            BrowserWorkspaceParticipant? participant =
                scope.SurfaceParticipants.FirstOrDefault(candidate =>
                    ReferenceEquals(
                        candidate.Assembly.Registration,
                        rejected.Subject.Registration));
            string coordinate = participant is null
                ? "an unknown Workspace participant"
                : $"{participant.Coordinate.PackageId}@"
                    + $"{participant.Coordinate.Version}/"
                    + participant.Coordinate.Framework;
            yield return
                $"Type dependencies for {coordinate} were rejected "
                + $"({rejected.Failure.Kind}).";
        }

        if (!dependencies.HasSurvivingParticipant)
        {
            yield return
                "Workspace type dependencies are unavailable because every "
                + "participant was rejected.";
        }
        else if (!dependencies.Dependency.Found)
        {
            yield return
                "Workspace type dependencies could not certify the selected "
                + "type; direct participant relationships are shown only.";
        }
    }

    /// <summary>
    /// The exact API member selected by a call-graph target. Package surfaces keep public types
    /// lean by omitting their non-public members; a graph click is the explicit gesture that
    /// projects one such member from the already bounded implementation surface.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryGraphMemberSurface(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        BrowserGraphMemberSurface surface = await GraphMemberSurfaceAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken);
        return JsonSerializer.Serialize(
            surface,
            BrowserMetadataJsonContext.Default.BrowserGraphMemberSurface);
    }

    static async Task<BrowserGraphMemberSurface> GraphMemberSurfaceAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        await using BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken);
        BrowserWorkspaceParticipant surfaceParticipant = resolved.SurfaceParticipant;
        Analysis.CallGraphMemberResolution resolution = resolved.Member;
        var textBudget = new BrowserSurfaceProjection.BrowserSurfaceTextBudget(
            BrowserApiSurfacePolicy.MaxRetainedTextCharacters);
        textBudget.BeginParticipant();
        BrowserTypeSurfaceInfo projectedType =
            BrowserSurfaceProjection.Type(
                resolution.Type,
                surfaceParticipant.Asset.AssemblyName,
                surfaceParticipant.Asset.Id,
                surfaceParticipant.Assembly.Identity.Name,
                textBudget,
                qualifyId: true,
                selectedMembers: [resolution.Member]);
        BrowserTypeSurface type = BrowserMetadataWireProjection.Project(projectedType);
        BrowserMemberSurface member = type.Api.Single();
        BrowserMemberBodySelector selectedBody =
            member.BodySelectors.SingleOrDefault(
                body => body.Token == resolution.BodyToken)
            ?? throw new InvalidOperationException(
                $"The projected member '{member.Name}' does not retain "
                + $"body 0x{resolution.BodyToken:X8}.");
        textBudget.CommitParticipant();
        return new BrowserGraphMemberSurface(type, selectedBody);
    }
}
