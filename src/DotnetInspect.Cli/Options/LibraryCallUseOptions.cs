using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Options;

public sealed record LibraryCallUseOptions : IProjectionOptions
{
    public string[] Libraries { get; init; } = [];
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;
    public bool Count { get; init; }
    public RowWindow? Rows { get; init; }
    public bool NoHeader { get; init; }
    public bool Verbose { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public string[]? Discover { get; init; }
    public string[]? Select { get; init; }
    public bool SelectDefault { get; init; }
    public bool Schema { get; init; }
    public bool Tree { get; init; }
}
