using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ILInspector.Analysis;

namespace ILInspector.CallGraph;

/// <summary>
/// Projects the typed call-graph facts that <c>ILInspector.Analysis</c> produces
/// (<see cref="CallTreeNode"/> caller and callee roots built by
/// <c>LibraryBodyIndex.BuildCallerTree</c> / <c>BuildCallTree</c>) into a single
/// deterministic Mermaid <c>flowchart</c> centered on one selected overload:
/// <code>
/// callers -&gt; selected overload -&gt; callees
/// </code>
/// This is a host-neutral product layer that sits <em>below</em> host applications
/// so <c>dotnet-inspect</c> and the browser-Wasm prototype share one graph
/// semantics and one Mermaid document. It owns the concerns a host must not
/// re-invent: stable node identity, Mermaid escaping, duplicate/shared-node and
/// cycle collapsing, depth-limited / truncated boundary marking, external-node
/// styling, and loop-call edge annotations. It takes no dependency on Markout, the
/// CLI, or inspected-assembly loading and stays SRM-only / NativeAOT / browser-Wasm
/// friendly (see issue #3120).
/// </summary>
public static class CallGraphMermaid
{
    public sealed record Options(
        bool CompactLabels = false,
        bool RelationshipColors = false);

    /// <summary>
    /// Renders the combined caller/target/callee view. Both roots are the selected
    /// overload: <paramref name="callerRoot"/>'s children are its inbound callers
    /// (edges flow <em>into</em> the target) and <paramref name="calleeRoot"/>'s
    /// children are its outbound callees (edges flow <em>out of</em> the target).
    /// Either root may be null (e.g. the browser's first caller-only view), but not
    /// both. When both are supplied they must name the same selected member.
    /// </summary>
    public static string Render(
        CallTreeNode? callerRoot,
        CallTreeNode? calleeRoot,
        Options? options = null)
        => Render(
            CallGraphProjection.Create(callerRoot, calleeRoot),
            options);

    /// <summary>
    /// Renders an existing typed projection without reconstructing node identity from
    /// labels or trees. Mermaid node ids are <c>n{CallGraphNode.Id}</c>, allowing hosts
    /// to bind interactions back to <see cref="CallGraphNode.Member"/>.
    /// </summary>
    public static string Render(
        CallGraphProjection projection,
        Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        options ??= new Options();
        MemberRef target = projection.Focus.Member;
        var sb = new StringBuilder();
        sb.Append("flowchart LR\n");

        foreach (CallGraphNode node in projection.Nodes)
        {
            sb.Append("    ")
                .Append(NodeId(node.Id))
                .Append("[\"")
                .Append(Escape(Label(node.Member, options)))
                .Append("\"]");
            if (!options.RelationshipColors && ClassName(node.Kind) is { } className)
                sb.Append(":::").Append(className);
            sb.Append('\n');
        }

        foreach (CallGraphEdge edge in projection.Edges)
        {
            sb.Append("    ").Append(NodeId(edge.From));
            if (edge.LoopLabel is { } loop)
                sb.Append(" -->|").Append(Escape(loop, edgeLabel: true)).Append("| ");
            else
                sb.Append(" --> ");
            sb.Append(NodeId(edge.To)).Append('\n');
        }

        if (options.RelationshipColors)
        {
            foreach (CallGraphNode node in projection.Nodes)
            {
                sb.Append("    class ")
                    .Append(NodeId(node.Id))
                    .Append(' ')
                    .Append(RelationshipClass(node.Member, target))
                    .Append(";\n");
            }
            sb.Append("    classDef target fill:var(--graph-target-fill),stroke:var(--graph-target-stroke),stroke-width:3px,color:var(--graph-target-text);\n");
            sb.Append("    classDef sameType fill:var(--graph-same-type-fill),stroke:var(--graph-same-type-stroke),stroke-width:2px,color:var(--graph-same-type-text);\n");
            sb.Append("    classDef differentType fill:var(--graph-different-type-fill),stroke:var(--graph-different-type-stroke),stroke-width:2px,color:var(--graph-different-type-text);\n");
            sb.Append("    classDef differentAssembly fill:var(--graph-different-assembly-fill),stroke:var(--graph-different-assembly-stroke),stroke-width:2px,color:var(--graph-different-assembly-text);\n");
        }
        else
        {
            if (projection.Nodes.Any(node => node.Kind == CallGraphNodeKind.Focus))
                sb.Append("    classDef target fill:#dae8fc,stroke:#6c8ebf,stroke-width:2px;\n");
            if (projection.Nodes.Any(node => node.Kind == CallGraphNodeKind.External))
                sb.Append("    classDef external fill:#f5f5f5,stroke:#999999,stroke-dasharray:4 3,color:#666666;\n");
            if (projection.Nodes.Any(node => node.Kind == CallGraphNodeKind.Truncated))
                sb.Append("    classDef truncated fill:#fff2cc,stroke:#d6b656,stroke-dasharray:2 2;\n");
        }

        return sb.ToString();
    }

    /// <summary>Renders the inbound (caller) half only, centered on the selected overload.</summary>
    public static string RenderCallers(CallTreeNode callerRoot)
    {
        ArgumentNullException.ThrowIfNull(callerRoot);
        return Render(callerRoot, null);
    }

    /// <summary>Renders the outbound (callee) half only, centered on the selected overload.</summary>
    public static string RenderCallees(CallTreeNode calleeRoot)
    {
        ArgumentNullException.ThrowIfNull(calleeRoot);
        return Render(null, calleeRoot);
    }

    /// <summary>
    /// A stable structural identity for a member so shared callees, cycles, the
    /// target-as-caller-and-callee, and self-recursion all collapse to one node. This
    /// mirrors the Analysis layer's cross-assembly caller-graph identity
    /// (<see cref="GenericMemberIdentity"/>): the open-definition side (the target root
    /// and caller nodes, which <c>BuildCallerTree</c> builds without method type
    /// arguments) and the constructed-call-site side (callee edges decoded from IL) must
    /// erase to the <em>same</em> key, so a generic target that calls itself collapses
    /// onto <c>n0</c> instead of splitting. Non-generic members keep their exact
    /// instantiated signature — including the return type, which alone separates C#
    /// conversion operators — while same-name / same-arity generic overloads coarsen, the
    /// accepted trade the rest of the product already makes. The declaring type is
    /// assembly-qualified, so same-namespace / same-name types from different assemblies
    /// stay distinct (#1741).
    /// </summary>
    static string IdentityKey(MemberRef member)
    {
        // Mirror LibraryBodyIndex.CallerGraphKey exactly so the projection and the builder
        // compute byte-identical keys for the same MemberRef.
        var eraseGenericSignature = GenericMemberIdentity.ShouldErase(member.DeclaringType, member.ParameterTypes, member.ReturnType, member.TypeArguments);
        var openDeclaring = GenericMemberIdentity.OpenDeclaringType(member.DeclaringType);
        var shape = eraseGenericSignature
            ? GenericMemberIdentity.ErasedParameterShape(member.OpenSignatureParameters)
            : string.Join(",", member.ParameterTypes.Select(GenericMemberIdentity.KeyFragment));
        return $"{GenericMemberIdentity.KeyFragment(openDeclaring)}|{member.Name}|{member.ParameterTypes.Length}|{shape}|{GenericMemberIdentity.KeyFragment(member.OpenSignatureReturn)}";
    }

    static string Label(MemberRef member, Options options)
    {
        if (member.Kind == MemberKind.Unsupported)
            return member.DeclaringType.ToDisplayString();

        if (options.CompactLabels)
            return $"{member.DeclaringType.SimpleName}.{member.Name}";

        var name = member.Name;
        if (!member.TypeArguments.IsDefaultOrEmpty)
            name += "<" + string.Join(", ", member.TypeArguments.Select(type => type.ToDisplayString())) + ">";
        var parameters = string.Join(
            ", ",
            member.ParameterTypes.Select(type => type.ToDisplayString()));
        return $"{member.DeclaringType.ToDisplayString()}.{name}({parameters})";
    }

    static string RelationshipClass(MemberRef member, MemberRef target)
    {
        if (IdentityKey(member) == IdentityKey(target))
            return "target";

        var targetType = GenericMemberIdentity.KeyFragment(
            GenericMemberIdentity.OpenDeclaringType(target.DeclaringType));
        var memberType = GenericMemberIdentity.KeyFragment(
            GenericMemberIdentity.OpenDeclaringType(member.DeclaringType));
        if (string.Equals(targetType, memberType, StringComparison.Ordinal))
            return "sameType";

        var memberAssembly =
            GenericMemberIdentity.OpenDeclaringType(member.DeclaringType).Assembly;
        var targetAssembly =
            GenericMemberIdentity.OpenDeclaringType(target.DeclaringType).Assembly;
        return string.Equals(memberAssembly, targetAssembly, StringComparison.Ordinal)
            ? "differentType"
            : "differentAssembly";
    }

    static string? ClassName(CallGraphNodeKind nodeKind) => nodeKind switch
    {
        CallGraphNodeKind.Focus => "target",
        CallGraphNodeKind.External => "external",
        CallGraphNodeKind.Truncated => "truncated",
        _ => null,
    };

    static string NodeId(int id) => "n" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes text for a Mermaid quoted label / edge label using Mermaid entity codes,
    /// so hostile or unusual member names (quotes, angle brackets, pipes, <c>#</c>)
    /// cannot break out of the label or the flowchart grammar. Node labels are wrapped in
    /// <c>["..."]</c> so brackets/parentheses are already safe there; edge labels
    /// (<c>|...|</c>) are unquoted, so <paramref name="edgeLabel"/> additionally entity-
    /// encodes the structural delimiters that would otherwise corrupt an edge label.
    /// </summary>
    static string Escape(string text, bool edgeLabel = false)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '#': sb.Append("#35;"); break;
                case '"': sb.Append("#quot;"); break;
                case '<': sb.Append("#60;"); break;
                case '>': sb.Append("#62;"); break;
                case '&': sb.Append("#38;"); break;
                case '|': sb.Append("#124;"); break;
                case '\r': sb.Append("#13;"); break;
                case '\n': sb.Append("#10;"); break;
                case '(' when edgeLabel: sb.Append("#40;"); break;
                case ')' when edgeLabel: sb.Append("#41;"); break;
                case '[' when edgeLabel: sb.Append("#91;"); break;
                case ']' when edgeLabel: sb.Append("#93;"); break;
                case '{' when edgeLabel: sb.Append("#123;"); break;
                case '}' when edgeLabel: sb.Append("#125;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
