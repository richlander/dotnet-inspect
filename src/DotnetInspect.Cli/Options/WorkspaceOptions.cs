using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Options;

public sealed record WorkspaceOptions
{
    public string[] Packages { get; init; } = [];
    public string? Tfm { get; init; }
    public string? Packet { get; init; }
    public WorkspacePackageComponentPath? UpdatePackage { get; init; }
    public string? ReplacementVersion { get; init; }
    public string? ReplacementTfm { get; init; }
    public bool EnvelopeOutput { get; init; }
    internal WorkspaceRegistrationInput[] OrderedRegistrations { get; init; } =
        [];
    public string[] RegisteredLibraries { get; init; } = [];
    public string[] RegisteredPackagePrefixes { get; init; } = [];
    public string[] RegisteredEcosystems { get; init; } = [];
    public WorkspaceTopLevelInventoryEntryKind[] InventoryKinds { get; init; } =
        [];

    public bool Inventory { get; init; }

    public bool IncludePrerelease { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;
    internal bool FormatExplicitlySet { get; init; }
    internal bool FormatFlagExplicitlySet { get; init; }
    internal bool TabularExplicitlySet { get; init; }
    internal bool MarkdownExplicitlySet { get; init; }
    public bool Count { get; init; }
    internal RowSelectionIntent<string>? RowSelection { get; init; }
    public RowWindow? Rows { get; init; }
    public bool NoHeader { get; init; }
    public bool Verbose { get; init; }
    public WorkspaceShareFormat? ShareFormat { get; init; }
    public bool MakePackageDependenciesExplicit { get; init; }
    public WorkspacePackageSourceDefinition[] PackageSources { get; init; } =
        [];
    internal WorkspacePatBindingInput[] PatBindings { get; init; } = [];
    public NuGetSourceOptions SourceOptions { get; init; } =
        NuGetSourceOptions.Default;
}

internal enum WorkspacePatInputKind
{
    Environment,
    StandardInput,
    File,
}

internal sealed record WorkspacePatBindingInput(
    string Endpoint,
    string Username,
    WorkspacePatInputKind Kind,
    string? Value);

internal enum WorkspaceRegistrationInputKind
{
    ExactLibrary,
    PackagePrefix,
    Ecosystem,
}

internal sealed record WorkspaceRegistrationInput(
    WorkspaceRegistrationInputKind Kind,
    string Value);
