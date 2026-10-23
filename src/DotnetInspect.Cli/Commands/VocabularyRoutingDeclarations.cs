using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Product Vocabulary's routing declaration: one reference entry for the
/// collection of value vocabularies the CLI composes. Vocabularies are
/// neither inspected nor work performed; they list the values that query
/// inputs accept.
/// </summary>
internal static class VocabularyRoutingDeclarations
{
    public static ImmutableArray<ResourceExplanationRoutingDeclaration>
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
