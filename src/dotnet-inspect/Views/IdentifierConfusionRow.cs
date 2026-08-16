using DotnetInspector.Models;
using InertText;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable]
public sealed record IdentifierConfusionRow(
    [property: MarkoutIgnore] InertString LocationText,
    [property: MarkoutIgnore] InertString KindText,
    [property: MarkoutIgnore] InertString ConcernText,
    [property: MarkoutIgnore] InertString? ReservedPrefixText,
    [property: MarkoutIgnore] InertString? SimilarityText,
    [property: MarkoutIgnore] InertString CharactersText)
{
    public string Location => LocationText.ToString();
    public string Kind => KindText.ToString();
    public string Concern => ConcernText.ToString();
    [MarkoutPropertyName("Reserved Prefix")]
    public string? ReservedPrefix => PackageViewText.Render(ReservedPrefixText);
    public string? Similarity => PackageViewText.Render(SimilarityText);
    public string Characters => CharactersText.ToString();
}

internal static class IdentifierConfusionRows
{
    public static List<IdentifierConfusionRow> Create(
        IReadOnlyList<IdentifierConfusionCase> cases)
        => cases.Select(value => new IdentifierConfusionRow(
                Field(value.Location),
                Field(value.Kind),
                Field(IdentifierConfusionAudit.DescribeConcern(value.Confusion)),
                value.Confusion.ReservedPrefixMatch is { } match
                    ? Field(match.ReservedPrefix)
                    : null,
                IdentifierConfusionAudit.DescribeSimilarity(value.Confusion) is { } similarity
                    ? Field(similarity)
                    : null,
                Field(IdentifierConfusionAudit.DescribeCharacters(value.Confusion))))
            .ToList();

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
