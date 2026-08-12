using DotnetInspector.Output;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration for an exact rendered body-shape search.
/// </summary>
public sealed record BodyShapeOptions : IProjectionOptions
{
    public string Kind { get; init; } = "";
    public string LibraryPath { get; init; } = "";
    public bool IncludeAll { get; init; }
    public int? MatchLimit { get; init; }
    public RowWindow? Rows { get; init; }
    public bool Count { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public bool Verbose { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }
    internal PrinterOptions? RenderOptions { get; init; }
    internal DotnetInspector.Services.RenderConfigWarningSink? RenderConfigWarnings { get; init; }
    internal string? PdbPath { get; init; }
}
