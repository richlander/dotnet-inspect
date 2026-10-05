using DotnetInspect.Cli.Commands;
using DotnetInspector.Sections;
using DotnetInspector.Vocabulary;

namespace DotnetInspect.Cli.Tests;

/// <summary>The Product Vocabulary document the CLI projects from its composed snapshot.</summary>
internal static class CliVocabularyDocument
{
    private static readonly Lazy<VocabularyDocument> Source =
        new(() => VocabularyCatalog.ProjectDocument(CliVocabularyComposition.Snapshot));

    public static VocabularyDocument Document => Source.Value;

    public static VocabularySection GetById(string id) =>
        Document.Sections.FirstOrDefault(section => section.Id == id)
        ?? throw new ArgumentException($"Unknown vocabulary section ID '{id}'.", nameof(id));
}
