using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Vocabulary;

/// <summary>
/// Names the product vocabulary sections and projects a host-composed snapshot
/// to the CLI-compatible Product Vocabulary document.
/// </summary>
public static class VocabularyCatalog
{
    /// <summary>The section that describes the vocabulary sections themselves.</summary>
    public const string SectionsSection = ProductVocabularyComposition.SectionsLabel;

    /// <summary>The API accessibility vocabulary section.</summary>
    public const string AccessibilitySection = ApiAccessibilityVocabulary.AccessibilityLabel;

    /// <summary>The C# style-tier vocabulary section.</summary>
    public const string StyleTiersSection = StyleOptionVocabularies.StyleTiersLabel;

    /// <summary>The selectable C# style-choice vocabulary section.</summary>
    public const string StyleChoicesSection = StyleOptionVocabularies.StyleChoicesLabel;

    /// <summary>The exact rendered C# body-kind vocabulary section.</summary>
    public const string BodyKindsSection = BodyShapeVocabulary.BodyKindsLabel;

    /// <summary>The Package Query durable-row field vocabulary section.</summary>
    public const string PackageQueryDurableRowSection =
        PackageQueryDurableRowContract.VocabularyLabel;

    /// <summary>
    /// Projects one host-composed snapshot to the Product Vocabulary document.
    /// A snapshot that lacks a product section fails visibly.
    /// </summary>
    public static VocabularyDocument ProjectDocument(VocabularySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ProductVocabularyCompatibility.Create(snapshot);
    }
}
