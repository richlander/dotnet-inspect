using System.Text;

namespace ILInspector.CSharp;

/// <summary>
/// Neutral description of a single-file C# compilation unit: assembly/module
/// attribute bodies (without the surrounding <c>[assembly: ]</c>/<c>[module: ]</c>
/// syntax), <c>using</c> namespaces, and the type print requests to render. This
/// DTO carries no compile-back planning vocabulary, so callers outside the C#
/// layer can describe a unit without depending on printer internals.
/// </summary>
public sealed record CSharpCompilationUnitSpec(
    IReadOnlyList<string> AssemblyAttributes,
    IReadOnlyList<string> ModuleAttributes,
    IReadOnlyList<string> Usings,
    IReadOnlyList<CSharpTypePrintRequest> PrintRequests);

/// <summary>
/// Assembles a <see cref="CSharpCompilationUnitSpec"/> into C# source text: a
/// leading <c>#pragma warning disable</c>, assembly/module attributes, escaped and
/// de-duplicated <c>using</c> directives in ordinal order, then the block-scoped
/// type declarations. This is the authoritative single-file composer for the C#
/// layer; consumers must not reimplement it.
/// </summary>
public static class CSharpCompilationUnitComposer
{
    public static string Compose(CSharpCompilationUnitSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        foreach (var attribute in spec.AssemblyAttributes)
            sb.AppendLine($"[assembly: {attribute}]");
        foreach (var attribute in spec.ModuleAttributes)
            sb.AppendLine($"[module: {attribute}]");
        foreach (var ns in spec.Usings.Select(CSharpFormatter.EscapeNamespace).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            sb.AppendLine($"using {ns};");

        var printed = new CSharpTypePrinter().PrintBatch(
            spec.PrintRequests,
            new CSharpTypePrintOptions
            {
                IncludeCustomAttributes = true,
                NamespaceStyle = CSharpNamespaceStyle.BlockScoped,
            });
        foreach (var unit in printed.Units)
            sb.AppendLine(unit.Source);

        return sb.ToString();
    }
}
