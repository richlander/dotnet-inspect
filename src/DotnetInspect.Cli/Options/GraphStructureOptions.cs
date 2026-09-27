using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Options;

public sealed record GraphStructureOptions
{
    public string? Library { get; init; }
    public string? Package { get; init; }
    public string? Tfm { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;
    public bool Envelope { get; init; }
    public bool CompactJson { get; init; }
    public string? OutputPath { get; init; }
    public string[]? Select { get; init; }
    public bool NoHeader { get; init; }
    public bool Verbose { get; init; }
}
