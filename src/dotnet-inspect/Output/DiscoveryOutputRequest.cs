using DotnetInspector.Options;

namespace DotnetInspector.Output;

/// <summary>
/// Complete rendering request for <c>-D</c>/<c>--discover</c>.
/// </summary>
/// <remarks>
/// Discovery is a renderer of its own, so passing the command's independent output flags through
/// separate optional parameters allowed a caller to lose part of the request. This contract keeps
/// format, projection, row selection, destination, and tree/header intent together until the
/// shared renderer has validated and answered it.
/// </remarks>
public sealed record DiscoveryOutputRequest : IProjectionOptions
{
    /// <summary>The selected output format.</summary>
    public OutputFormat Format { get; init; } = OutputFormat.Table;

    /// <summary>
    /// Whether tabular output was explicitly requested rather than selected as a command default.
    /// This controls discovery's automatic tree promotion.
    /// </summary>
    public bool TableExplicitlySet { get; init; }

    /// <summary>Whether the caller explicitly selected the output format.</summary>
    public bool FormatExplicitlySet { get; init; }

    /// <summary>Whether plain-text rendering was requested.</summary>
    public bool PlainText { get; init; }

    /// <summary>Header behavior for tabular output.</summary>
    public DiscoveryHeaderPolicy HeaderPolicy { get; init; } = DiscoveryHeaderPolicy.Default;

    /// <summary>Whether an explicit tree was requested or automatic promotion is suppressed.</summary>
    public DiscoveryTreeMode TreeMode { get; init; } = DiscoveryTreeMode.Automatic;

    public bool Count { get; init; }
    public bool Print { get; init; }
    public bool Value { get; init; }
    public bool Urls { get; init; }
    public bool Paths { get; init; }
    public string? OutputPath { get; init; }
    public RowWindow? Rows { get; init; }
    public string[]? Fields { get; init; }
    public string[]? Columns { get; init; }

    /// <summary>True when either projection list carries a requested name.</summary>
    public bool HasColumnProjection =>
        Fields is { Length: > 0 } || Columns is { Length: > 0 };

    /// <summary>True when this request must not be promoted to a tree automatically.</summary>
    public bool SuppressesTreePromotion =>
        TreeMode == DiscoveryTreeMode.SuppressPromotion;

    /// <summary>
    /// Resolves a command's effective format when a low-level caller supplied the legacy output
    /// booleans without also populating its resolved <see cref="OutputFormat"/> property.
    /// </summary>
    public static OutputFormat ResolveFormat(
        OutputFormat format,
        bool json = false,
        bool tsv = false,
        bool jsonl = false,
        bool tabular = false,
        bool plainText = false)
        => json ? OutputFormat.Json
            : jsonl ? OutputFormat.Jsonl
            : tsv ? OutputFormat.Tsv
            : tabular ? OutputFormat.Table
            : plainText ? OutputFormat.PlainText
            : format;

    /// <summary>
    /// Creates a request from a command's payload projection and its resolved output decisions.
    /// </summary>
    public static DiscoveryOutputRequest From(
        IProjectionOptions? projection,
        OutputFormat format,
        bool tree = false,
        bool tableExplicitlySet = false,
        bool? plainText = null,
        bool noHeader = false,
        bool formatExplicitlySet = false)
    {
        bool hasProjection = projection?.Fields is { Length: > 0 }
            || projection?.Columns is { Length: > 0 };
        bool suppressPromotion =
            tableExplicitlySet
            || format is OutputFormat.Tsv or OutputFormat.Jsonl or OutputFormat.Json or OutputFormat.PlainText
            || hasProjection
            || projection?.Rows is not null;

        return new DiscoveryOutputRequest
        {
            Format = format,
            TableExplicitlySet = tableExplicitlySet,
            FormatExplicitlySet = formatExplicitlySet,
            PlainText = plainText ?? format == OutputFormat.PlainText,
            HeaderPolicy = noHeader
                ? DiscoveryHeaderPolicy.Suppress
                : DiscoveryHeaderPolicy.Default,
            TreeMode = tree
                ? DiscoveryTreeMode.Force
                : suppressPromotion
                    ? DiscoveryTreeMode.SuppressPromotion
                    : DiscoveryTreeMode.Automatic,
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

    /// <summary>
    /// Creates a request for direct callers that already have the individual legacy flags.
    /// </summary>
    public static DiscoveryOutputRequest FromLegacy(
        bool tree = false,
        bool markdown = false,
        bool json = false,
        bool tsv = false,
        bool jsonl = false,
        bool plainText = false,
        bool tableExplicitlySet = false,
        bool? showHeader = null,
        IProjectionOptions? projection = null)
    {
        var format = json ? OutputFormat.Json
            : jsonl ? OutputFormat.Jsonl
            : tsv ? OutputFormat.Tsv
            : plainText ? OutputFormat.PlainText
            : markdown ? OutputFormat.Markdown
            : OutputFormat.Table;

        return From(
            projection,
            format,
            tree,
            tableExplicitlySet,
            plainText,
            noHeader: showHeader == false,
            formatExplicitlySet: tableExplicitlySet || format != OutputFormat.Table)
            with
            {
                HeaderPolicy = showHeader is true
                    ? DiscoveryHeaderPolicy.Include
                    : showHeader is false
                        ? DiscoveryHeaderPolicy.Suppress
                        : format == OutputFormat.Tsv
                            ? DiscoveryHeaderPolicy.Include
                            : DiscoveryHeaderPolicy.Default,
            };
    }
}

public enum DiscoveryHeaderPolicy
{
    /// <summary>Use the format's default header behavior.</summary>
    Default,

    /// <summary>Include a tabular header row.</summary>
    Include,

    /// <summary>Suppress a tabular header row.</summary>
    Suppress,
}

public enum DiscoveryTreeMode
{
    /// <summary>Promote to a tree only when the default discovery behavior calls for it.</summary>
    Automatic,

    /// <summary>Render a tree even when the selected format would otherwise be tabular.</summary>
    Force,

    /// <summary>Keep the flat discovery rows and do not auto-promote.</summary>
    SuppressPromotion,
}
