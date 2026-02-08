using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Options for the api command.
/// </summary>
public record ApiOptions
{
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Verbose { get; init; }
    public int? Limit { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;
    public HashSet<string>? MemberFilter { get; init; }
    public bool ShowDocs { get; init; }
    public bool UseLocalDocs { get; init; }
    public bool ShowSamples { get; init; }
    public bool SourceLinkOnly { get; init; }
    public bool BrowsableUrls { get; init; }
    public bool ShowInterfaces { get; init; }
    public bool ShowHierarchy { get; init; }
    public bool IncludeAll { get; init; }
    public string? TypeFilter { get; init; }
    public bool SignaturesOnly { get; init; }
    public bool UnsafeOnly { get; init; }
    public bool CtorOnly { get; init; }
    public bool FieldsOnly { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public HashSet<string>? ExcludeSections { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Returns true if the named section should be rendered.
    /// IncludeSections takes precedence: if set, only listed sections appear.
    /// Otherwise ExcludeSections hides listed sections.
    /// Matching is case-insensitive.
    /// </summary>
    public bool ShouldRenderSection(string name)
    {
        if (IncludeSections != null)
            return IncludeSections.Contains(name, StringComparer.OrdinalIgnoreCase);
        if (ExcludeSections != null)
            return !ExcludeSections.Contains(name, StringComparer.OrdinalIgnoreCase);
        return true;
    }
}
