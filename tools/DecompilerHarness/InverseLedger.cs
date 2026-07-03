using System.Reflection;
using System.Text;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Tests.InverseArchitecture;

/// <summary>
/// Reflects <see cref="InverseOfAttribute"/> / <see cref="NotInvertedAttribute"/>
/// on <see cref="IrExpression"/> subclasses and renders the node ledger
/// (docs/design/inverse-architecture.md). Test/tooling only — never the shipped
/// decompiler. The generated markdown is the single source of truth for the
/// ledger table; the committed copy is drift-gated by a test.
/// </summary>
public static class InverseLedger
{
    /// <summary>One generated ledger row: the type assertion an inverse node makes.</summary>
    public sealed record Row(
        string Node,
        string ForwardName,
        Oracle Oracle,
        NameProvenance Naming,
        string Precondition,
        string Witness,
        string? Assumes = null);

    /// <summary>Concrete, non-abstract IR expression node types in the given assembly, name-ordered.</summary>
    public static IReadOnlyList<Type> NodeTypes(Assembly assembly)
        => [.. assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IrExpression).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>The annotated node rows — the generated ledger content.</summary>
    public static IReadOnlyList<Row> Rows(Assembly assembly)
        => [.. NodeTypes(assembly)
            .Select(t => (Node: t, Inverse: t.GetCustomAttribute<InverseOfAttribute>()))
            .Where(x => x.Inverse is not null)
            .Select(x => new Row(
                x.Node.Name,
                x.Inverse!.ForwardName ?? x.Inverse.Forward.ToString(),
                x.Inverse.Oracle,
                x.Inverse.Naming,
                x.Inverse.Precondition ?? "—",
                x.Inverse.Witness ?? "—",
                x.Inverse.Assumes))];

    /// <summary>One declared non-inverse boundary: a node deliberately outside the checkable domain.</summary>
    public sealed record Boundary(string Node, string Reason);

    /// <summary>IR node types marked <see cref="NotInvertedAttribute"/> — declared non-inverse boundaries.</summary>
    public static IReadOnlyList<Boundary> Boundaries(Assembly assembly)
        => [.. NodeTypes(assembly)
            .Select(t => (Node: t, NotInverted: t.GetCustomAttribute<NotInvertedAttribute>()))
            .Where(x => x.NotInverted is not null)
            .Select(x => new Boundary(x.Node.Name, x.NotInverted!.Reason))];

    /// <summary>IR node types carrying neither attribute — the advisory annotation backlog.</summary>
    public static IReadOnlyList<string> Unannotated(Assembly assembly)
        => [.. NodeTypes(assembly)
            .Where(t => t.GetCustomAttribute<InverseOfAttribute>() is null
                     && t.GetCustomAttribute<NotInvertedAttribute>() is null)
            .Select(t => t.Name)];

    /// <summary>Renders the node ledger as deterministic, markdownlint-clean Markdown for the committed generated file.</summary>
    public static string RenderMarkdown(Assembly assembly)
    {
        var sb = new StringBuilder();
        sb.Append("# Inverse node ledger (generated)\n\n");
        sb.Append("Generated from the `[InverseOf]` / `[NotInverted]` annotations in\n");
        sb.Append("`ILInspector.Decompiler` by the test reflector; drift-gated by a test. Do not\n");
        sb.Append("edit by hand. See [inverse-architecture.md](inverse-architecture.md) for the framing.\n\n");
        sb.Append("## Type assertions\n\n");
        sb.Append("| Node | Forward construct (Roslyn) | Oracle (RyuJIT) | Naming | Precondition | Witness |\n");
        sb.Append("| --- | --- | --- | --- | --- | --- |\n");

        var rows = Rows(assembly);
        if (rows.Count == 0)
            sb.Append("| _(none yet)_ | — | — | — | — | — |\n");
        else
            foreach (var row in rows)
                sb.Append($"| `{row.Node}` | {Cell(row.ForwardName)} | {row.Oracle} | {row.Naming} | {Cell(row.Precondition)} | {Cell(row.Witness)} |\n");

        sb.Append("\n## Declared non-inverse boundaries\n\n");
        sb.Append("| Node | Reason |\n");
        sb.Append("| --- | --- |\n");

        var boundaries = Boundaries(assembly);
        if (boundaries.Count == 0)
            sb.Append("| _(none yet)_ | — |\n");
        else
            foreach (var boundary in boundaries)
                sb.Append($"| `{boundary.Node}` | {Cell(boundary.Reason)} |\n");

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a user-authored annotation string for a Markdown table cell. A
    /// literal <c>|</c> in a precondition/forward/witness/reason (e.g. a bitwise
    /// <c>|</c> operator or an IL sequence) would otherwise split the row into
    /// phantom columns; <c>\|</c> is the Markdown table-cell escape.
    /// </summary>
    static string Cell(string value) => value.Replace("|", "\\|");
}
