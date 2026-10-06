using QuerySpace.Vocabulary;

namespace ILInspector.Decompiler;

/// <summary>
/// Declares the exact rendered C# body kinds that <see cref="BodyShapeSearch"/>
/// accepts as a value vocabulary, labeled by <see cref="AnnotatedSourceNodeKinds"/>,
/// so a host can compose them into its vocabulary snapshot without restating a
/// kind, label, or order. The identity carries the host's catalog identity, so the
/// declaration is produced per catalog; its content is deterministic.
/// </summary>
public static class BodyShapeVocabulary
{
    /// <summary>The stable identity of the body-kind vocabulary within a catalog.</summary>
    public const string BodyKindsId = "csharp.body-kinds";

    /// <summary>The display label of the body-kind vocabulary.</summary>
    public const string BodyKindsLabel = "C# Body Kinds";

    /// <summary>Declares <see cref="BodyShapeSearch.SupportedKinds"/> under <paramref name="catalog"/>.</summary>
    public static VocabularyDefinition Declare(VocabularyCatalogIdentity catalog)
    {
        var identity = new VocabularyIdentity(catalog, BodyKindsId);
        return new(
            identity,
            BodyKindsLabel,
            "Exact rendered C# syntax kinds accepted by body queries.",
            maps: [],
            BodyShapeSearch.SupportedKinds.Select(kind => new VocabularyTerm(
                new(identity, kind),
                AnnotatedSourceNodeKinds.GetDisplayLabel(kind),
                summary: null)));
    }
}
