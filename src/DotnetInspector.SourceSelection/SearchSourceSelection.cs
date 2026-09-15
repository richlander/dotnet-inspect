namespace DotnetInspector.SourceSelection;

public enum SearchPlatformFramework
{
    Runtime,
    AspNetCore,
    NetStandard,
}

public enum SearchCandidateIntent
{
    Broad,
    Explicit,
}

public sealed class SearchSourceSelection
{
    internal SearchSourceSelection(
        SourceIntent intent,
        SearchCandidateIntent candidateIntent,
        bool usesImplicitPlatform,
        SearchPlatformFramework[] frameworks,
        SourceSelector.PackageSource[] packages,
        SourceSelector[] otherSources)
    {
        Intent = intent;
        CandidateIntent = candidateIntent;
        UsesImplicitPlatform = usesImplicitPlatform;
        Frameworks = Array.AsReadOnly(frameworks);
        Packages = Array.AsReadOnly(packages);
        OtherSources = Array.AsReadOnly(otherSources);
    }

    public SourceIntent Intent { get; }
    public SearchCandidateIntent CandidateIntent { get; }
    public bool UsesImplicitPlatform { get; }
    public IReadOnlyList<SearchPlatformFramework> Frameworks { get; }
    public IReadOnlyList<SourceSelector.PackageSource> Packages { get; }
    public IReadOnlyList<SourceSelector> OtherSources { get; }
}
