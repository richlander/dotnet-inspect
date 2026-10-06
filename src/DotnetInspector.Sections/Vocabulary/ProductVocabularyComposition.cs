using System.Collections.Immutable;

using QuerySpace.Explanation;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Sections;

/// <summary>
/// One owner-declared vocabulary a host ships, with the product query inputs
/// that accept its values.
/// </summary>
/// <remarks>
/// The owner issues <see cref="Vocabulary"/>. The accepted inputs name product
/// query inputs rather than owner terms, so the host supplies them when it
/// composes.
/// </remarks>
public sealed record ProductVocabularyContribution
{
    public ProductVocabularyContribution(
        VocabularyDefinition vocabulary,
        params string[] acceptedBy)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(acceptedBy);
        if (acceptedBy.Length == 0)
        {
            throw new ArgumentException(
                $"Vocabulary '{vocabulary.Identity}' must name at least one "
                + "accepting query input.",
                nameof(acceptedBy));
        }
        foreach (string input in acceptedBy)
            ArgumentException.ThrowIfNullOrWhiteSpace(input, nameof(acceptedBy));

        Vocabulary = vocabulary;
        AcceptedBy = [.. acceptedBy];
    }

    public VocabularyDefinition Vocabulary { get; }

    public ImmutableArray<string> AcceptedBy { get; }
}

/// <summary>
/// Composes the product vocabulary snapshot from the contributions one host
/// ships. Each host supplies its own contribution list; this composition adds
/// only the product catalog identity and the <c>vocabulary.sections</c> index.
/// Equal contribution lists yield one snapshot identity.
/// </summary>
public static class ProductVocabularyComposition
{
    /// <summary>The product vocabulary catalog name.</summary>
    public const string CatalogId = "dotnet-inspect.product";

    /// <summary>The snapshot format version the product composes.</summary>
    public const int FormatVersion = 1;

    /// <summary>The identity of the index vocabulary that lists every section.</summary>
    public const string SectionsId = "vocabulary.sections";

    /// <summary>The index map naming the query inputs that accept each vocabulary.</summary>
    public const string AcceptedByMapId = "accepted_by";

    /// <summary>The display label of the index vocabulary.</summary>
    public const string SectionsLabel = "Vocabulary Sections";

    /// <summary>The product vocabulary catalog identity every contribution declares under.</summary>
    public static VocabularyCatalogIdentity Catalog { get; } = new(CatalogId);

    /// <summary>
    /// Composes one exactly identified snapshot: the section index first, then
    /// each contributed vocabulary in the supplied order.
    /// </summary>
    public static VocabularySnapshot Compose(
        IEnumerable<ProductVocabularyContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ImmutableArray<ProductVocabularyContribution> items = [.. contributions];
        foreach (ProductVocabularyContribution item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(contributions));
            if (item.Vocabulary.Identity.Catalog != Catalog)
            {
                throw new ArgumentException(
                    $"Vocabulary '{item.Vocabulary.Identity}' is declared under "
                    + $"catalog '{item.Vocabulary.Identity.Catalog}', not the "
                    + $"product catalog '{Catalog}'.",
                    nameof(contributions));
            }
        }

        return VocabularySnapshot.Create(
            FormatVersion,
            Catalog,
            [
                CreateSectionIndex(items),
                .. items.Select(item => item.Vocabulary),
            ]);
    }

    private static VocabularyDefinition CreateSectionIndex(
        ImmutableArray<ProductVocabularyContribution> items)
    {
        var identity = new VocabularyIdentity(Catalog, SectionsId);
        VocabularyMapDefinition acceptedBy = VocabularyMapDefinition.Scalar(
            identity,
            AcceptedByMapId,
            "Accepted By",
            "Typed query inputs that consume these values.",
            ExplanationScalarKind.Text,
            VocabularyMapCardinality.OneOrMore);
        VocabularyMapDefinition values = VocabularyMapDefinition.Scalar(
            identity,
            "values",
            "Values",
            "Number of legal values.",
            ExplanationScalarKind.Integer);

        return new(
            identity,
            SectionsLabel,
            "Product-owned vocabularies available as rich-query inputs.",
            [acceptedBy, values],
            items.Select(item => new VocabularyTerm(
                new(identity, item.Vocabulary.Identity.Value),
                item.Vocabulary.DisplayLabel,
                item.Vocabulary.Summary,
                [
                    new(
                        acceptedBy.Identity,
                        item.AcceptedBy.Select(ExplanationValue.Text)),
                    new(
                        values,
                        ExplanationValue.Integer(item.Vocabulary.Terms.Length)),
                ])));
    }
}
