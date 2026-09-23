using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Options;

public sealed record ExternalCallGraphOptions
{
    public required string TypeName { get; init; }
    public required string Member { get; init; }
    public required string RootPackage { get; init; }
    public required string RootTfm { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public MemberCallGraphSupplyChainBaseline SupplyChainBaseline
    {
        get;
        init;
    } = MemberCallGraphSupplyChainBaseline
        .SelfAndRegisteredEcosystems;
    public string[] FirstPartyPackagePrefixes { get; init; } = [];
    public int Depth { get; init; } = 3;
    public int MaxNodes { get; init; } = 25;
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;
    public bool EmbeddedMermaid { get; init; }
    public bool Tree { get; init; }
    public bool Count { get; init; }
    public RowSelectionIntent<string>? RowSelection { get; init; }
    public RowWindow? Rows { get; init; }
    public bool NoHeader { get; init; }
    public bool Verbose { get; init; }
    public NuGetSourceOptions SourceOptions { get; init; } =
        NuGetSourceOptions.Default;
}
