using DotnetInspector.Output;

namespace DotnetInspector.Options;

/// <summary>Configuration for the sectioned product vocabulary document.</summary>
public sealed record VocabularyOptions : IProjectionOptions
{
    public string[]? Discover { get; init; }
    public string[]? Select { get; init; }
    public bool SelectDefault { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public bool Schema { get; init; }
    public bool Tree { get; init; }
    public bool Count { get; init; }
    public RowWindow? Rows { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;
    public bool JsonOutput { get; init; }
    public bool PlainText { get; init; }
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
}
