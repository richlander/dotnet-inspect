using DotnetInspector.Sections;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Vocabulary;

/// <summary>Composes product-owned query vocabularies without reclassifying their values.</summary>
public static class VocabularyCatalog
{
    /// <summary>The section that describes the vocabulary sections themselves.</summary>
    public const string SectionsSection = "Vocabulary Sections";

    /// <summary>The API accessibility vocabulary section.</summary>
    public const string AccessibilitySection = "Accessibility";

    /// <summary>The C# style-tier vocabulary section.</summary>
    public const string StyleTiersSection = "C# Style Tiers";

    /// <summary>The selectable C# style-choice vocabulary section.</summary>
    public const string StyleChoicesSection = "C# Style Choices";

    /// <summary>The exact rendered C# body-kind vocabulary section.</summary>
    public const string BodyKindsSection = "C# Body Kinds";

    /// <summary>The exact immutable product vocabulary snapshot.</summary>
    public static VocabularySnapshot Snapshot { get; } =
        ProductVocabularySnapshot.Create();

    private static readonly Lazy<VocabularyDocument> DocumentSource =
        new(() => ProductVocabularyCompatibility.Create(Snapshot));

    /// <summary>The current CLI-compatible vocabulary document.</summary>
    public static VocabularyDocument Document => DocumentSource.Value;

    /// <summary>Projects one typed snapshot to the existing Product Vocabulary document.</summary>
    public static VocabularyDocument ProjectDocument(VocabularySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ReferenceEquals(snapshot, Snapshot)
            && (snapshot.Catalog != Snapshot.Catalog
                || snapshot.Identity != Snapshot.Identity))
        {
            throw new ArgumentException(
                $"Snapshot '{snapshot.Identity}' is not the current Product "
                + $"Vocabulary snapshot '{Snapshot.Identity}'.",
                nameof(snapshot));
        }
        return Document;
    }

    /// <summary>Returns the section with the exact stable <paramref name="id"/>.</summary>
    public static VocabularySection GetById(string id) =>
        Document.Sections.FirstOrDefault(section => section.Id == id)
        ?? throw new ArgumentException($"Unknown vocabulary section ID '{id}'.", nameof(id));
}
