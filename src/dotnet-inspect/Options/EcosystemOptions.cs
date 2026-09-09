using DotnetInspector.Output;

namespace DotnetInspector.Options;

/// <summary>Configuration for product ecosystem-pack catalog inspection.</summary>
public sealed record EcosystemOptions : IProjectionOptions
{
    public string? Ecosystem { get; init; }
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
    public bool NoHeader { get; init; }
}
