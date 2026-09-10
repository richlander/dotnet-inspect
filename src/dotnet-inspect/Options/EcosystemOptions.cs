using DotnetInspector.Output;

namespace DotnetInspector.Options;

/// <summary>Configuration for the sectioned ecosystem document.</summary>
public sealed record EcosystemOptions : IProjectionOptions
{
    /// <summary>
    /// One registered ecosystem to narrow the registry to, or null for every ecosystem.
    /// </summary>
    /// <remarks>
    /// A row selector, not a subject route. There is no second operand: drill-in belongs to
    /// <c>library</c>, <c>type</c>, and <c>member</c>.
    /// </remarks>
    public string? Ecosystem { get; init; }

    /// <summary>
    /// The platform framework whose prune inventory to read, such as <c>runtime</c>. Null leaves
    /// the platform-specific sections empty rather than guessing a target.
    /// </summary>
    public string? Framework { get; init; }

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
