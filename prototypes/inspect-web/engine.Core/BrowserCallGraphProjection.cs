using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using DotnetInspector.Queries;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace InspectWeb.Engine;

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
    string? SurfaceAssemblyId);

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

internal sealed record BrowserCallGraphDiagnosticsInfo(
    int IncompleteNodes,
    int IncompleteEdges,
    int BindingIdentityConflicts,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary);

internal sealed record BrowserCallGraphInfo(
    string Mermaid,
    BrowserCallGraphNodeInfo Callers,
    BrowserCallGraphNodeInfo Callees,
    BrowserCallGraphScopeInfo Scope,
    BrowserCallGraphTargetInfo[] Targets,
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
            Diagnostics(
                view.Diagnostics,
                projection.HasUnexploredTraversalBoundary,
                projection.HasAnalysisFailureBoundary),
            NoBody: view.CalleeRoot is null && view.CallerRoot is null);
    }

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
            surfaceAssemblyId);
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
            hasAnalysisFailureBoundary);

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
