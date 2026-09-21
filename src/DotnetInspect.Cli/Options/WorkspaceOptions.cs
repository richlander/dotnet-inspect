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
    public int? ReplacePackage { get; init; }
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

    /// <summary>
    /// An owner-issued package Root reopening token, opened exactly as issued.
    /// </summary>
    /// <remarks>
    /// The token is opaque: only the Artifact owner's decoder reads it, and
    /// this command never reconstructs an opening target from a package id,
    /// version, asset path, or any other display field.
    /// </remarks>
    public string? RootRequest { get; init; }

    /// <summary>
    /// One-based ordered Package occurrence to evaluate as Navigation context.
    /// </summary>
    public int? ActivePackage { get; init; }

    /// <summary>The exact owner-issued compile asset id for a Library.</summary>
    public string? Library { get; init; }

    /// <summary>Use the aggregate Library subject as the descendant source.</summary>
    public bool AllLibraries { get; init; }

    /// <summary>An exact returned metadata Type full name.</summary>
    public string? Type { get; init; }

    /// <summary>An exact returned Member stable selector.</summary>
    public string? Member { get; init; }

    /// <summary>The exact destination view-facet id.</summary>
    public string? Lens { get; init; }

    public bool IncludePrerelease { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;
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
