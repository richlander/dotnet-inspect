using QuerySpace.Explanation;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Queries;

/// <summary>
/// Declares the accessibility values that <see cref="ApiAccessibility"/> owns as a
/// value vocabulary, so a host can compose them into its vocabulary snapshot
/// without restating a value, label, order, or default. The identity carries the
/// host's catalog identity, so the declaration is produced per catalog; its
/// content is deterministic.
/// </summary>
public static class ApiAccessibilityVocabulary
{
    /// <summary>The stable identity of the accessibility vocabulary within a catalog.</summary>
    public const string AccessibilityId = "api.accessibility";

    /// <summary>The display label of the accessibility vocabulary.</summary>
    public const string AccessibilityLabel = "Accessibility";

    /// <summary>Declares <see cref="ApiAccessibility.Values"/> under <paramref name="catalog"/>.</summary>
    public static VocabularyDefinition Declare(VocabularyCatalogIdentity catalog)
    {
        var identity = new VocabularyIdentity(catalog, AccessibilityId);
        VocabularyMapDefinition order = VocabularyMapDefinition.Scalar(
            identity,
            "order",
            "Order",
            "Product-owned presentation order.",
            ExplanationScalarKind.Integer);
        VocabularyMapDefinition defaultMap = VocabularyMapDefinition.Scalar(
            identity,
            "default",
            "Default",
            "Whether this value participates without an explicit selection.",
            ExplanationScalarKind.Boolean);

        return new(
            identity,
            AccessibilityLabel,
            "Accessibility facets accepted by API type and member inventory queries.",
            [order, defaultMap],
            ApiAccessibility.Values.Select(bucket => new VocabularyTerm(
                new(identity, bucket.Id),
                bucket.Label,
                summary: null,
                [
                    new(order, ExplanationValue.Integer(bucket.Order)),
                    new(defaultMap, ExplanationValue.Boolean(bucket.IsDefault)),
                ])));
    }
}
