using System.Collections.Immutable;

namespace DotnetInspector.Sections;

/// <summary>
/// Product Vocabulary's routing declaration: one reference entry for the
/// collection of value vocabularies the host composes. Vocabularies are
/// neither inspected nor work performed; they list the values that query
/// inputs accept.
/// </summary>
public static class VocabularyRoutingDeclarations
{
    public static InspectionCapabilityModule Module { get; } = new(
        "product-vocabulary.routing", routingDeclarations: static () => Cache.Declarations);

    private static class Cache
    {
        internal static readonly ImmutableArray<ResourceExplanationRoutingDeclaration>
            Declarations = Create();
    }

    private static ImmutableArray<ResourceExplanationRoutingDeclaration>
        Create() =>
    [
        new(
            ResourceExplanationRoutingRole.Reference,
            ResourceExplanationCatalog.VocabulariesCollectionSegment,
            "Value vocabularies",
            "Product Vocabulary",
            "The stable values that product queries accept, such as API "
            + "accessibility, C# style choices, and C# body kinds, with the "
            + "inputs that accept each one.",
            [
                "Which values may I pass to this option?",
                "Which C# body kinds can I search for?",
                "What does this value mean, and which inputs accept it?",
            ],
            [ResourceExplanationCatalog.VocabulariesCollectionKey],
            grain: "One vocabulary, or one of its values.",
            scope: "The vocabularies this host composes."),
    ];
}
