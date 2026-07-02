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
        NameProvenance Naming,
        string Precondition,
        string Witness);

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
                x.Inverse.Naming,
                x.Inverse.Precondition ?? "—",
                x.Inverse.Witness ?? "—"))];

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
        sb.Append("Generated from the `[InverseOf]` annotations in `ILInspector.Decompiler` by the\n");
        sb.Append("test reflector; drift-gated by a test. Do not edit by hand. See\n");
        sb.Append("[inverse-architecture.md](inverse-architecture.md) for the framing.\n\n");
        sb.Append("| Node | Forward construct | Naming | Precondition | Witness |\n");
        sb.Append("| --- | --- | --- | --- | --- |\n");

        var rows = Rows(assembly);
        if (rows.Count == 0)
            sb.Append("| _(none yet)_ | — | — | — | — |\n");
        else
            foreach (var row in rows)
                sb.Append($"| `{row.Node}` | {row.ForwardName} | {row.Naming} | {row.Precondition} | {row.Witness} |\n");

        return sb.ToString();
    }
}
