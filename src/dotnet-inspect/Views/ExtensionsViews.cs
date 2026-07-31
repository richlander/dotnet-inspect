using ILInspector.CSharp;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class ExtensionsResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }

    [MarkoutSection(Name = "Summary")]
    public List<ExtensionCountRow>? Counts { get; set; }

    [MarkoutSection(Name = "Extensions")]
    [MarkoutIgnoreColumnWhen(nameof(OverloadsUniform), nameof(ExtensionRow.Overloads))]
    [MarkoutIgnoreColumnWhen(nameof(TypeUniform), nameof(ExtensionRow.Type))]
    [MarkoutIgnoreColumnWhen(nameof(ViaEmpty), nameof(ExtensionRow.Via))]
    public List<ExtensionRow>? Extensions { get; set; }

    // Hide the Overloads column when every method has a single overload.
    public static bool OverloadsUniform(List<ExtensionRow>? rows)
        => rows is null || rows.All(r => r.Overloads <= 1);

    // Hide the Type column only for all-direct results, where every row echoes the
    // queried type shown in the title. Reachable (indirect) rows carry a distinct
    // resolved type, so keep the column whenever any row has a Via path.
    public static bool TypeUniform(List<ExtensionRow>? rows)
        => rows is null
            || (rows.All(r => string.IsNullOrEmpty(r.Via))
                && rows.Select(r => r.Type).Distinct(StringComparer.Ordinal).Count() <= 1);

    // Hide the Via column when no reachable-path (indirect) extensions are present.
    public static bool ViaEmpty(List<ExtensionRow>? rows)
        => rows is null || rows.All(r => string.IsNullOrEmpty(r.Via));
}

[MarkoutSerializable]
public record ExtensionCountRow(string Type, string Extensions, string Via)
{
    public string Type { get; init; } = CSharpIdentifier.ContainRenderedText(Type);
    public string Extensions { get; init; } = Extensions;
    public string Via { get; init; } = CSharpIdentifier.ContainRenderedText(Via);
}

[MarkoutSerializable]
public record ExtensionRow(
    string Name,
    int Overloads,
    string Kind,
    string Class,
    string Library,
    string Source,
    string Type,
    string Via)
{
    // Redeclared in full, in constructor order; a partial redeclaration would
    // reorder the rendered columns and the serialized keys.
    public string Name { get; init; } = CSharpIdentifier.ContainRenderedText(Name);
    public int Overloads { get; init; } = Overloads;
    public string Kind { get; init; } = Kind;
    public string Class { get; init; } = CSharpIdentifier.ContainRenderedText(Class);
    public string Library { get; init; } = CSharpIdentifier.ContainRenderedText(Library);
    public string Source { get; init; } = CSharpIdentifier.ContainRenderedText(Source);
    public string Type { get; init; } = CSharpIdentifier.ContainRenderedText(Type);
    public string Via { get; init; } = CSharpIdentifier.ContainRenderedText(Via);
}
