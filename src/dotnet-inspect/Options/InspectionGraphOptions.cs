using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Options;

public sealed record InspectionGraphOptions
{
    public string[] Packages { get; init; } = [];
    public string? Tfm { get; init; }
    public string[] Relationships { get; init; } = [];
    public bool IncludePrerelease { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;
    public bool EmbeddedMermaid { get; init; }
    public bool Tree { get; init; }
    public bool Count { get; init; }
    public RowWindow? Rows { get; init; }
    public bool NoHeader { get; init; }
    public bool Verbose { get; init; }
    public NuGetSourceOptions SourceOptions { get; init; } =
        NuGetSourceOptions.Default;
}
