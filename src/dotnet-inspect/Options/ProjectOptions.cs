using DotnetInspector.Packages;

namespace DotnetInspector.Options;

public record ProjectOptions
{
    public string ProjectPath { get; init; } = ".";

    public bool AgentsIndex { get; init; }

    public string? ReadmePackageId { get; init; }

    public bool Print { get; init; }

    public string? Tfm { get; init; }

    public PackageFileContentScope ContentScope { get; init; } = PackageFileContentScope.Full;

    public bool FrontmatterRequested { get; init; }

    public bool BodyRequested { get; init; }

    public string? OutputPath { get; init; }

    public bool JsonOutput { get; init; }

    public bool OneLine { get; init; }

    public bool Tsv { get; init; }

    public bool Jsonl { get; init; }

    public bool NoHeader { get; init; }

    public bool Bare { get; init; }

    public string[]? Discover { get; init; }

    public bool Tree { get; init; }

    public bool Schema { get; init; }

    public string[]? Select { get; init; }

    public string[]? Columns { get; init; }

    public string[]? Fields { get; init; }

    public bool Count { get; init; }

    public bool Verbose { get; init; }

    public NuGetSourceOptions? SourceOptions { get; init; }
}
