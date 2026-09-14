using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Complete presentation request for <c>-D</c>/<c>--discover</c>.
/// </summary>
public sealed record DiscoveryOutputRequest : IProjectionOptions
{
    public OutputFormat Format { get; init; } = OutputFormat.Table;

    public bool TableExplicitlySet { get; init; }

    public bool Tree { get; init; }

    public bool NoHeader { get; init; }

    public int Verbosity { get; init; }

    public bool Count { get; init; }

    public bool Print { get; init; }

    public bool Value { get; init; }

    public bool Urls { get; init; }

    public bool Paths { get; init; }

    public string? OutputPath { get; init; }

    public RowWindow? Rows { get; init; }

    public string[]? Fields { get; init; }

    public string[]? Columns { get; init; }

    internal bool AllowsAutomaticTreePromotion =>
        !TableExplicitlySet
        && Format is OutputFormat.Table or OutputFormat.Markdown;

    public static DiscoveryOutputRequest Create(
        OutputFormat format,
        bool tree = false,
        bool tableExplicitlySet = false,
        bool noHeader = false,
        int verbosity = 0,
        IProjectionOptions? projection = null)
        => new()
        {
            Format = format,
            TableExplicitlySet = tableExplicitlySet,
            Tree = tree,
            NoHeader = noHeader,
            Verbosity = verbosity,
            Count = projection?.Count ?? false,
            Print = projection?.Print ?? false,
            Value = projection?.Value ?? false,
            Urls = projection?.Urls ?? false,
            Paths = projection?.Paths ?? false,
            OutputPath = projection?.OutputPath,
            Rows = projection?.Rows,
            Fields = projection?.Fields,
            Columns = projection?.Columns,
        };
}
