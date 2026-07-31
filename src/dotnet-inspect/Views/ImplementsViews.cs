using ILInspector.CSharp;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class ImplementsResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }
    [MarkoutIgnore] public int Matches { get; set; }

    [MarkoutSection(Name = "Implementers")]
    public List<ImplementerRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record ImplementerRow(
    string Type, string Kind, string Relationship, string Library, string Source)
{
    /// <summary>
    /// The implementing type's spelling and its assembly are metadata the
    /// inspected library chose, so both are contained here rather than at the
    /// formatter (issue #3319). Every positional property is redeclared so the
    /// reflected order stays the constructor's.
    /// </summary>
    /// <remarks>
    /// <see cref="Type"/> arrives already contained: its sole caller wraps it
    /// in <c>MarkoutInline.Code</c>, which contains. That call is the gated
    /// owner (<c>UntrustedRelationshipContainmentTests</c>); the repeat here is
    /// defense in depth and is deliberately not claimed as the gate.
    /// <see cref="Library"/> has no containing upstream, so this is its only
    /// owner, gated by <c>ImplementerRowContainmentTests</c>.
    /// </remarks>
    public string Type { get; init; } = CSharpIdentifier.ContainRenderedText(Type);

    /// <inheritdoc cref="Type"/>
    public string Kind { get; init; } = Kind;

    /// <inheritdoc cref="Type"/>
    public string Relationship { get; init; } = Relationship;

    /// <inheritdoc cref="Type"/>
    public string Library { get; init; } = CSharpIdentifier.ContainRenderedText(Library);

    /// <inheritdoc cref="Type"/>
    public string Source { get; init; } = Source;
}
